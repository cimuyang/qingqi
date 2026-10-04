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
using System.Windows.Threading;
using System.Threading.Tasks;

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
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder); string root = Path.Combine(folder, "ui-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var app = Entry.CreateApplication(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var store = new ConfigStore(root); var main = new MainWindow(store, Configuration.Initial());
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
                        await Modal<NoticeDialog>(main, delegate { Invoke(main, "DeleteGroup", main.Config.Groups.First(g => g.Id == id)); }, delegate(NoticeDialog d)
                        {
                            Check(Visuals<Button>(d).First(b => b.Content as string == "取消").IsDefault && !Visuals<Button>(d).First(b => b.Content as string == "删除分组").IsDefault, "删除确认默认取消并使用明确操作名称"); Click(d, "取消");
                        });
                        Check(main.Config.Groups.Count == beforeDelete, "取消新版删除确认保留原分组");
                        var cancelledId = main.Config.Groups.First(g => g.Id != id).Id;
                        await Modal<NoticeDialog>(main, delegate { Invoke(main, "DeleteGroup", main.Config.Groups.First(g => g.Id == cancelledId)); }, delegate(NoticeDialog d) { Click(d, "删除分组"); });
                        Check(main.Config.Groups.Count == beforeDelete - 1 && store.Load().Groups.All(g => g.Id != cancelledId), "新版删除确认执行并持久化");
                        var launchConfig = ConfigCodec.Clone(main.Config); var launchGroup = launchConfig.Groups.First(g => g.Id == id); launchConfig.Settings.ExitAfterLaunch = false; launchConfig.Settings.DelayMs = 0;
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
