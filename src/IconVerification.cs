using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OrbitLauncher
{
    public static class IconVerification
    {
        static readonly List<string> log = new List<string>();
        static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); log.Add("PASS: " + label); }
        static string Hash(ImageSource icon)
        {
            var bitmap = icon as BitmapSource; if (bitmap == null) return "none";
            var normalized = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var pixels = new byte[normalized.PixelWidth * normalized.PixelHeight * 4]; normalized.CopyPixels(pixels, normalized.PixelWidth * 4, 0);
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(pixels));
        }
        static void Link(string file, string target, string icon)
        {
            object shell = null, link = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type); link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { file });
                var lt = link.GetType(); lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                if (icon != null) lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { icon });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, new object[0]);
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        static void Expire(LaunchItem item, int seconds)
        {
            var cache = (IDictionary)typeof(Assets).GetField("cache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); var entry = cache[Targets.Key(item)];
            entry.GetType().GetField("Time").SetValue(entry, DateTime.UtcNow.AddSeconds(-seconds));
        }
        static async Task Until(Func<bool> condition)
        {
            var time = Stopwatch.StartNew(); while (!condition() && time.ElapsedMilliseconds < 6000) await Task.Delay(30);
            if (!condition()) throw new Exception("Icon update timed out.");
        }
        public static int Run(string folder, string userConfig = null)
        {
            string root = Path.Combine(folder, "icons-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(root);
            var app = Entry.CreateApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var main = new MainWindow(new ConfigStore(root), Configuration.Initial()); bool failed = false;
            main.Loaded += delegate
            {
                main.Dispatcher.BeginInvoke(new Action(async delegate
                {
                    try
                    {
                        string exe = Process.GetCurrentProcess().MainModule.FileName;
                        var program = new LaunchItem { Target = exe }; var direct = await Assets.Get(program, true);
                        Check(direct.Icon != null && !direct.NeedsRetry, "EXE 通过系统图标接口获得真实图标");
                        string shortcut = Path.Combine(root, "空图标路径.lnk"); Link(shortcut, exe, ",0"); var item = new LaunchItem { Name = "快捷方式", Target = shortcut }; var empty = await Assets.Get(item, true);
                        Check(empty.Icon != null && Hash(empty.Icon) == Hash(direct.Icon), "快捷方式未指定图标路径时回退到目标 EXE");
                        Link(shortcut, exe, Path.Combine(root, "deleted.ico") + ",0"); var invalid = await Assets.Get(item, true);
                        Check(invalid.Icon != null && Hash(invalid.Icon) == Hash(direct.Icon), "快捷方式图标文件被删除时仍显示目标程序图标");
                        Link(shortcut, exe, exe + ",9999"); var index = await Assets.Get(item, true);
                        Check(index.Icon != null && Hash(index.Icon) == Hash(direct.Icon), "无效图标索引不会阻止目标程序图标回退");
                        string iconFile = Path.Combine(root, "图标,中文.ico"); using (var file = File.Create(iconFile)) System.Drawing.SystemIcons.Warning.Save(file);
                        Link(shortcut, exe, iconFile + ",0"); var custom = await Assets.Get(item, true);
                        Check(custom.Icon != null && Hash(custom.Icon) != Hash(direct.Icon), "保留快捷方式已有图标并正确处理路径中的逗号");
                        Expire(item, 31); var unchanged = await Assets.Get(item);
                        Check(Object.ReferenceEquals(custom.Icon, unchanged.Icon), "资源未变化时后台复查复用同一图标");
                        using (var file = File.Create(iconFile)) System.Drawing.SystemIcons.Information.Save(file); File.SetLastWriteTimeUtc(iconFile, DateTime.UtcNow.AddSeconds(2)); Expire(item, 31); var replaced = await Assets.Get(item);
                        Check(Hash(replaced.Icon) != Hash(custom.Icon), "图标资源更新后缓存自动失效并重新提取");
                        string missingFile = Path.Combine(root, "暂时不可用.exe"); var missing = new LaunchItem { Target = missingFile }; var unavailable = await Assets.Get(missing, true);
                        Check(unavailable.Icon == null && unavailable.NeedsRetry, "暂时不可用的资源标记为可重试而非成功缓存");
                        File.Copy(exe, missingFile); Expire(missing, 2); var recovered = await Assets.Get(missing);
                        Check(recovered.Icon != null && recovered.Error == null, "失败缓存短期过期后重新获取已恢复的资源");
                        string loop = Path.Combine(root, "循环.lnk"); File.WriteAllText(loop, "invalid shortcut"); var watch = Stopwatch.StartNew(); var cyclic = await Assets.Get(new LaunchItem { Target = loop }, true);
                        Check(watch.ElapsedMilliseconds < 4000 && cyclic.NeedsRetry, "损坏快捷方式有限解析并保留重试状态");
                        var c = new Configuration(); c.Settings.ExitAfterLaunch = false; var group = new LaunchGroup { Name = "图标验证" }; group.Items.Add(item); c.Groups.Add(group);
                        var commit = (Task<bool>)typeof(MainWindow).GetMethod("Commit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, new object[] { c }); await commit; main.UpdateLayout();
                        var row = Ui.Descendants<Grid>(main).First(g => g.Tag as string == "item:" + item.Id); var view = Ui.Descendants<IconPresenter>(row).Single(); await Until(delegate { return view.Child is Image; });
                        string oldHash = Hash(((Image)view.Child).Source); using (var file = File.Create(iconFile)) System.Drawing.SystemIcons.Error.Save(file);
                        var refresh = row.ContextMenu.Items.OfType<MenuItem>().First(m => m.Header as string == "刷新图标"); refresh.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Until(delegate { return view.Child is Image && Hash(((Image)view.Child).Source) != oldHash; });
                        Check(Hash(((Image)view.Child).Source) != oldHash, "右键刷新图标绕过未过期缓存并更新当前应用行");
                        string lateFile = Path.Combine(root, "稍后恢复.exe"); var late = new LaunchItem { Name = "稍后恢复", Target = lateFile }; var lateView = new IconPresenter(late, 25); var panel = new StackPanel(); panel.Children.Add(lateView); var window = new Window { Width = 180, Height = 120, Content = panel }; window.Show(); await Assets.Get(late);
                        File.Copy(exe, lateFile); await Until(delegate { return lateView.Child is Image; });
                        Check(lateView.Child is Image, "已显示应用行的暂时失败会自动重试并补齐图标"); window.Close();
                        var original = File.ReadAllBytes(shortcut); await Assets.Get(item, true); Check(original.SequenceEqual(File.ReadAllBytes(shortcut)), "图标读取不会改写用户快捷方式");
                        // Read the reported WeChat shortcut only; never start the app or save its configuration.
                        if (userConfig != null)
                        {
                            var user = ConfigStore.Read(userConfig); var wechat = user.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Name == "微信");
                            if (wechat == null) throw new Exception("WeChat test item not found.");
                            if (wechat != null)
                            {
                                var before = File.ReadAllBytes(userConfig); var actual = await Assets.Get(wechat, true); string target, location; int iconIndex;
                                bool resolved = IconLoader.Shortcut(Targets.Clean(wechat.Target), out target, out location, out iconIndex);
                                var targetAsset = resolved ? await Assets.Get(new LaunchItem { Target = target }, true) : null;
                                Check(actual.Icon != null && !actual.NeedsRetry && targetAsset != null && Hash(actual.Icon) == Hash(targetAsset.Icon), "真实微信快捷方式获得目标程序图标");
                                Check(before.SequenceEqual(File.ReadAllBytes(userConfig)), "真实快捷方式检查不改写个人分组配置");
                                var demo = ConfigCodec.Clone(c); demo.Groups[0].Name = "开始工作"; demo.Groups[0].Items.Clear(); demo.Groups[0].Items.Add(new LaunchItem { Name = "微信", Target = wechat.Target });
                                var show = (Task<bool>)typeof(MainWindow).GetMethod("Commit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, new object[] { demo }); await show; main.UpdateLayout();
                                await Task.Delay(200); Ui.Snapshot(main, Path.Combine(folder, "12-wechat-icon.png"));
                            }
                        }
                    }
                    catch (Exception e) { failed = true; log.Add(e.ToString()); }
                    finally { File.WriteAllText(Path.Combine(folder, "icon-test-results.txt"), String.Join("\r\n", log) + "\r\nTOTAL: " + log.Count(s => s.StartsWith("PASS:")) + " passed\r\n", Encoding.UTF8); main.Close(); }
                    await Task.Delay(100); app.Shutdown(failed ? 1 : 0);
                }), DispatcherPriority.ApplicationIdle);
            };
            return app.Run(main);
        }
    }
}
