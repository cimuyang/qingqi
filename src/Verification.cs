using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace OrbitLauncher
{
    public static class Tests
    {
        static List<string> results;
        static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); results.Add("PASS: " + label); }
        static void Reject(Action action, string label)
        {
            bool failed = false; try { action(); } catch { failed = true; } Check(failed, label);
        }
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder); results = new List<string>();
            string root = Path.Combine(folder, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(root);
            try
            {
                var c = Configuration.Initial(); c.Groups[0].Name = "工作 / 备课 ✦";
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                var marker = Path.Combine(root, "中文 probe marker.txt");
                var i = new LaunchItem { Name = "中文应用", Target = exe, Arguments = "--probe \"" + marker + "\"" };
                c.Groups[0].Items.Add(i);
                var clone = ConfigCodec.Clone(c); Check(clone.Groups[0].Name == c.Groups[0].Name && clone.Groups[0].Items[0].Arguments == i.Arguments, "中文、符号、参数 JSON 往返");
                clone.Groups[0].Items[0].Name = "改名"; Check(c.Groups[0].Items[0].Name == "中文应用", "编辑副本不影响原配置");
                Reject(delegate { ConfigCodec.Decode("{}"); }, "拒绝缺失配置结构");
                Reject(delegate { ConfigCodec.Decode("{\"Version\":99,\"Groups\":[],\"Settings\":{}}"); }, "拒绝未来版本");
                Reject(delegate { ConfigCodec.Decode("{\"Version\":1,\"Groups\":null,\"Settings\":{}}"); }, "拒绝空分组结构");
                clone = ConfigCodec.Clone(c); clone.Groups[1].Id = clone.Groups[0].Id; Reject(delegate { ConfigCodec.Validate(clone); }, "拒绝重复标识");
                clone = ConfigCodec.Clone(c); clone.Settings.DelayMs = -1; Reject(delegate { ConfigCodec.Validate(clone); }, "拒绝非法间隔");
                clone = ConfigCodec.Clone(c); clone.Groups[0].Name = " "; Reject(delegate { ConfigCodec.Validate(clone); }, "拒绝空名称");
                clone = ConfigCodec.Clone(c); clone.Groups[0].Color = "unknown"; Reject(delegate { ConfigCodec.Validate(clone); }, "拒绝非法颜色");
                var store = new ConfigStore(Path.Combine(root, "store")); Check(store.Load().Groups.Count == 3, "首次启动创建三组空模板");
                store.Save(c); Check(store.Load().Groups[0].Items.Count == 1, "本地配置落盘再读取");
                var c2 = ConfigCodec.Clone(c); c2.Groups[0].Name = "新版"; store.Save(c2);
                Check(ConfigStore.Read(store.FilePath + ".bak").Groups[0].Name == c.Groups[0].Name, "原子替换保留前一版备份");
                File.WriteAllText(store.FilePath, "damaged"); var recovered = store.Load();
                Check(recovered.Groups[0].Name == c.Groups[0].Name && store.RecoveryMessage != null, "配置损坏自动恢复备份");
                Check(Directory.GetFiles(store.Folder, "*.damaged-*").Length == 1, "损坏配置另存保留");
                Check(ConfigStore.Read(store.FilePath + ".bak").Groups[0].Name == recovered.Groups[0].Name, "恢复后备份仍然有效");
                var broken = new ConfigStore(Path.Combine(root, "broken")); Directory.CreateDirectory(broken.Folder); File.WriteAllText(broken.FilePath, "broken");
                Reject(delegate { broken.Load(); }, "无有效备份时拒绝覆盖损坏配置"); Check(File.ReadAllText(broken.FilePath) == "broken", "无备份时保留原文件");
                Check(Targets.Error(i) == null, "验证真实可执行路径");
                Check(Targets.Error(new LaunchItem { Target = "C:\\not-found-orbit-7654321.exe" }) != null, "验证缺失路径");
                Check(Targets.Error(new LaunchItem { Target = "relative.exe" }) != null, "拒绝相对路径");
                Check(Targets.IsWeb("https://example.com/path?q=中文") && !Targets.IsWeb("javascript:alert(1)"), "网页地址只接受 HTTP 与 HTTPS");
                Check(Targets.Clean("\"" + exe + "\"") == exe, "清理带引号的文件路径");
                Check(Targets.Error(new LaunchItem { Target = "https://example.com", Arguments = "bad" }) != null, "拒绝网页启动参数");
                Check(Targets.Error(new LaunchItem { Target = exe, WorkingDirectory = Path.Combine(root, "missing-dir") }) != null, "工作目录检查");
                Check(Targets.Error(new LaunchItem { Target = root }) == null, "支持文件夹位置");
                var launcher = new Launcher(); var outcomes = launcher.RunAsync(new[] { i }, 0, null, CancellationToken.None).GetAwaiter().GetResult();
                var timer = Stopwatch.StartNew(); while (!File.Exists(marker) && timer.ElapsedMilliseconds < 8000) Thread.Sleep(80);
                Check(outcomes.Count == 1 && outcomes[0].Success && File.Exists(marker) && File.ReadAllText(marker).Contains("orbit-probe-ok"), "真实 Shell 启动 EXE 并传递中文及空格参数");
                var bad = new LaunchItem { Name = "缺失", Target = Path.Combine(root, "missing.exe") };
                var marker2 = Path.Combine(root, "after-error.txt"); var afterError = new LaunchItem { Name = "继续", Target = exe, Arguments = "--probe \"" + marker2 + "\"" };
                outcomes = launcher.RunAsync(new[] { bad, afterError }, 0, null, CancellationToken.None).GetAwaiter().GetResult();
                timer.Restart(); while (!File.Exists(marker2) && timer.ElapsedMilliseconds < 8000) Thread.Sleep(80);
                Check(!outcomes[0].Success && outcomes[1].Success && File.Exists(marker2), "单项失败不阻止后续应用");
                var token = new CancellationTokenSource(); token.Cancel();
                Check(launcher.RunAsync(new[] { i }, 0, null, token.Token).GetAwaiter().GetResult().Count == 0 && !launcher.IsBusy, "取消启动不执行剩余项目并释放忙状态");
                var pending = launcher.RunAsync(new[] { bad, bad }, 350, null, CancellationToken.None);
                Reject(delegate { launcher.RunAsync(new[] { i }, 0, null, CancellationToken.None).GetAwaiter().GetResult(); }, "拦截并发重复启动");
                pending.GetAwaiter().GetResult(); Check(!launcher.IsBusy, "完成后释放启动锁");
                var same = new[] { i, i }.GroupBy(Targets.Key).Count(); Check(same == 1, "全部启动时识别相同位置与参数");
                Check(GroupPages.Count(0) == 1 && GroupPages.Count(4) == 1, "空分组与四项分组只有一页");
                Check(GroupPages.Count(5) == 2 && GroupPages.Count(8) == 2 && GroupPages.Count(9) == 3, "五项、八项、九项的分页边界");
                Check(GroupPages.Count(200) == 50, "最大分组数量的分页计算");
                var pages = new GroupPages(); Check(pages.Get("one", 9) == 0, "分组默认第一页");
                Check(pages.Set("one", 9, 99) == 2 && pages.Set("two", 5, -1) == 0, "分页请求上下界限制");
                Check(pages.Get("one", 9) == 2 && pages.Get("two", 5) == 0, "各分组页码独立保留");
                Check(pages.Get("one", 4) == 0 && pages.Get("one", 0) == 0, "删除项目后修正失效页码");
                pages.Set("one", 9, 2); pages.Retain(new[] { "two" }); Check(pages.Get("one", 9) == 0, "移除分组时清理分页状态");
                File.WriteAllText(Path.Combine(folder, "test-results.txt"), String.Join("\r\n", results) + "\r\nTOTAL: " + results.Count + " passed\r\n", Encoding.UTF8); return 0;
            }
            catch (Exception e) { results.Add(e.ToString()); File.WriteAllText(Path.Combine(folder, "test-results.txt"), String.Join("\r\n", results), Encoding.UTF8); return 1; }
        }
    }
    public static class Preview
    {
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            try
            {
                var app = Entry.CreateApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var store = new ConfigStore(Path.Combine(folder, "preview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
                var config = Configuration.Initial();
                var main = new MainWindow(store, config); app.MainWindow = main;
                main.Loaded += delegate
                {
                    main.Dispatcher.BeginInvoke(new Action(async delegate
                    {
                        try
                        {
                            Ui.Snapshot(main, Path.Combine(folder, "01-first-run.png"));
                            config.Groups[0].Items.Add(new LaunchItem { Name = "浏览器", Target = "https://example.com" });
                            config.Groups[0].Items.Add(new LaunchItem { Name = "资料文件夹", Target = folder });
                            config.Groups[0].Items.Add(new LaunchItem { Name = "轻启", Target = Process.GetCurrentProcess().MainModule.FileName });
                            config.Groups[0].Items.Add(new LaunchItem { Name = "备课资料", Target = "https://example.com/lesson" });
                            config.Groups[0].Items.Add(new LaunchItem { Name = "阅读清单", Target = "https://example.com/list" });
                            config.Groups[1].Items.Add(new LaunchItem { Name = "邮件", Target = "https://example.com/mail" });
                            config.Groups[1].Items.Add(new LaunchItem { Name = "消息", Target = "https://example.com/chat" });
                            config.Groups[2].Items.Add(new LaunchItem { Name = "阅读", Target = "https://example.com/read" });
                            main.Render(); main.UpdateLayout(); await Task.Delay(200); Ui.Snapshot(main, Path.Combine(folder, "02-main.png"));
                            var next = Ui.Descendants<System.Windows.Controls.Button>(main).First(b => String.Equals(b.Tag as string, "page-next:" + config.Groups[0].Id)); next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Ui.Snapshot(main, Path.Combine(folder, "07-page-two.png"));
                            var previous = Ui.Descendants<System.Windows.Controls.Button>(main).First(b => String.Equals(b.Tag as string, "page-prev:" + config.Groups[0].Id)); previous.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                            var item = new ItemDialog(main, config.Groups[0].Items[0], config); item.Show(); item.UpdateLayout(); Ui.Snapshot(item, Path.Combine(folder, "03-app-editor.png")); item.Close();
                            var group = new GroupDialog(main, ConfigCodec.Clone(config).Groups[0], config); group.Show(); group.UpdateLayout(); Ui.Snapshot(group, Path.Combine(folder, "04-group-editor.png")); group.Close();
                            var settings = new SettingsDialog(main, config, store, delegate { return Task.FromResult(true); }); settings.Show(); settings.UpdateLayout(); Ui.Snapshot(settings, Path.Combine(folder, "05-settings.png")); settings.Close();
                            var notice = new NoticeDialog(main, "删除“保持联系”？", "将移除此分组及其中的启动配置。电脑上的应用和文件会保留。", "删除分组", true, true); notice.Show(); notice.UpdateLayout(); Ui.Snapshot(notice, Path.Combine(folder, "08-confirmation.png")); Ui.Snapshot(main, Path.Combine(folder, "09-modal-backdrop.png")); notice.Close();
                            var more = Ui.Descendants<System.Windows.Controls.Button>(main).First(b => b.ContextMenu != null && b.ContextMenu.Items.Count > 4); var menu = more.ContextMenu; menu.PlacementTarget = more; menu.IsOpen = true; menu.UpdateLayout(); Ui.SnapshotVisual(menu, Path.Combine(folder, "10-group-menu.png")); menu.IsOpen = false;
                            main.Width = 800; main.Height = 540; main.UpdateLayout(); Ui.Snapshot(main, Path.Combine(folder, "06-compact.png"));
                            File.WriteAllText(Path.Combine(folder, "preview-results.txt"), "PASS: 10 WPF windows/layouts rendered", Encoding.UTF8);
                        }
                        catch (Exception e) { File.WriteAllText(Path.Combine(folder, "preview-results.txt"), e.ToString()); Environment.ExitCode = 1; }
                        finally { main.Close(); app.Shutdown(); }
                    }), DispatcherPriority.ApplicationIdle);
                };
                app.Run(main); return Environment.ExitCode;
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(folder, "preview-results.txt"), e.ToString()); return 1; }
        }
    }
}
