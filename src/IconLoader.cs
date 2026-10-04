using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitLauncher
{
    internal sealed class IconStamp
    {
        public string Path;
        public long Modified, Length;
        public static IconStamp Read(string path)
        {
            var stamp = new IconStamp { Path = path, Modified = -1, Length = -1 };
            try { var file = new FileInfo(path); if (file.Exists) { stamp.Modified = file.LastWriteTimeUtc.Ticks; stamp.Length = file.Length; } }
            catch { }
            return stamp;
        }
    }
    // Read-only Shell operations run on Assets' background STA workers.
    internal static class IconLoader
    {
        public static bool Unchanged(IconStamp[] stamps)
        {
            return stamps != null && stamps.All(s => { var now = IconStamp.Read(s.Path); return now.Modified == s.Modified && now.Length == s.Length; });
        }
        public static void Load(string path, AssetInfo result)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool retry;
            result.Icon = Read(path, files, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, out retry);
            result.NeedsRetry = retry;
            result.Dependencies = files.Select(IconStamp.Read).ToArray();
        }
        static ImageSource Read(string path, HashSet<string> files, HashSet<string> visited, int depth, out bool retry)
        {
            retry = false;
            if (Targets.IsWeb(path) || Assets.Remote(path)) return null;
            if (String.IsNullOrWhiteSpace(path) || depth > 4 || !visited.Add(path)) { retry = true; return null; }
            files.Add(path);
            if (!File.Exists(path) && !Directory.Exists(path)) { retry = true; return null; }
            string extension = System.IO.Path.GetExtension(path);
            if (String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string target, icon; int index;
                if (Shortcut(path, out target, out icon, out index))
                {
                    icon = Reference(icon, path);
                    if (!String.IsNullOrEmpty(icon) && !Assets.Remote(icon))
                    {
                        files.Add(icon);
                        var custom = Extract(icon, index); if (custom != null) return custom;
                    }
                    target = Reference(target, path);
                    if (!String.IsNullOrEmpty(target) && !Assets.Remote(target))
                    {
                        bool targetRetry; var resolved = Read(target, files, visited, depth + 1, out targetRetry);
                        if (resolved != null && !targetRetry) return resolved;
                    }
                }
                bool generic; var shell = Shell(path, out generic);
                retry = shell == null || generic;
                return retry ? null : shell;
            }
            bool defaultIcon; var source = Shell(path, out defaultIcon);
            bool executable = String.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase);
            if (source != null && (!defaultIcon || !executable)) return source;
            if (executable) { var extracted = Extract(path, 0); if (extracted != null) return extracted; }
            retry = source == null || executable;
            return retry ? null : source;
        }
        static string Reference(string path, string shortcut)
        {
            path = Targets.Clean(path);
            if (String.IsNullOrWhiteSpace(path) || Targets.IsWeb(path)) return path;
            try { return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(shortcut), path)); }
            catch { return ""; }
        }
        internal static bool Shortcut(string path, out string target, out string icon, out int index)
        {
            target = icon = ""; index = 0; object shell = null, link = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type);
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var linkType = link.GetType();
                target = (string)linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                string location = (string)linkType.InvokeMember("IconLocation", BindingFlags.GetProperty, null, link, null);
                int comma = location.LastIndexOf(',');
                if (comma >= 0 && Int32.TryParse(location.Substring(comma + 1).Trim(), out index)) icon = location.Substring(0, comma).Trim(); else icon = location;
                return true;
            }
            catch { return false; }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
        static ImageSource Shell(string path, out bool generic)
        {
            generic = true; ShellInfo info = new ShellInfo();
            try
            {
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(ShellInfo)), 0x100 | 0x4000) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
                ShellInfo fallback = new ShellInfo();
                SHGetFileInfo("icon-fallback.unknown-file-type", 0x80, ref fallback, (uint)Marshal.SizeOf(typeof(ShellInfo)), 0x4000 | 0x10);
                generic = info.Index == fallback.Index;
                return Bitmap(info.Icon);
            }
            catch { return null; }
            finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
        }
        static ImageSource Extract(string path, int index)
        {
            var large = new IntPtr[1]; var small = new IntPtr[1];
            try
            {
                if (!File.Exists(path) || ExtractIconEx(path, index, large, small, 1) == 0) return null;
                IntPtr icon = large[0] != IntPtr.Zero ? large[0] : small[0];
                return icon == IntPtr.Zero ? null : Bitmap(icon);
            }
            catch { return null; }
            finally { if (large[0] != IntPtr.Zero) DestroyIcon(large[0]); if (small[0] != IntPtr.Zero && small[0] != large[0]) DestroyIcon(small[0]); }
        }
        static ImageSource Bitmap(IntPtr icon)
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); return source;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ShellInfo
        {
            public IntPtr Icon; public int Index; public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellInfo info, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")] static extern uint ExtractIconEx(string path, int index, IntPtr[] large, IntPtr[] small, uint count);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    }
}
