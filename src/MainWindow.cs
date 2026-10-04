using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace OrbitLauncher
{
    public sealed class MainWindow : SurfaceWindow
    {
        readonly ConfigStore store;
        Configuration config;
        readonly Launcher launcher = new Launcher();
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly WrapPanel cards = new WrapPanel();
        readonly TextBlock status = Ui.Text("", 12, Ui.Muted);
        readonly TextBlock count = Ui.Text("", 12, Ui.Muted);
        readonly TextBox search = new TextBox();
        readonly List<Control> actions = new List<Control>();
        readonly ScrollViewer scroller = new ScrollViewer();
        readonly GroupPages pages = new GroupPages();
        readonly Border toast = new Border();
        readonly TextBlock toastText = Ui.Text("", 12, Ui.Color("green"));
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        sealed class CardView { public FrameworkElement Element; public string Signature; public int Index; public int Total; }
        readonly Dictionary<string, CardView> cardViews = new Dictionary<string, CardView>();
        readonly Dictionary<string, Action> refreshPages = new Dictionary<string, Action>();
        readonly Dictionary<string, HashSet<string>> failedItems = new Dictionary<string, HashSet<string>>();
        readonly DispatcherTimer searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        FrameworkElement addCard;
        FrameworkElement emptySearch;
        bool composing;
        bool saving;
        bool preparing;
        bool closing;
        readonly Queue<string> queuedGroups = new Queue<string>();
        string runningGroup;
        bool Busy { get { return launching || saving || preparing; } }
        public Task<bool> PendingSave { get; private set; }
        public Task PendingEdit { get; private set; }
        bool explicitExit;
        bool launching;
        public Configuration Config { get { return config; } }
        public MainWindow(ConfigStore configStore, Configuration initial)
        {
            store = configStore; config = initial;
            Style = (Style)Application.Current.FindResource(typeof(Window));
            Title = "轻启 · 一键启动"; Width = 1160; Height = 760; MinWidth = 800; MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = Ui.Logo();
            Build(); Render(); WindowPlacement.Restore(this, store.Folder);
            Closing += OnClosing;
            Loaded += delegate
            {
                if (!String.IsNullOrEmpty(store.RecoveryMessage)) status.Text = store.RecoveryMessage;
                Ui.Fade(scroller);
            };
            Closed += delegate { toastTimer.Stop(); searchTimer.Stop(); cancellation.Cancel(); };
        }
        void Build()
        {
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(148) }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
            SetSurface(new Border { CornerRadius = new CornerRadius(16), Background = Ui.Gradient("#F9FAFD", "#EDF1F7"), BorderBrush = Ui.Brush("#DFE5EE"), BorderThickness = new Thickness(1), Child = root });
            root.Children.Add(Caption(true, "轻启"));
            var header = new Grid { Margin = new Thickness(36, 14, 36, 16) };
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var eyebrow = Ui.Text("让日常，轻轻开始", 12, Ui.Muted); eyebrow.Margin = new Thickness(0, 0, 0, 9); title.Children.Add(eyebrow);
            var headline = Ui.Text("准备好，开始你的下一刻。", 29, Ui.Ink); headline.FontWeight = FontWeights.SemiBold; title.Children.Add(headline);
            var subtitle = Ui.Text("把常用应用放在一起。一个分组，一次开启。", 13, Ui.Muted); subtitle.Margin = new Thickness(0, 10, 0, 0); title.Children.Add(subtitle); header.Children.Add(title);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7) };
            var searchGrid = new Grid { Width = 205, Margin = new Thickness(0, 0, 10, 0) };
            search.Height = 39; search.Padding = new Thickness(32, 9, 10, 9); search.ToolTip = "搜索分组或应用";
            searchGrid.Children.Add(search);
            var hint = Ui.Text("搜索分组或应用", 12, Ui.Muted); hint.Margin = new Thickness(33, 0, 0, 0); hint.IsHitTestVisible = false; searchGrid.Children.Add(hint);
            var lens = Ui.Text("\uE721", 12, Ui.Muted); lens.FontFamily = new FontFamily("Segoe MDL2 Assets"); lens.Margin = new Thickness(12, 0, 0, 0); lens.IsHitTestVisible = false; searchGrid.Children.Add(lens);
            searchTimer.Tick += delegate { searchTimer.Stop(); if (!composing) ApplyFilter(); };
            search.TextChanged += delegate
            {
                hint.Visibility = String.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
                searchTimer.Stop(); if (search.Text.Length == 0) ApplyFilter(); else searchTimer.Start();
            };
            TextCompositionManager.AddPreviewTextInputStartHandler(search, delegate { composing = true; searchTimer.Stop(); });
            TextCompositionManager.AddPreviewTextInputUpdateHandler(search, delegate { composing = true; searchTimer.Stop(); });
            search.PreviewTextInput += delegate { composing = false; searchTimer.Stop(); searchTimer.Start(); };
            search.LostKeyboardFocus += delegate { composing = false; searchTimer.Stop(); ApplyFilter(); };
            tools.Children.Add(searchGrid);
            var add = Ui.Button("＋  新建分组", AddGroup, false); add.Height = 39; tools.Children.Add(add); actions.Add(add); header.Children.Add(tools);
            SizeChanged += delegate { title.MaxWidth = Math.Max(320, ActualWidth - 90); searchGrid.Width = ActualWidth < 1040 ? 160 : 205; };
            Grid.SetRow(header, 1); root.Children.Add(header);
            var area = new Grid { Margin = new Thickness(36, 0, 24, 0) }; area.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); area.RowDefinitions.Add(new RowDefinition());
            var labels = new Grid(); labels.Children.Add(Ui.Text("我的分组", 12, Ui.Muted)); count.HorizontalAlignment = HorizontalAlignment.Right; count.Margin = new Thickness(0, 0, 16, 0); labels.Children.Add(count); area.Children.Add(labels);
            cards.Margin = new Thickness(0, 8, 0, 8); scroller.Content = cards; scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Grid.SetRow(scroller, 1); area.Children.Add(scroller); Grid.SetRow(area, 2); root.Children.Add(area);
            var footer = new Grid { Margin = new Thickness(36, 0, 36, 0) };
            var all = Ui.Button("\u25B7  启动全部", delegate { RunAll(); }, false); all.HorizontalAlignment = HorizontalAlignment.Left; all.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(all); actions.Add(all);
            status.Margin = new Thickness(140, 0, 110, 0); status.TextTrimming = TextTrimming.CharacterEllipsis; status.ToolTip = "启动只发送打开请求；应用加载时间由各应用决定。"; footer.Children.Add(status);
            var settings = Ui.Quiet("\u2699  设置", ShowSettings); settings.HorizontalAlignment = HorizontalAlignment.Right; settings.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(settings); actions.Add(settings);
            Grid.SetRow(footer, 3); root.Children.Add(footer);
            var topLine = new Border { Height = 1, Background = Ui.Brush("#EAEDF2"), VerticalAlignment = VerticalAlignment.Top }; Grid.SetRow(topLine, 3); root.Children.Add(topLine);
            toast.Background = Ui.Surface(); toast.BorderBrush = Ui.Brush("#DCE9E2"); toast.BorderThickness = new Thickness(1); toast.CornerRadius = new CornerRadius(12); toast.Padding = new Thickness(16, 11, 16, 11); toast.Effect = Ui.Shadow(.15, 16, 4); toast.VerticalAlignment = VerticalAlignment.Bottom; toast.HorizontalAlignment = HorizontalAlignment.Center; toast.Margin = new Thickness(0, 0, 0, 90); toast.Visibility = Visibility.Collapsed; toast.IsHitTestVisible = false;
            var toastBody = new StackPanel { Orientation = Orientation.Horizontal }; var tick = Ui.Glyph("\uE73E", 13, Ui.Color("green")); tick.Margin = new Thickness(0, 0, 9, 0); toastBody.Children.Add(tick); toastBody.Children.Add(toastText); toast.Child = toastBody; Grid.SetRowSpan(toast, 4); root.Children.Add(toast);
            toastTimer.Tick += delegate { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
            InputBindings.Add(new KeyBinding(new SimpleCommand(delegate { if (!Busy) AddGroup(); }), Key.N, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new SimpleCommand(delegate { search.Focus(); }), Key.F, ModifierKeys.Control));
        }
        public void Render()
        {
            pages.Retain(config.Groups.Select(g => g.Id));
            foreach (var id in cardViews.Keys.Where(id => config.Groups.All(g => g.Id != id)).ToList())
            { cards.Children.Remove(cardViews[id].Element); cardViews.Remove(id); refreshPages.Remove(id); failedItems.Remove(id); }
            if (addCard == null) { addCard = AddCard(); emptySearch = Ui.Text("没有找到相关分组或应用。", 14, Ui.Muted); emptySearch.Width = 500; emptySearch.Margin = new Thickness(0, 45, 0, 45); }
            for (int index = 0; index < config.Groups.Count; index++)
            {
                var group = config.Groups[index]; CardView view;
                string signature = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(group);
                if (!cardViews.TryGetValue(group.Id, out view) || view.Signature != signature || view.Index != index || view.Total != config.Groups.Count)
                {
                    if (view != null) cards.Children.Remove(view.Element);
                    view = new CardView { Element = Card(group), Signature = signature, Index = index, Total = config.Groups.Count }; cardViews[group.Id] = view;
                }
                int current = cards.Children.IndexOf(view.Element);
                if (current != index) { if (current >= 0) cards.Children.RemoveAt(current); cards.Children.Insert(index, view.Element); }
            }
            if (!cards.Children.Contains(emptySearch)) cards.Children.Add(emptySearch);
            if (!cards.Children.Contains(addCard)) cards.Children.Add(addCard);
            count.Text = config.Groups.Count + " 个分组  ·  " + config.Groups.Sum(g => g.Items.Count) + " 个应用";
            ApplyFilter();
            if (!Busy) status.Text = config.Settings.ExitAfterLaunch ? "完成启动后，轻启将自动退出" : "分组与设置保存在这台电脑";
        }
        void ApplyFilter()
        {
            string q = search.Text.Trim(); int visible = 0;
            foreach (var group in config.Groups)
            {
                CardView view; if (!cardViews.TryGetValue(group.Id, out view)) continue;
                bool nameMatch = group.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                int found = q.Length == 0 ? -1 : group.Items.FindIndex(i => i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                bool show = q.Length == 0 || nameMatch || found >= 0;
                view.Element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) visible++;
                if (show && !nameMatch && found >= 0 && pages.Get(group.Id, group.Items.Count) != found / GroupPages.PageSize)
                { pages.Set(group.Id, group.Items.Count, found / GroupPages.PageSize); Action update; if (refreshPages.TryGetValue(group.Id, out update)) update(); }
            }
            if (emptySearch != null) emptySearch.Visibility = visible == 0 && q.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        FrameworkElement Card(LaunchGroup group)
        {
            var frame = new Grid { Width = 294, Height = 356, Margin = new Thickness(4, 4, 14, 20) };
            var shadow = new Border { CornerRadius = new CornerRadius(18), Background = Brushes.White, Effect = Ui.Shadow(.12, 14, 4), IsHitTestVisible = false }; frame.Children.Add(shadow);
            var card = new Border { Tag = "group:" + group.Id, CornerRadius = new CornerRadius(18), Background = Ui.Surface(), BorderBrush = Ui.Brush("#FFFFFF"), BorderThickness = new Thickness(1), Padding = new Thickness(20), AllowDrop = true }; frame.Children.Add(card);
            card.MouseEnter += delegate { shadow.Effect = Ui.Shadow(.18, 17, 5); card.BorderBrush = Ui.Brush("#D7E0F2"); };
            card.MouseLeave += delegate { shadow.Effect = Ui.Shadow(.12, 14, 4); card.BorderBrush = Brushes.White; };
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(164) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); card.Child = grid;
            var header = new Grid(); var mark = Ui.GroupMark(group.Color, 42); mark.HorizontalAlignment = HorizontalAlignment.Left; mark.VerticalAlignment = VerticalAlignment.Top; header.Children.Add(mark);
            var titles = new StackPanel { Margin = new Thickness(54, 0, 28, 0) };
            var name = Ui.Text(group.Name, 17, Ui.Ink); name.FontWeight = FontWeights.SemiBold; name.ToolTip = group.Name; titles.Children.Add(name);
            var number = Ui.Text(group.Items.Count + " 个应用", 11, Ui.Muted); number.Margin = new Thickness(0, 5, 0, 0); titles.Children.Add(number); header.Children.Add(titles);
            var more = Ui.Quiet("\uE712", delegate { }); more.FontFamily = new FontFamily("Segoe MDL2 Assets"); more.FontSize = 14; more.Width = 28; more.Padding = new Thickness(3, 6, 3, 6); more.HorizontalAlignment = HorizontalAlignment.Right; more.VerticalAlignment = VerticalAlignment.Top; more.ToolTip = "管理分组";
            var menu = new ContextMenu(); AddMenu(menu, "编辑分组", delegate { EditGroup(group); }); AddMenu(menu, "添加应用…", delegate { AddItem(group, null); });
            AddMenu(menu, "复制分组", delegate { DuplicateGroup(group); }); AddMenu(menu, "创建桌面快捷方式", delegate { CreateShortcut(group); }); menu.Items.Add(new Separator());
            AddMenu(menu, "重新启动整个分组", delegate { RunGroup(group); }, group.Items.Count > 0); menu.Items.Add(new Separator());
            AddMenu(menu, "向前移动", delegate { MoveGroup(group, -1); }, config.Groups.FindIndex(g => g.Id == group.Id) > 0); AddMenu(menu, "向后移动", delegate { MoveGroup(group, 1); }, config.Groups.FindIndex(g => g.Id == group.Id) < config.Groups.Count - 1); menu.Items.Add(new Separator());
            AddMenu(menu, "删除分组…", delegate { DeleteGroup(group); }); more.ContextMenu = menu; more.Click += delegate { menu.PlacementTarget = more; menu.IsOpen = true; }; header.Children.Add(more); grid.Children.Add(header);
            var body = new StackPanel { Tag = "page-items", ClipToBounds = true };
            if (group.Items.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(0, 36, 0, 0) };
                empty.Children.Add(Ui.Text("你的日常，从这里开始", 13, Ui.Muted));
                var tip = Ui.Text("拖入桌面快捷方式，或添加应用", 11, Ui.Muted); tip.Margin = new Thickness(0, 9, 0, 0); empty.Children.Add(tip); body.Children.Add(empty);
            }
            var navigation = new Grid(); var addApp = Ui.Quiet("＋  添加应用", delegate { AddItem(group, null); }); addApp.HorizontalAlignment = HorizontalAlignment.Left; addApp.FontSize = 11; addApp.Margin = new Thickness(-8, 0, 0, 0); navigation.Children.Add(addApp);
            if (group.Items.Count > 0)
            {
                int totalPages = GroupPages.Count(group.Items.Count);
                var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Visibility = totalPages > 1 ? Visibility.Visible : Visibility.Collapsed };
                var pageLabel = Ui.Text("", 11, Ui.Muted); pageLabel.Width = 39; pageLabel.TextAlignment = TextAlignment.Center;
                Action update = null;
                var previous = Ui.Quiet("‹", delegate { pages.Set(group.Id, group.Items.Count, pages.Get(group.Id, group.Items.Count) - 1); update(); Ui.Fade(body); });
                var next = Ui.Quiet("›", delegate { pages.Set(group.Id, group.Items.Count, pages.Get(group.Id, group.Items.Count) + 1); update(); Ui.Fade(body); });
                previous.Tag = "page-prev:" + group.Id; next.Tag = "page-next:" + group.Id; previous.ToolTip = "上一页"; next.ToolTip = "下一页";
                AutomationProperties.SetName(previous, group.Name + "，上一页"); AutomationProperties.SetName(next, group.Name + "，下一页");
                foreach (var b in new[] { previous, next }) { b.FontSize = 20; b.Width = 27; b.Height = 28; b.Padding = new Thickness(0); }
                update = delegate
                {
                    int page = pages.Get(group.Id, group.Items.Count); body.Children.Clear();
                    foreach (var item in group.Items.Skip(page * GroupPages.PageSize).Take(GroupPages.PageSize)) body.Children.Add(ItemRow(group, item));
                    pageLabel.Text = (page + 1) + " / " + totalPages; previous.IsEnabled = page > 0; next.IsEnabled = page < totalPages - 1;
                };
                pager.Children.Add(previous); pager.Children.Add(pageLabel); pager.Children.Add(next); navigation.Children.Add(pager); refreshPages[group.Id] = update; update();
            }
            Grid.SetRow(body, 1); grid.Children.Add(body);
            Grid.SetRow(navigation, 2); grid.Children.Add(navigation);
            var launch = Ui.Button("\u25B7   启动分组" + (group.Items.Count > 0 ? " · " + group.Items.Count : ""), delegate { RunRequested(group); }, false); launch.Tag = "launch:" + group.Id; launch.Background = Ui.Gradient("#F9FBFF", ((SolidColorBrush)Ui.Pale(group.Color)).Color.ToString()); launch.Foreground = Ui.Color(group.Color); launch.BorderBrush = Ui.Pale(group.Color); launch.FontWeight = FontWeights.SemiBold; launch.IsEnabled = group.Items.Count > 0; launch.ToolTip = "启动此分组的全部 " + group.Items.Count + " 个应用";
            UpdateLaunchButton(group.Id, launch);
            Grid.SetRow(launch, 4); grid.Children.Add(launch);
            card.DragOver += delegate(object s, DragEventArgs e) { e.Effects = !Busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            card.Drop += delegate(object s, DragEventArgs e)
            {
                if (Busy || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = (string[])e.Data.GetData(DataFormats.FileDrop); AddDropped(group, files); e.Handled = true;
            };
            return frame;
        }
        FrameworkElement ItemRow(LaunchGroup group, LaunchItem item)
        {
            var row = new Grid { Tag = "item:" + item.Id, Height = 38, Margin = new Thickness(0, 0, 0, 2), ToolTip = item.Target };
            var icon = Ui.AppIcon(item, 25); icon.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(icon);
            var name = Ui.Text(item.Name, 13, Ui.Ink); name.Margin = new Thickness(36, 0, 27, 0); row.Children.Add(name);
            Ui.ValidateRow(row, name, item);
            var play = Ui.Quiet("\u25B7", delegate { RunSingle(item); }); play.Padding = new Thickness(5); play.Width = 24; play.Height = 25; play.HorizontalAlignment = HorizontalAlignment.Right; play.ToolTip = "只启动 " + item.Name; row.Children.Add(play);
            var menu = new ContextMenu(); AddMenu(menu, "编辑应用", delegate { EditItem(group, item); }); AddMenu(menu, "刷新图标", delegate { Ui.RefreshIcons(row); }); AddMenu(menu, "从分组移除…", delegate { RemoveItem(group, item); }); row.ContextMenu = menu;
            return row;
        }
        FrameworkElement AddCard()
        {
            var plus = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var symbol = Ui.Text("＋", 30, Ui.Muted); symbol.HorizontalAlignment = HorizontalAlignment.Center; plus.Children.Add(symbol);
            var label = Ui.Text("新的日常", 12, Ui.Muted); label.Margin = new Thickness(0, 10, 0, 0); label.HorizontalAlignment = HorizontalAlignment.Center; plus.Children.Add(label);
            var button = Ui.Button("", AddGroup, false); button.Content = plus; button.Width = 140; button.Height = 356; button.Margin = new Thickness(4, 4, 14, 20); button.Background = Ui.Gradient("#F7F9FD", "#E9EEF7"); button.BorderBrush = Ui.Brush("#E1E7F0"); button.ToolTip = "新建分组 · Ctrl+N"; return button;
        }
        static void AddMenu(ContextMenu menu, string label, Action action, bool enabled = true)
        {
            string glyph = label.Contains("刷新") ? "\uE72C" : label.Contains("启动") ? "\uE768" : label.Contains("编辑") ? "\uE70F" : label.Contains("添加") ? "\uE710" : label.Contains("复制") ? "\uE8C8" : label.Contains("快捷") ? "\uE8A7" : label.Contains("向前") ? "\uE72B" : label.Contains("向后") ? "\uE72A" : "\uE74D";
            bool danger = label.Contains("删除") || label.Contains("移除");
            var i = new MenuItem { Header = label, IsEnabled = enabled }; i.Icon = Ui.Glyph(glyph, 14, danger ? Ui.Brush("#B45561") : Ui.Muted); if (danger) i.Foreground = Ui.Brush("#B45561"); i.Click += delegate { action(); }; menu.Items.Add(i);
        }
        Task<bool> Commit(Configuration next)
        {
            if (Busy) return Task.FromResult(false);
            PendingSave = SaveConfiguration(next); return PendingSave;
        }
        async Task<bool> SaveConfiguration(Configuration next)
        {
            saving = true; UpdateAvailability(); status.Text = "正在保存…";
            try { await store.SaveAsync(next); config = next; Render(); Toast("已保存"); return true; }
            catch (Exception e) { Ui.Error(this, "保存失败，修改尚未应用。\n" + e.Message); return false; }
            finally { saving = false; UpdateAvailability(); if (status.Text == "正在保存…") status.Text = "分组与设置保存在这台电脑"; ScheduleQueued(); }
        }
        void Toast(string text) { toastText.Text = text; toast.Visibility = Visibility.Visible; Ui.Fade(toast); toastTimer.Stop(); toastTimer.Start(); }
        async void AddGroup()
        {
            if (Busy) return;
            if (config.Groups.Count >= 100) { Ui.Error(this, "最多可创建 100 个分组。"); return; }
            var dialog = new GroupDialog(this, new LaunchGroup(), config);
            if (dialog.ShowDialog() != true) return;
            var next = ConfigCodec.Clone(config); next.Groups.Add(dialog.Group); await Commit(next);
        }
        async void EditGroup(LaunchGroup group)
        {
            if (Busy) return;
            var next = ConfigCodec.Clone(config); var copy = next.Groups.First(g => g.Id == group.Id);
            var dialog = new GroupDialog(this, copy, config);
            if (dialog.ShowDialog() == true) await Commit(next);
        }
        async void AddItem(LaunchGroup group, string target)
        {
            if (Busy) return;
            if (group.Items.Count >= 200) { Ui.Error(this, "每组最多 200 个应用。"); return; }
            var dialog = new ItemDialog(this, new LaunchItem { Name = target == null ? "" : Targets.GuessName(target), Target = target ?? "" }, config);
            if (dialog.ShowDialog() != true) return;
            var next = ConfigCodec.Clone(config); next.Groups.First(g => g.Id == group.Id).Items.Add(dialog.Item); await Commit(next);
        }
        async void EditItem(LaunchGroup group, LaunchItem item)
        {
            if (Busy) return;
            var next = ConfigCodec.Clone(config); var copy = next.Groups.First(g => g.Id == group.Id).Items.First(i => i.Id == item.Id);
            var dialog = new ItemDialog(this, copy, config); if (dialog.ShowDialog() == true) await Commit(next);
        }
        async void RemoveItem(LaunchGroup group, LaunchItem item)
        {
            if (Busy) return;
            if (!Ui.Confirm(this, "移除“" + item.Name + "”？", "将从“" + group.Name + "”移除此启动配置。电脑上的原软件会保留。", "移除应用", true)) return;
            var next = ConfigCodec.Clone(config); next.Groups.First(g => g.Id == group.Id).Items.RemoveAll(i => i.Id == item.Id); await Commit(next);
        }
        void AddDropped(LaunchGroup group, string[] targets) { if (!Busy) PendingEdit = DropItems(group, targets); }
        async Task DropItems(LaunchGroup group, string[] targets)
        {
            if (Busy) return;
            preparing = true; UpdateAvailability();
            var next = ConfigCodec.Clone(config); var g = next.Groups.First(x => x.Id == group.Id); int added = 0;
            try
            {
                await Task.Run(delegate
                {
                    foreach (string path in targets)
                    {
                        if (g.Items.Count >= 200) break;
                        if (g.Items.Any(i => String.Equals(Targets.Clean(i.Target), path, StringComparison.OrdinalIgnoreCase))) continue;
                        var item = new LaunchItem { Name = Targets.GuessName(path), Target = path };
                        if (String.IsNullOrWhiteSpace(item.Name)) item.Name = path;
                        if (Targets.Error(item) != null) continue;
                        g.Items.Add(item); added++;
                    }
                });
            }
            catch (Exception e) { Ui.Error(this, "无法读取拖入的项目：" + e.Message); return; }
            finally { preparing = false; UpdateAvailability(); }
            if (!IsVisible) return;
            if (added > 0 && await Commit(next)) { status.Text = "已添加 " + added + " 个项目到“" + group.Name + "”"; Toast("已添加 " + added + " 个应用"); }
            else status.Text = "没有添加新项目；请检查位置、重复项目及数量上限。";
            ScheduleQueued();
        }
        async void DuplicateGroup(LaunchGroup group)
        {
            if (Busy) return;
            if (config.Groups.Count >= 100) { Ui.Error(this, "最多可创建 100 个分组。"); return; }
            var next = ConfigCodec.Clone(config); var copy = ConfigCodec.Clone(new Configuration { Groups = new List<LaunchGroup> { group } }).Groups[0];
            copy.Id = Guid.NewGuid().ToString("N"); copy.Name = group.Name.Length <= 35 ? group.Name + " 副本" : group.Name.Substring(0, 35) + " 副本";
            foreach (var i in copy.Items) i.Id = Guid.NewGuid().ToString("N"); next.Groups.Insert(config.Groups.FindIndex(g => g.Id == group.Id) + 1, copy); await Commit(next);
        }
        async void MoveGroup(LaunchGroup group, int direction)
        {
            if (Busy) return;
            var next = ConfigCodec.Clone(config); int index = next.Groups.FindIndex(g => g.Id == group.Id), target = index + direction;
            if (target < 0 || target >= next.Groups.Count) return;
            var moved = next.Groups[index]; next.Groups.RemoveAt(index); next.Groups.Insert(target, moved); await Commit(next);
        }
        async void DeleteGroup(LaunchGroup group)
        {
            if (Busy) return;
            if (!Ui.Confirm(this, "删除“" + group.Name + "”？", "将移除此分组及其中的启动配置。电脑上的应用和文件会保留。", "删除分组", true)) return;
            var next = ConfigCodec.Clone(config); next.Groups.RemoveAll(g => g.Id == group.Id); await Commit(next);
        }
        void ShowSettings()
        {
            if (Busy) return;
            new SettingsDialog(this, config, store, Commit).ShowDialog();
        }
        public void RunGroup(LaunchGroup group)
        {
            var current = config.Groups.FirstOrDefault(g => g.Id == group.Id); if (current != null) RunItems(current.Items, current.Name, true, current.Id);
        }
        void RunRequested(LaunchGroup group)
        {
            var current = config.Groups.FirstOrDefault(g => g.Id == group.Id); if (current == null) return;
            HashSet<string> failed;
            var items = failedItems.TryGetValue(current.Id, out failed) ? current.Items.Where(i => failed.Contains(i.Id)) : current.Items;
            RunItems(items, current.Name, true, current.Id);
        }
        Button GroupButton(string id)
        {
            CardView view; return id != null && cardViews.TryGetValue(id, out view) ? Ui.Descendants<Button>(view.Element).FirstOrDefault(b => b.Tag as string == "launch:" + id) : null;
        }
        void UpdateLaunchButton(string id, Button button)
        {
            if (button == null) return; var group = config.Groups.FirstOrDefault(g => g.Id == id); if (group == null) return;
            HashSet<string> failures;
            int retry = failedItems.TryGetValue(id, out failures) ? group.Items.Count(i => failures.Contains(i.Id)) : 0;
            if (retry == 0) failedItems.Remove(id);
            button.Content = retry > 0 ? "↻   重试失败项 · " + retry : "\u25B7   启动分组" + (group.Items.Count > 0 ? " · " + group.Items.Count : "");
            button.ToolTip = retry > 0 ? "仅重试失败的项目；菜单中可重新启动整个分组" : "启动此分组的全部 " + group.Items.Count + " 个应用";
        }
        void RunSingle(LaunchItem item) { RunItems(new[] { item }, item.Name, false); }
        void RunAll()
        {
            var items = config.Groups.SelectMany(g => g.Items).GroupBy(Targets.Key, StringComparer.Ordinal).Select(g => g.First()).ToList();
            RunItems(items, "全部分组", true);
        }
        async void RunItems(IEnumerable<LaunchItem> source, string label, bool allowExit, string groupId = null)
        {
            if (Busy) return;
            var items = source.ToList(); var launchButton = GroupButton(groupId); if (items.Count == 0) { status.Text = "先添加应用，再开启你的日常。"; return; }
            SetBusy(true);
            runningGroup = groupId;
            try
            {
                var results = await launcher.RunAsync(items, config.Settings.DelayMs, delegate(int n, int total, LaunchItem item) { status.Text = "正在启动 " + n + " / " + total + " · " + item.Name; if (launchButton != null) launchButton.Content = "正在启动 · " + n + " / " + total; }, cancellation.Token);
                var failures = results.Where(r => !r.Success).ToList();
                if (groupId != null) { if (failures.Count > 0) failedItems[groupId] = new HashSet<string>(failures.Select(r => r.Item.Id)); else failedItems.Remove(groupId); }
                if (cancellation.IsCancellationRequested) return;
                if (failures.Count > 0)
                {
                    status.Text = "已发送 " + results.Count(r => r.Success) + " 个启动请求，" + failures.Count + " 项未能打开";
                    var text = String.Join("\n\n", failures.Take(12).Select(r => r.Item.Name + "\n" + r.Error));
                    if (failures.Count > 12) text += "\n\n另有 " + (failures.Count - 12) + " 项失败。请检查分组中的位置。";
                    Ui.Error(this, text + "\n\n已打开的应用继续运行。可右键编辑应用位置，再重试。");
                }
                else
                {
                    status.Text = "“" + label + "”的 " + results.Count + " 个启动请求已发送";
                    if (allowExit && config.Settings.ExitAfterLaunch && queuedGroups.Count == 0)
                    {
                        await Task.Delay(350);
                        if (!closing && !cancellation.IsCancellationRequested && queuedGroups.Count == 0)
                        {
                            await WindowPlacement.Save(this, store.Folder);
                            if (!closing && !cancellation.IsCancellationRequested && queuedGroups.Count == 0) { explicitExit = true; Close(); }
                        }
                    }
                }
            }
            catch (Exception e) { Ui.Error(this, e.Message); }
            finally { runningGroup = null; SetBusy(false); if (groupId != null) UpdateLaunchButton(groupId, GroupButton(groupId)); ScheduleQueued(); }
        }
        void SetBusy(bool value) { launching = value; UpdateAvailability(); }
        void UpdateAvailability()
        {
            cards.IsEnabled = !Busy; search.IsEnabled = !launching;
            foreach (var action in actions) action.IsEnabled = !Busy;
        }
        async void OnClosing(object sender, CancelEventArgs e)
        {
            if (explicitExit) return;
            e.Cancel = true; if (closing) return; closing = true;
            try
            {
                // Always leave the original Closing event before attempting the final Close.
                await Task.Yield();
                if (PendingEdit != null && !PendingEdit.IsCompleted) await PendingEdit;
                if (PendingSave != null && !PendingSave.IsCompleted && !await PendingSave) return;
                if (launching)
                {
                    if (!Ui.Confirm(this, "停止启动并退出？", "尚未发送的启动请求将取消，已打开的应用会继续运行。", "停止并退出", true)) return;
                    cancellation.Cancel();
                }
                await WindowPlacement.Save(this, store.Folder); explicitExit = true; Close();
            }
            finally { closing = false; }
        }
        public void Receive(string command)
        {
            if (!IsVisible || closing) return;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            NativeDepth.Reveal(this);
            var modal = OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible); if (modal != null) modal.Activate(); else Activate();
            if (command == "activate") return;
            var group = config.Groups.FirstOrDefault(g => String.Equals(g.Id, command, StringComparison.OrdinalIgnoreCase));
            if (group == null) { Toast("此分组已不存在，请重新创建快捷方式"); return; }
            if (String.Equals(runningGroup, group.Id, StringComparison.OrdinalIgnoreCase)) { Toast("此分组正在启动"); return; }
            if (Busy || modal != null)
            {
                if (!queuedGroups.Contains(group.Id)) queuedGroups.Enqueue(group.Id);
                Toast("已排队，当前操作完成后启动“" + group.Name + "”"); return;
            }
            RunGroup(group);
        }
        protected override void OnModalClosed() { ScheduleQueued(); }
        void ScheduleQueued()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (Busy || closing || !IsVisible || OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) return;
                while (queuedGroups.Count > 0)
                {
                    string id = queuedGroups.Dequeue(); var group = config.Groups.FirstOrDefault(g => g.Id == id);
                    if (group != null && group.Items.Count > 0) { RunGroup(group); return; }
                }
            }), DispatcherPriority.ApplicationIdle);
        }
        void CreateShortcut(LaunchGroup group)
        {
            if (Busy) return;
            var dialog = new SaveFileDialog { Title = "创建分组快捷方式", Filter = "Windows 快捷方式 (*.lnk)|*.lnk", FileName = SafeName(group.Name) + ".lnk", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AddExtension = true, DefaultExt = ".lnk" };
            if (dialog.ShowDialog(this) != true) return;
            object shell = null, link = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type);
                link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { dialog.FileName });
                var lt = link.GetType(); string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { exe });
                lt.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { "--group " + group.Id });
                lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { exe + ",0" });
                lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { "启动轻启分组：" + group.Name });
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, new object[0]); status.Text = "已创建“" + group.Name + "”的快捷方式"; Toast("分组快捷方式已创建");
            }
            catch (Exception e) { Ui.Error(this, "创建快捷方式失败：" + e.Message); }
            finally { if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        }
        static string SafeName(string name) { foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name; }
    }
    public sealed class SimpleCommand : ICommand
    {
        readonly Action action; public SimpleCommand(Action a) { action = a; }
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { action(); }
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }
}
