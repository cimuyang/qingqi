using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OrbitLauncher
{
    public static class UiVerification
    {
        static readonly List<string> log = new List<string>();
        static Exception failure;
        static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); log.Add("PASS: " + label); }
        static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
        {
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
            {
                var child = VisualTreeHelper.GetChild(root, n); if (child is T) yield return (T)child;
                foreach (var nested in Visuals<T>(child)) yield return nested;
            }
        }
        static void Click(Window window, string label)
        {
            window.UpdateLayout(); var button = Visuals<Button>(window).First(b => b.Content is string && (string)b.Content == label);
            Check(button.IsEnabled, "按钮可用：" + label); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        static void Invoke(MainWindow window, string name, params object[] args) { typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args); }
        static async Task Call(MainWindow window, string name, params object[] args)
        {
            var task = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args) as Task;
            if (task != null) await task; await Saved(window);
        }
        static async Task Saved(MainWindow main)
        {
            if (main.PendingEdit != null) await main.PendingEdit;
            if (main.PendingSave != null) await main.PendingSave;
        }
        static async Task Modal<T>(MainWindow main, Action open, Action<T> edit) where T : Window
        {
            var editOperation = main.Dispatcher.BeginInvoke(new Action(delegate
            {
                var dialog = Application.Current.Windows.OfType<T>().LastOrDefault();
                try { if (dialog == null) throw new Exception("Dialog not opened: " + typeof(T).Name); dialog.UpdateLayout(); edit(dialog); }
                catch (Exception e) { failure = e; if (dialog != null) dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            open(); await editOperation; await Saved(main); if (failure != null) throw failure;
        }
        static async Task VerifyStartup(MainWindow main, ConfigStore store)
        {
            string branch = @"Software\OrbitLauncherVerification\" + Guid.NewGuid().ToString("N");
            string run = branch + @"\Run", approval = branch + @"\Approval";
            var registration = new StartupRegistration(run, approval, Process.GetCurrentProcess().MainModule.FileName);
            try
            {
                Check(!registration.Read().Registered, "空启动项显示尚未开启");
                await Modal<SettingsDialog>(main, delegate { new SettingsDialog(main, main.Config, store, delegate { return Task.FromResult(true); }, registration).ShowDialog(); }, delegate(SettingsDialog d)
                { Visuals<CheckBox>(d).First(x => x.Content.ToString().Contains("登录 Windows")).IsChecked = true; Click(d, "保存设置"); });
                Check(registration.Read().CurrentPath, "通过设置窗口开启并回读带引号的当前 EXE 路径");
                var enabled = registration.Read();
                await Modal<SettingsDialog>(main, delegate { new SettingsDialog(main, main.Config, store, delegate { return Task.FromResult(true); }, registration).ShowDialog(); }, delegate(SettingsDialog d)
                { Visuals<CheckBox>(d).First(x => x.Content.ToString().Contains("登录 Windows")).IsChecked = false; Click(d, "保存设置"); });
                Check(!registration.Read().Registered, "通过设置窗口关闭自启动删除本程序的记录");
                registration.Restore(enabled); Check(registration.Read().CurrentPath, "启动项回滚恢复原始值");
                using (var key = Registry.CurrentUser.CreateSubKey(run)) key.SetValue("OrbitLauncher", @"%TEMP%\旧位置\轻启.exe", RegistryValueKind.ExpandString);
                var old = registration.Read(); Check(old.Registered && !old.CurrentPath, "旧路径保持开启状态并提示需要修复");
                await Modal<SettingsDialog>(main, delegate { new SettingsDialog(main, main.Config, store, delegate { return Task.FromResult(false); }, registration).ShowDialog(); }, delegate(SettingsDialog d)
                {
                    Check(Visuals<CheckBox>(d).First(x => x.Content.ToString().Contains("登录 Windows")).IsChecked == true, "设置窗口不会把旧路径误判为关闭");
                    Click(d, "保存设置");
                    Check(registration.Read().Value as string == old.Value as string && registration.Read().Kind == RegistryValueKind.ExpandString, "配置保存失败精确恢复旧启动路径及注册表类型"); Click(d, "取消");
                });
                await Modal<SettingsDialog>(main, delegate { new SettingsDialog(main, main.Config, store, delegate { return Task.FromResult(true); }, registration).ShowDialog(); }, delegate(SettingsDialog d)
                {
                    Click(d, "保存设置");
                    Check(registration.Read().CurrentPath, "保存时临时禁用控件不影响写入启动项");
                });
                Check(registration.Read().CurrentPath, "保持勾选并保存即可修复程序移动后的路径");
                byte[] disabled = new byte[12]; disabled[0] = 3;
                using (var key = Registry.CurrentUser.CreateSubKey(approval)) key.SetValue("OrbitLauncher", disabled, RegistryValueKind.Binary);
                Check(registration.Read().Disabled, "识别 Windows 已禁用启动项");
                await Modal<SettingsDialog>(main, delegate { new SettingsDialog(main, main.Config, store, delegate { return Task.FromResult(true); }, registration).ShowDialog(); }, delegate(SettingsDialog d)
                {
                    Check(Visuals<TextBlock>(d).Any(t => t.Text.Contains("Windows 已禁用")), "设置展示系统禁用状态和处理入口");
                    Click(d, "保存设置"); Check(d.IsVisible && Visuals<TextBlock>(d).Any(t => t.Text.Contains("设置已保存，但 Windows")), "系统禁用时保存后留在设置并明确反馈"); Click(d, "取消");
                });
                using (var key = Registry.CurrentUser.OpenSubKey(approval)) Check(((byte[])key.GetValue("OrbitLauncher")).SequenceEqual(disabled), "不擅自修改 Windows 的启动禁用记录");
                registration.Set(false); Check(!registration.Read().Registered, "被系统禁用的启动项仍可关闭");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(branch, false); }
        }
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder); string root = Path.Combine(folder, "ui-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var app = Entry.CreateApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = new ConfigStore(root); var main = new MainWindow(store, Configuration.Initial(), Tests.IsolatedStartup());
            main.Loaded += delegate
            {
                main.Dispatcher.BeginInvoke(new Action(async delegate
                {
                    try
                    {
                        await Modal<GroupDialog>(main, delegate { Click(main, "＋  新建分组"); }, delegate(GroupDialog d) { Visuals<TextBox>(d).First().Text = "测试 · 备课"; Click(d, "保存分组"); });
                        Check(main.Config.Groups.Count == 4 && store.Load().Groups.Last().Name == "测试 · 备课", "通过真实分组编辑窗口新增并持久化");
                        var group = main.Config.Groups.Last(); string id = group.Id;
                        await Modal<ItemDialog>(main, delegate { Invoke(main, "AddItem", group, null); }, delegate(ItemDialog d)
                        {
                            var fields = Visuals<TextBox>(d).ToList(); fields[0].Text = "中文测试应用"; fields[1].Text = Process.GetCurrentProcess().MainModule.FileName; Click(d, "保存应用");
                        });
                        Check(store.Load().Groups.Last().Items.Single().Name == "中文测试应用", "通过真实应用编辑窗口添加并持久化");
                        group = main.Config.Groups.First(g => g.Id == id);
                        await Modal<GroupDialog>(main, delegate { Invoke(main, "EditGroup", group); }, delegate(GroupDialog d) { Visuals<TextBox>(d).First().Text = "未保存修改"; Click(d, "取消"); });
                        Check(main.Config.Groups.Last().Name == "测试 · 备课" && store.Load().Groups.Last().Name == "测试 · 备课", "取消分组编辑不改变内存与磁盘");
                        group = main.Config.Groups.Last();
                        await Call(main, "AddDropped", group, new[] { Process.GetCurrentProcess().MainModule.FileName, root });
                        Check(main.Config.Groups.Last().Items.Count == 2, "拖入处理添加文件夹并跳过已有路径");
                        group = main.Config.Groups.Last();
                        await Modal<GroupDialog>(main, delegate { Invoke(main, "EditGroup", group); }, delegate(GroupDialog d)
                        {
                            var down = Visuals<Button>(d).First(b => b.Content is string && (string)b.Content == "↓" && b.IsEnabled); down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Click(d, "保存分组");
                        });
                        Check(store.Load().Groups.Last().Items.First().Target == root, "应用排序通过编辑窗口保存");
                        group = main.Config.Groups.Last(); await Call(main, "DuplicateGroup", group);
                        var copy = main.Config.Groups.Last(); Check(copy.Name.Contains("副本") && copy.Items.Count == group.Items.Count && copy.Id != group.Id && copy.Items[0].Id != group.Items[0].Id, "复制分组创建独立项目标识");
                        await Call(main, "MoveGroup", copy, -1); Check(main.Config.Groups[3].Id == copy.Id && store.Load().Groups[3].Id == copy.Id, "分组排序持久化");
                        await Modal<SettingsDialog>(main, delegate { Invoke(main, "ShowSettings"); }, delegate(SettingsDialog d)
                        {
                            Visuals<CheckBox>(d).First(x => x.Content.ToString().Contains("自动退出")).IsChecked = false; Click(d, "保存设置");
                        });
                        Check(!main.Config.Settings.ExitAfterLaunch && !store.Load().Settings.ExitAfterLaunch, "自动退出开关通过设置窗口保存");
                        await VerifyStartup(main, store);
                        var expanded = ConfigCodec.Clone(main.Config); var paged = expanded.Groups.First(g => g.Id == id);
                        while (paged.Items.Count < 9) paged.Items.Add(new LaunchItem { Name = "分页应用 " + (paged.Items.Count + 1), Target = Process.GetCurrentProcess().MainModule.FileName });
                        await Call(main, "Commit", expanded); main.UpdateLayout();
                        Func<Border> card = delegate { main.UpdateLayout(); return Visuals<Border>(main).First(b => b.Tag as string == "group:" + id); };
                        Func<Button> prev = delegate { return Visuals<Button>(card()).First(b => b.Tag as string == "page-prev:" + id); };
                        Func<Button> next = delegate { return Visuals<Button>(card()).First(b => b.Tag as string == "page-next:" + id); };
                        Func<int> rows = delegate { return Visuals<Grid>(card()).Count(g => (g.Tag as string ?? "").StartsWith("item:")); };
                        Check(rows() == 4 && !prev().IsEnabled && next().IsEnabled, "第一页四项且上一页禁用");
                        var launch = Visuals<Button>(card()).First(b => b.Tag as string == "launch:" + id); var anchor = launch.TranslatePoint(new Point(0, 0), main);
                        next().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
                        Check(rows() == 4 && Visuals<TextBlock>(card()).Any(t => t.Text == "2 / 3"), "第二页显示第五至第八项");
                        Check(launch.TranslatePoint(new Point(0, 0), main) == anchor, "翻页不移动分组启动按钮");
                        next().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
                        Check(rows() == 1 && !next().IsEnabled && prev().IsEnabled, "最后一页只显示剩余项目且下一页禁用");
                        Check(launch.TranslatePoint(new Point(0, 0), main) == anchor && Visuals<Button>(card()).Any(b => b.Content as string == "＋  添加应用"), "末页保留添加入口和固定底部位置");
                        main.Render(); Check(Visuals<TextBlock>(card()).Any(t => t.Text == "3 / 3"), "重新渲染记住本次会话页码");
                        var shrinking = ConfigCodec.Clone(main.Config); shrinking.Groups.First(g => g.Id == id).Items.RemoveRange(4, 5); await Call(main, "Commit", shrinking);
                        Check(rows() == 4 && !next().IsVisible && Visuals<TextBlock>(card()).Any(t => t.Text == "1 / 1"), "删除末页项目后回到有效页并隐藏单页导航");
                        await Call(main, "Commit", expanded);
                        var search = (TextBox)typeof(MainWindow).GetField("search", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main); search.Text = "中文测试应用"; await Task.Delay(160); main.UpdateLayout();
                        Check(Visuals<TextBlock>(main).Count(t => t.IsVisible && t.Text == "开始工作") == 0 && Visuals<TextBlock>(main).Any(t => t.Text == "测试 · 备课"), "搜索应用名称过滤所属分组");
                        search.Text = "不存在-123456"; await Task.Delay(160); main.UpdateLayout(); Check(Visuals<TextBlock>(main).Any(t => t.Text.Contains("没有找到")), "无搜索结果提供反馈");
                        search.Text = "分页应用 9"; await Task.Delay(160); main.UpdateLayout(); Check(Visuals<TextBlock>(card()).Any(t => t.Text == "分页应用 9") && Visuals<TextBlock>(card()).Any(t => t.Text == "3 / 3"), "搜索自动切到命中应用所在页");
                        search.Clear(); main.UpdateLayout();
                        var beforeDelete = main.Config.Groups.Count;
                        Func<string, ContextMenu> groupMenu = delegate(string groupId)
                        {
                            var groupCard = Visuals<Border>(main).First(b => b.Tag as string == "group:" + groupId);
                            var more = Visuals<Button>(groupCard).First(b => b.ContextMenu != null); more.ContextMenu.PlacementTarget = more; return more.ContextMenu;
                        };
                        var deletionMenu = groupMenu(id); deletionMenu.IsOpen = true; deletionMenu.UpdateLayout();
                        var deletion = deletionMenu.Items.OfType<MenuItem>().Last(); var deletionPoint = deletion.PointToScreen(new Point(0, 0)); double menuWidth = deletionMenu.ActualWidth;
                        deletion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); deletionMenu.UpdateLayout();
                        Check(main.Config.Groups.Count == beforeDelete && deletionMenu.IsOpen && deletion.Header is TextBlock && Application.Current.Windows.OfType<NoticeDialog>().Count() == 0, "第一次删除只在原菜单项确认且不弹出居中窗口");
                        Check(deletion.PointToScreen(new Point(0, 0)) == deletionPoint && Math.Abs(deletionMenu.ActualWidth - menuWidth) < .1, "确认时保持菜单宽度和按钮屏幕位置");
                        deletion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Check(main.Config.Groups.Count == beforeDelete, "快速双击不会确认删除");
                        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(deletionMenu), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        deletionMenu.RaiseEvent(escape); await Task.Delay(30); Check(!deletionMenu.IsOpen && deletion.Header as string == "删除分组…", "Esc 取消并恢复菜单文案");
                        var cancelledId = main.Config.Groups.First(g => g.Id != id).Id;
                        deletionMenu = groupMenu(cancelledId); deletionMenu.IsOpen = true; deletion = deletionMenu.Items.OfType<MenuItem>().Last();
                        deletion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); deletionMenu.IsOpen = false; await Task.Delay(30);
                        deletionMenu.IsOpen = true; Check(deletion.Header as string == "删除分组…", "点击外部关闭后重新打开需要重新确认");
                        deletion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 60);
                        deletion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Saved(main);
                        Check(main.Config.Groups.Count == beforeDelete - 1 && store.Load().Groups.All(g => g.Id != cancelledId), "原位二次确认删除分组并持久化");
                        var row = Visuals<Grid>(card()).First(g => (g.Tag as string ?? "").StartsWith("item:")); string removedId = ((string)row.Tag).Substring(5);
                        var removalMenu = row.ContextMenu; removalMenu.PlacementTarget = row; removalMenu.IsOpen = true; var removal = removalMenu.Items.OfType<MenuItem>().Last();
                        removal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Check(main.Config.Groups.First(g => g.Id == id).Items.Count == 9, "移除应用第一次点击保留启动配置");
                        await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 60); removal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Saved(main);
                        Check(main.Config.Groups.First(g => g.Id == id).Items.Count == 8 && store.Load().Groups.First(g => g.Id == id).Items.All(i => i.Id != removedId), "原位确认只移除对应启动项并持久化");
                        var launchConfig = ConfigCodec.Clone(main.Config); var launchGroup = launchConfig.Groups.First(g => g.Id == id); launchConfig.Settings.ExitAfterLaunch = false; launchConfig.Settings.DelayMs = 0;
                        launchGroup.Items = ConfigCodec.Clone(expanded).Groups.First(g => g.Id == id).Items;
                        var markers = new List<string>();
                        for (int index = 0; index < launchGroup.Items.Count; index++)
                        {
                            var marker = Path.Combine(root, "page-launch-" + index + ".txt"); markers.Add(marker); launchGroup.Items[index].Target = Process.GetCurrentProcess().MainModule.FileName; launchGroup.Items[index].Arguments = "--probe \"" + marker + "\"";
                        }
                        await Call(main, "Commit", launchConfig); while (prev().IsEnabled) prev().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); next().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); next().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var groupLaunch = Visuals<Button>(card()).First(b => b.Tag as string == "launch:" + id); groupLaunch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var watch = Stopwatch.StartNew();
                        while ((markers.Any(m => !File.Exists(m)) || (bool)typeof(MainWindow).GetField("launching", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main)) && watch.ElapsedMilliseconds < 10000) await Task.Delay(30);
                        Check(markers.All(File.Exists), "在第三页点击启动分组仍启动全部九项");
                        Check(main.IsVisible && groupLaunch.IsEnabled, "关闭自动退出时完成启动后窗口保留并恢复交互");
                        search.Clear(); main.Width = 800; main.Height = 540; main.UpdateLayout();
                        var scroll = (ScrollViewer)typeof(MainWindow).GetField("scroller", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
                        Check(scroll.ScrollableHeight > 0, "小窗口允许滚动浏览全部分组"); scroll.ScrollToEnd(); main.UpdateLayout(); Check(scroll.VerticalOffset > 0, "滚动条实际移动内容");
                        var roundtrip = ConfigStore.Read(store.FilePath); Check(roundtrip.Groups.Count == 4, "所有界面操作后的配置仍可读取");
                    }
                    catch (Exception e) { failure = e; log.Add(e.ToString()); }
                    finally
                    {
                        File.WriteAllText(Path.Combine(folder, "ui-test-results.txt"), String.Join("\r\n", log) + "\r\nTOTAL: " + log.Count(x => x.StartsWith("PASS:")) + " passed\r\n", Encoding.UTF8);
                        main.Close(); app.Shutdown(failure == null ? 0 : 1);
                    }
                }), DispatcherPriority.ApplicationIdle);
            };
            return app.Run(main);
        }
    }
}
