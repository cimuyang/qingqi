using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitLauncher
{
    public static class ResponsivenessVerification
    {
        static readonly List<string> log = new List<string>();
        static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); log.Add("PASS: " + label); }
        static Task<bool> Commit(MainWindow main, Configuration config) { return (Task<bool>)typeof(MainWindow).GetMethod("Commit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, new object[] { config }); }
        static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner); }
        static Button Launch(MainWindow main, string id) { main.UpdateLayout(); return Ui.Descendants<Button>(main).First(b => b.Tag as string == "launch:" + id); }
        static async Task Idle(MainWindow main)
        {
            var watch = Stopwatch.StartNew(); while (Field<bool>(main, "launching") && watch.ElapsedMilliseconds < 10000) await Task.Delay(20);
            Check(!Field<bool>(main, "launching"), "启动任务在限定时间内完成并释放交互");
        }
        public static int Run(string folder)
        {
            string root = Path.Combine(folder, "responsive-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(root);
            var app = Entry.CreateApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = new ConfigStore(root); var config = new Configuration(); config.Settings.ExitAfterLaunch = false; config.Settings.DelayMs = 0;
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            for (int g = 0; g < 20; g++)
            {
                var group = new LaunchGroup { Name = "分组 " + g };
                for (int i = 0; i < 20; i++) group.Items.Add(new LaunchItem { Name = "测试应用 " + g + "-" + i, Target = exe, Arguments = "--probe \"" + Path.Combine(root, "safe-probe.txt") + "\"" });
                config.Groups.Add(group);
            }
            var startup = Stopwatch.StartNew(); var main = new MainWindow(store, config); bool failed = false;
            main.Loaded += delegate
            {
                main.Dispatcher.BeginInvoke(new Action(async delegate
                {
                    try
                    {
                        log.Add("MEASURE: 20 groups / 400 items, first window Loaded = " + startup.ElapsedMilliseconds + " ms");
                        var original = Ui.Descendants<Border>(main).First(b => b.Tag as string == "group:" + config.Groups[0].Id);
                        var watch = Stopwatch.StartNew(); for (int n = 0; n < 30; n++) main.Render(); watch.Stop();
                        Check(Object.ReferenceEquals(original, Ui.Descendants<Border>(main).First(b => b.Tag as string == "group:" + config.Groups[0].Id)), "连续刷新复用未变化卡片");
                        log.Add("MEASURE: 30 cached Render calls = " + watch.ElapsedMilliseconds + " ms");
                        var search = Field<TextBox>(main, "search"); for (int n = 0; n < 30; n++) search.Text = "测试应用 0-" + (n % 20);
                        search.Text = "测试应用 0-19"; await Task.Delay(160); main.UpdateLayout();
                        Check(original.IsVisible && Ui.Descendants<TextBlock>(original).Any(t => t.Text == "5 / 5"), "快速输入仅应用最新搜索并定位第五页");
                        Check(Ui.Descendants<Border>(main).Count(b => (b.Tag as string ?? "").StartsWith("group:") && b.IsVisible) == 1, "搜索隐藏无关卡片并保留视图");
                        search.Clear(); main.UpdateLayout();
                        Check(Object.ReferenceEquals(original, Ui.Descendants<Border>(main).First(b => b.Tag as string == "group:" + config.Groups[0].Id)), "清除搜索恢复原卡片而非重新创建");
                        var shared = Assets.Get(config.Groups[0].Items[0]); var duplicate = Assets.Get(config.Groups[0].Items[0]);
                        Check(Object.ReferenceEquals(shared, duplicate), "相同图标和路径检查合并为同一后台任务");
                        var asset = await shared; Check(asset.Icon != null && asset.Icon.IsFrozen && asset.Error == null, "后台 STA 图标冻结后可安全显示");
                        var missing = await Assets.Get(new LaunchItem { Target = Path.Combine(root, "not-there.exe") }); Check(missing.Error != null, "后台路径检查识别失效位置");
                        var remote = await Assets.Get(new LaunchItem { Target = "\\\\offline-server\\missing\\tool.exe" }); Check(remote.Deferred && remote.Error == null, "网络路径展示延后检查而不阻塞本地图标队列");
                        Check(Object.ReferenceEquals(Ui.Shadow(.12, 14, 4), Ui.Shadow(.12, 14, 4)), "相同阴影资源冻结并复用");
                        var animated = Launch(main, config.Groups[0].Id);
                        animated.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent }); await Task.Delay(100);
                        Check(((ScaleTransform)animated.RenderTransform).ScaleX < 1, "按下反馈自然进入轻微缩放状态");
                        for (int n = 0; n < 20; n++)
                        {
                            animated.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                            animated.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                        }
                        await Task.Delay(180); Check(Math.Abs(((ScaleTransform)animated.RenderTransform).ScaleX - 1) < .001, "连续按下与松开不堆积动画且恢复原尺寸");
                        var gate = Field<SemaphoreSlim>(store, "writes"); gate.Wait();
                        var next = ConfigCodec.Clone(main.Config); next.Groups[0].Name = "保存后的分组";
                        var pending = Commit(main, next); bool dispatched = false; var dispatchWatch = Stopwatch.StartNew();
                        var dispatch = main.Dispatcher.BeginInvoke(new Action(delegate { dispatched = true; log.Add("MEASURE: UI dispatch while save is blocked = " + dispatchWatch.ElapsedMilliseconds + " ms"); }));
                        await Task.Delay(80);
                        Check(dispatched && !pending.IsCompleted && main.Config.Groups[0].Name != next.Groups[0].Name, "磁盘写入等待期间界面响应且尚未宣称保存成功");
                        Check(!await Commit(main, ConfigCodec.Clone(main.Config)), "保存期间拒绝重入并保留正在写入的配置");
                        gate.Release(); Check(await pending && ConfigStore.Read(store.FilePath).Groups[0].Name == next.Groups[0].Name, "完成原子写入后才更新界面配置"); await dispatch;
                        var other = Ui.Descendants<Border>(main).First(b => b.Tag as string == "group:" + next.Groups[1].Id); var edited = ConfigCodec.Clone(main.Config); edited.Groups[0].Name = "再次编辑"; await Commit(main, edited);
                        Check(Object.ReferenceEquals(other, Ui.Descendants<Border>(main).First(b => b.Tag as string == "group:" + next.Groups[1].Id)), "编辑一个分组保留其他卡片实例");
                        var ordered = new ConfigStore(Path.Combine(root, "ordered"));
                        var versions = new List<Task>(); for (int n = 0; n < 3; n++) { var c = Configuration.Initial(); c.Groups[0].Name = "顺序 " + n; versions.Add(ordered.SaveAsync(c)); }
                        await Task.WhenAll(versions); Check(ordered.Load().Groups[0].Name == "顺序 2" && ConfigStore.Read(ordered.FilePath + ".bak").Groups[0].Name == "顺序 1", "串行保存保留最后版本及前一版备份");
                        var retryConfig = ConfigCodec.Clone(main.Config); var group = retryConfig.Groups[0]; group.Items.Clear();
                        string first = Path.Combine(root, "success.txt"), second = Path.Combine(root, "retry.txt");
                        group.Items.Add(new LaunchItem { Name = "成功项", Target = exe, Arguments = "--probe \"" + first + "\"" });
                        group.Items.Add(new LaunchItem { Name = "待修复项", Target = Path.Combine(root, "missing.exe"), Arguments = "--probe \"" + second + "\"" });
                        await Commit(main, retryConfig);
                        var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                        dismiss.Tick += delegate { foreach (var dialog in app.Windows.OfType<NoticeDialog>().Where(w => w.IsVisible).ToList()) { var button = Ui.Descendants<Button>(dialog).First(b => b.Content as string == "知道了"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } }; dismiss.Start();
                        main.RunGroup(group); Check(Launch(main, group.Id).Content.ToString().Contains("正在启动"), "卡片按钮显示当前启动进度");
                        main.Receive(group.Id); await Idle(main); dismiss.Stop();
                        Check(File.Exists(first) && Launch(main, group.Id).Content.ToString().Contains("重试失败项 · 1"), "部分失败保留窗口并切换失败项重试入口");
                        Check(Field<Queue<string>>(main, "queuedGroups").Count == 0, "重复启动同一运行中分组不会再次排队");
                        Ui.Snapshot(main, Path.Combine(folder, "11-retry.png"));
                        File.Delete(first); var corrected = ConfigCodec.Clone(main.Config); corrected.Groups[0].Items[1].Target = exe; await Commit(main, corrected);
                        Launch(main, group.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(main);
                        var retryWait = Stopwatch.StartNew(); while (!File.Exists(second) && retryWait.ElapsedMilliseconds < 4000) await Task.Delay(20);
                        Check(File.Exists(second) && !File.Exists(first), "修改路径后仅重试失败项且不重复打开成功项");
                        Check(Launch(main, group.Id).Content.ToString().Contains("启动分组"), "重试成功恢复完整启动入口");
                        gate.Wait(); var settings = new SettingsDialog(main, main.Config, store, delegate(Configuration c) { return Commit(main, c); });
                        var releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) }; releaseTimer.Tick += delegate { releaseTimer.Stop(); gate.Release(); };
                        var editDialog = main.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            settings.UpdateLayout(); var saveButton = Ui.Descendants<Button>(settings).First(b => b.Content as string == "保存设置");
                            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); settings.Close();
                            Check(settings.IsVisible && !saveButton.IsEnabled, "设置正在保存时关闭与重复提交被阻止"); releaseTimer.Start();
                        }), DispatcherPriority.ApplicationIdle);
                        settings.ShowDialog(); await editDialog; await main.PendingSave;
                        Check(!settings.IsVisible && main.PendingSave.Result, "设置落盘完成后正常关闭编辑窗口");
                        var notice = new NoticeDialog(main, "测试排队", "正在编辑时快捷方式请求将在操作完成后启动。", "知道了", false, false); notice.Show();
                        File.Delete(second); main.Receive(group.Id); main.Receive(group.Id);
                        Check(Field<Queue<string>>(main, "queuedGroups").Count == 1 && !Field<bool>(main, "launching"), "弹窗期间分组快捷方式排队且重复请求合并");
                        notice.Close(); await Task.Delay(80); await Idle(main);
                        var queueWait = Stopwatch.StartNew(); while (!File.Exists(second) && queueWait.ElapsedMilliseconds < 4000) await Task.Delay(20);
                        Check(File.Exists(second), "弹窗关闭后自动执行排队的分组请求");
                        main.WindowState = WindowState.Minimized; main.Receive("activate"); Check(main.WindowState == WindowState.Normal, "重复打开恢复已最小化窗口");
                        string blockedPath = Path.Combine(root, "blocked"); File.WriteAllText(blockedPath, "not a directory");
                        var bad = new MainWindow(new ConfigStore(blockedPath), Configuration.Initial()); bad.Show();
                        dismiss.Start(); var before = bad.Config; Check(!await Commit(bad, Configuration.Initial()) && Object.ReferenceEquals(before, bad.Config), "保存失败保留原内存配置并提供错误反馈"); dismiss.Stop(); bad.Close();
                        var closeStore = new ConfigStore(Path.Combine(root, "close-pending")); var closeWindow = new MainWindow(closeStore, Configuration.Initial()); closeWindow.Show();
                        var closeGate = Field<SemaphoreSlim>(closeStore, "writes"); closeGate.Wait(); var closeConfig = ConfigCodec.Clone(closeWindow.Config); closeConfig.Groups[0].Name = "关闭前保存";
                        var closeSave = Commit(closeWindow, closeConfig); var closed = new TaskCompletionSource<bool>(); closeWindow.Closed += delegate { closed.TrySetResult(true); }; closeWindow.Close();
                        await Task.Delay(60); Check(closeWindow.IsVisible && !closed.Task.IsCompleted, "保存未完成时关闭窗口会等待写入"); closeGate.Release(); await closeSave;
                        if (await Task.WhenAny(closed.Task, Task.Delay(4000)) != closed.Task) throw new Exception("close timeout");
                        Check(closeStore.Load().Groups[0].Name == "关闭前保存" && File.Exists(Path.Combine(closeStore.Folder, "window.json")), "关闭前完成配置写入与独立窗口偏好保存");
                        var restored = new MainWindow(closeStore, closeStore.Load()); restored.Show(); Check(Math.Abs(restored.Width - closeWindow.Width) < 2, "再次打开恢复窗口尺寸"); restored.Close();
                        string key = Guid.NewGuid().ToString("N"); var received = new TaskCompletionSource<string>();
                        using (var broker = new InstanceBroker(key, delegate(string command) { received.TrySetResult(command); }))
                        {
                            bool delivered = await Task.Run(delegate { return InstanceBroker.Send(key, group.Id); });
                            Check(delivered && await received.Task == group.Id, "命名管道将分组请求送至已有实例并确认接收");
                            await Task.Delay(250);
                        }
                    }
                    catch (Exception e) { failed = true; log.Add(e.ToString()); }
                    finally
                    {
                        File.WriteAllText(Path.Combine(folder, "responsiveness-results.txt"), String.Join("\r\n", log) + "\r\nTOTAL: " + log.Count(l => l.StartsWith("PASS:")) + " passed\r\n", Encoding.UTF8);
                        main.Close();
                    }
                    await Task.Delay(100); app.Shutdown(failed ? 1 : 0);
                }), DispatcherPriority.ApplicationIdle);
            };
            return app.Run(main);
        }
    }
}
