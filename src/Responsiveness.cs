using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Web.Script.Serialization;

namespace OrbitLauncher
{
    public sealed class AssetInfo
    {
        public ImageSource Icon;
        public string Error;
        public bool Deferred;
        public bool Folder;
        public bool NeedsRetry;
        internal IconStamp[] Dependencies;
    }
    // Coalesce identical requests and limit Shell work to two background STA workers.
    public static class Assets
    {
        sealed class Entry { public Task<AssetInfo> Task; public DateTime Time; }
        static readonly object sync = new object();
        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        static readonly SemaphoreSlim slots = new SemaphoreSlim(2);
        public static Task<AssetInfo> Get(LaunchItem item, bool force = false)
        {
            string key = Targets.Key(item);
            lock (sync)
            {
                Entry entry;
                bool found = cache.TryGetValue(key, out entry);
                if (found && (!entry.Task.IsCompleted || (!force && (DateTime.UtcNow - entry.Time).TotalSeconds < (entry.Task.Status != TaskStatus.RanToCompletion || entry.Task.Result.NeedsRetry ? 1 : 30)))) return entry.Task;
                AssetInfo previous = found && !force && entry.Task.Status == TaskStatus.RanToCompletion ? entry.Task.Result : null;
                // Pending work stays coalesced even if the cache is full.
                foreach (var old in cache.Where(p => p.Value.Task.IsCompleted).OrderBy(p => p.Value.Time).Take(Math.Max(0, cache.Count - 511)).ToList()) cache.Remove(old.Key);
                var copy = new LaunchItem { Id = item.Id, Name = item.Name, Target = item.Target, Arguments = item.Arguments, WorkingDirectory = item.WorkingDirectory };
                entry = new Entry { Task = Read(copy, previous), Time = DateTime.UtcNow }; cache[key] = entry; return entry.Task;
            }
        }
        static async Task<AssetInfo> Read(LaunchItem item, AssetInfo previous)
        {
            await slots.WaitAsync().ConfigureAwait(false);
            var done = new TaskCompletionSource<AssetInfo>();
            var thread = new Thread(delegate()
            {
                var result = new AssetInfo();
                try
                {
                    // Disconnected shares must not occupy both display workers indefinitely.
                    if (Remote(item.Target) || Remote(item.WorkingDirectory)) { result.Deferred = true; return; }
                    result.Error = Targets.Error(item);
                    string path = Targets.Clean(item.Target);
                    result.Folder = Directory.Exists(path);
                    if (previous != null && !previous.NeedsRetry && IconLoader.Unchanged(previous.Dependencies))
                    { result.Icon = previous.Icon; result.Dependencies = previous.Dependencies; }
                    else IconLoader.Load(path, result);
                    result.NeedsRetry = result.NeedsRetry || result.Error != null;
                }
                catch { result.NeedsRetry = true; }
                finally { slots.Release(); done.TrySetResult(result); }
            });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA);
            try { thread.Start(); } catch { slots.Release(); throw; }
            return await done.Task.ConfigureAwait(false);
        }
        internal static bool Remote(string target)
        {
            string path = Targets.Clean(target);
            if (path.StartsWith("\\\\", StringComparison.Ordinal)) return true;
            if (Targets.IsWeb(path) || String.IsNullOrEmpty(path)) return false;
            try { string root = Path.GetPathRoot(path); return !String.IsNullOrEmpty(root) && GetDriveType(root) == 4; }
            catch { return false; }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetDriveType(string root);
    }
    public sealed class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
        static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        public static void Restore(Window window, string folder)
        {
            try
            {
                string path = Path.Combine(folder, "window.json"); if (!File.Exists(path) || new FileInfo(path).Length > 4096) return;
                var p = new JavaScriptSerializer().Deserialize<WindowPlacement>(File.ReadAllText(path));
                if (p == null || !Finite(p.Left) || !Finite(p.Top) || !Finite(p.Width) || !Finite(p.Height)) return;
                window.Width = Math.Max(window.MinWidth, Math.Min(2400, p.Width)); window.Height = Math.Max(window.MinHeight, Math.Min(1600, p.Height));
                window.Left = p.Left; window.Top = p.Top; window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Loaded += delegate { KeepVisible(window); if (p.Maximized) window.WindowState = WindowState.Maximized; };
            }
            catch { /* Optional window preferences never affect application data. */ }
        }
        public static Task Save(Window window, string folder)
        {
            Rect r = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
            var p = new WindowPlacement { Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height, Maximized = window.WindowState == WindowState.Maximized };
            return Task.Run(delegate
            {
                string temporary = null;
                try
                {
                    Directory.CreateDirectory(folder); string path = Path.Combine(folder, "window.json"); temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                    File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(p));
                    if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                }
                catch { }
                finally { if (temporary != null) try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
            });
        }
        public static void KeepVisible(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle; NativeRect r;
            if (!GetWindowRect(handle, out r)) return;
            var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)).WorkingArea;
            int width = Math.Min(r.Right - r.Left, screen.Width), height = Math.Min(r.Bottom - r.Top, screen.Height);
            int x = Math.Max(screen.Left, Math.Min(r.Left, screen.Right - width)), y = Math.Max(screen.Top, Math.Min(r.Top, screen.Bottom - height));
            SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x0014);
        }
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    }
    public sealed class InstanceBroker : IDisposable
    {
        readonly string name;
        readonly Action<string> receive;
        int stopped;
        bool Stopping { get { return Volatile.Read(ref stopped) != 0; } }
        NamedPipeServerStream active;
        readonly object sync = new object();
        public InstanceBroker(string key, Action<string> handler)
        {
            name = PipeName(key); receive = handler; Task.Run((Func<Task>)Listen);
        }
        static string PipeName(string key) { return "OrbitLauncher-" + System.Diagnostics.Process.GetCurrentProcess().SessionId + "-" + key; }
        async Task Listen()
        {
            while (!Stopping)
            {
                try
                {
                    var security = new PipeSecurity(); security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 128, 128, security))
                    {
                        lock (sync) { if (Stopping) return; active = pipe; }
                        await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                        using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                        {
                            await writer.WriteLineAsync(System.Diagnostics.Process.GetCurrentProcess().Id.ToString()).ConfigureAwait(false);
                            var bytes = new byte[64]; int count = 0;
                            while (count < bytes.Length)
                            {
                                var read = pipe.ReadAsync(bytes, count, 1);
                                if (await Task.WhenAny(read, Task.Delay(2000)).ConfigureAwait(false) != read) break;
                                if (await read.ConfigureAwait(false) == 0 || bytes[count] == 10) break;
                                count++;
                            }
                            string command = System.Text.Encoding.UTF8.GetString(bytes, 0, count).Trim(); Guid id;
                            if (command == "activate" || Guid.TryParse(command, out id)) { receive(command); await writer.WriteLineAsync("ok").ConfigureAwait(false); }
                        }
                    }
                }
                catch { }
                finally { lock (sync) active = null; }
                if (!Stopping) await Task.Delay(100).ConfigureAwait(false);
            }
        }
        public static bool Send(string key, string group)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName(key), PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(2000);
                    using (var reader = new StreamReader(pipe)) using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                    {
                        var greeting = reader.ReadLineAsync(); if (!greeting.Wait(2000)) return false;
                        uint pid; if (UInt32.TryParse(greeting.Result, out pid)) AllowSetForegroundWindow(pid);
                        writer.WriteLine(group ?? "activate"); var ack = reader.ReadLineAsync(); return ack.Wait(2000) && ack.Result == "ok";
                    }
                }
            }
            catch { return false; }
        }
        public void Dispose()
        {
            NamedPipeServerStream pipe;
            Interlocked.Exchange(ref stopped, 1);
            lock (sync) pipe = active;
            // Cancelling/disposal can finish I/O continuations; never hold their state lock.
            if (pipe != null) pipe.Dispose();
        }
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    }
}
