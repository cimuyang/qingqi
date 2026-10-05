using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace OrbitLauncher
{
    public class Sheet : SurfaceWindow
    {
        protected readonly StackPanel Body = new StackPanel();
        protected readonly StackPanel Footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        protected readonly TextBlock Feedback = Ui.Text("", 12, Ui.Brush("#B5604B"));
        public Sheet(Window owner, string title, string subtitle, double height)
        {
            Owner = owner; Title = title + " · 轻启"; Width = 600; Height = Math.Min(height + 44, SystemParameters.WorkArea.Height - 64); MinWidth = 520; MinHeight = 460;
            Style = (Style)Application.Current.FindResource(typeof(Window));
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false; Icon = Ui.Logo();
            var shell = new Grid(); shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); shell.RowDefinitions.Add(new RowDefinition()); shell.Children.Add(Caption(false, "轻启"));
            var root = new Grid { Margin = new Thickness(30, 15, 30, 25) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            var name = Ui.Text(title, 23, Ui.Ink); name.FontWeight = FontWeights.SemiBold; header.Children.Add(name);
            var hint = Ui.Text(subtitle, 12, Ui.Muted); hint.Margin = new Thickness(0, 8, 0, 0); hint.TextWrapping = TextWrapping.Wrap; header.Children.Add(hint); root.Children.Add(header);
            var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            var bottom = new StackPanel { Margin = new Thickness(0, 15, 0, 0) }; Feedback.TextWrapping = TextWrapping.Wrap; Feedback.Margin = new Thickness(0, 0, 0, 12); bottom.Children.Add(Feedback); bottom.Children.Add(Footer); Grid.SetRow(bottom, 2); root.Children.Add(bottom); Grid.SetRow(root, 1); shell.Children.Add(root);
            SetSurface(new Border { CornerRadius = new CornerRadius(16), Background = Ui.Surface(), BorderBrush = Ui.Brush("#E0E5EE"), BorderThickness = new Thickness(1), Child = shell });
        }
        protected void Label(string text) { var l = Ui.Text(text, 12, Ui.Ink); l.Margin = new Thickness(0, 14, 0, 7); Body.Children.Add(l); }
        protected void Help(string text) { var t = Ui.Text(text, 11, Ui.Muted); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 7, 0, 0); Body.Children.Add(t); }
        protected void Buttons(Action save, string label)
        {
            var cancel = Ui.Button("取消", delegate { DialogResult = false; }, false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 9, 0); Footer.Children.Add(cancel);
            var ok = Ui.Button(label, save, true); ok.IsDefault = true; Footer.Children.Add(ok);
        }
        bool pending;
        protected async void Perform(Func<Task> work)
        {
            if (pending) return; pending = true; Body.IsEnabled = false; Footer.IsEnabled = false;
            try { await work(); }
            catch (Exception e) { Feedback.Text = e.Message; }
            finally { pending = false; Body.IsEnabled = true; Footer.IsEnabled = true; }
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (pending && DialogResult != true) e.Cancel = true;
            base.OnClosing(e);
        }
        public void Preview(string path) { Ui.Snapshot(this, path); }
    }
    public sealed class ItemDialog : Sheet
    {
        readonly TextBox name = new TextBox { MaxLength = 80 };
        readonly TextBox target = new TextBox { MaxLength = 2048 };
        readonly TextBox arguments = new TextBox { MaxLength = 4096 };
        readonly TextBox working = new TextBox { MaxLength = 2048 };
        readonly Button test;
        bool testing;
        public LaunchItem Item { get; private set; }
        public ItemDialog(Window owner, LaunchItem item, Configuration source) : base(owner, "添加 / 编辑应用", "选择应用或快捷方式，名称与图标会自动带出。", 650)
        {
            Item = item; name.Text = item.Name ?? ""; target.Text = item.Target ?? ""; arguments.Text = item.Arguments ?? ""; working.Text = item.WorkingDirectory ?? "";
            var existing = source.Groups.SelectMany(g => g.Items).GroupBy(Targets.Key).Select(g => g.First()).ToList();
            if (existing.Count > 0)
            {
                Label("从已有应用复制"); var choices = new ComboBox { Height = 38, DisplayMemberPath = "Name", ItemsSource = existing, ToolTip = "复制配置，不影响其他分组" };
                choices.SelectionChanged += delegate { var i = choices.SelectedItem as LaunchItem; if (i == null) return; name.Text = i.Name; target.Text = i.Target; arguments.Text = i.Arguments ?? ""; working.Text = i.WorkingDirectory ?? ""; }; Body.Children.Add(choices);
            }
            Label("应用名称"); Body.Children.Add(name); Label("打开位置"); Body.Children.Add(target);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
            var browse = Ui.Button("选择文件…", Browse, false); browse.Margin = new Thickness(0, 0, 8, 0); tools.Children.Add(browse);
            var folder = Ui.Button("选择文件夹…", BrowseFolder, false); tools.Children.Add(folder); Body.Children.Add(tools);
            Help("支持应用、.lnk 快捷方式、文件、文件夹和 http / https 网页。也可以粘贴完整路径。");
            target.LostFocus += delegate { if (String.IsNullOrWhiteSpace(name.Text)) name.Text = Targets.GuessName(target.Text); };
            var advanced = new Expander { Header = "高级选项", Margin = new Thickness(0, 22, 0, 0), Foreground = Ui.Muted, IsExpanded = !String.IsNullOrWhiteSpace(item.Arguments) || !String.IsNullOrWhiteSpace(item.WorkingDirectory) };
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var a = Ui.Text("启动参数（可选）", 12, Ui.Ink); a.Margin = new Thickness(0, 0, 0, 7); panel.Children.Add(a); panel.Children.Add(arguments);
            var w = Ui.Text("工作目录（可选）", 12, Ui.Ink); w.Margin = new Thickness(0, 14, 0, 7); panel.Children.Add(w); panel.Children.Add(working); advanced.Content = panel; Body.Children.Add(advanced);
            test = Ui.Button("\u25B7  测试打开", Test, false); test.HorizontalAlignment = HorizontalAlignment.Left; test.Margin = new Thickness(0, 22, 0, 0); Body.Children.Add(test);
            Help("测试会实际打开此应用。分组配置在点击保存后生效。"); Buttons(delegate { Perform(Save); }, "保存应用");
            Loaded += delegate { name.Focus(); name.SelectAll(); };
        }
        LaunchItem Draft() { return new LaunchItem { Id = Item.Id, Name = name.Text.Trim(), Target = Targets.Clean(target.Text), Arguments = arguments.Text.Trim(), WorkingDirectory = Targets.Clean(working.Text) }; }
        void Browse()
        {
            var dialog = new OpenFileDialog { Title = "选择要打开的应用或文件", Filter = "应用与快捷方式|*.exe;*.lnk;*.appref-ms|所有文件|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return; target.Text = dialog.FileName;
            if (String.IsNullOrWhiteSpace(name.Text)) name.Text = Targets.GuessName(dialog.FileName);
        }
        void BrowseFolder()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "选择启动时打开的文件夹", ShowNewFolderButton = false })
            {
                if (d.ShowDialog(new DialogHandle(this)) != System.Windows.Forms.DialogResult.OK) return;
                target.Text = d.SelectedPath; if (String.IsNullOrWhiteSpace(name.Text)) name.Text = Targets.GuessName(d.SelectedPath);
            }
        }
        async void Test()
        {
            if (testing) return; testing = true; test.IsEnabled = false; Feedback.Foreground = Ui.Muted; Feedback.Text = "正在发送打开请求…";
            try { var result = await Launcher.OpenAsync(Draft()); Feedback.Foreground = result.Success ? Ui.Color("green") : Ui.Brush("#B5604B"); Feedback.Text = result.Success ? "打开请求已发送，请检查应用窗口。" : result.Error; }
            catch (Exception e) { Feedback.Text = e.Message; }
            finally { testing = false; test.IsEnabled = true; }
        }
        async Task Save()
        {
            Feedback.Foreground = Ui.Brush("#B5604B");
            var draft = Draft();
            if (String.IsNullOrWhiteSpace(draft.Name)) { Feedback.Text = "请填写应用名称。"; return; }
            string error = await Task.Run(delegate { return Targets.Error(draft); }); if (error != null) { Feedback.Text = error; return; }
            try { ConfigCodec.Validate(new Configuration { Groups = { new LaunchGroup { Items = { draft } } } }); }
            catch (Exception e) { Feedback.Text = e.Message; return; }
            Item.Name = draft.Name; Item.Target = draft.Target; Item.Arguments = draft.Arguments; Item.WorkingDirectory = draft.WorkingDirectory; DialogResult = true;
        }
    }
    public sealed class GroupDialog : Sheet
    {
        readonly TextBox name = new TextBox { MaxLength = 40 };
        readonly StackPanel list = new StackPanel();
        readonly Configuration config;
        readonly TextBlock itemCount = Ui.Text("", 12, Ui.Muted);
        readonly System.Collections.Generic.List<Button> swatches = new System.Collections.Generic.List<Button>();
        public LaunchGroup Group { get; private set; }
        public GroupDialog(Window owner, LaunchGroup group, Configuration source) : base(owner, "编辑你的日常", "设置分组名称、颜色与启动顺序。", 680)
        {
            Group = group; config = source; name.Text = group.Name; Label("分组名称"); Body.Children.Add(name); Label("分组颜色");
            var colors = new StackPanel { Orientation = Orientation.Horizontal }; string[] labels = { "蓝色", "绿色", "紫色", "橙色", "粉色", "灰色" }; int index = 0;
            foreach (var color in new[] { "blue", "green", "purple", "orange", "pink", "slate" })
            {
                string selected = color; var b = Ui.Button("", delegate { Group.Color = selected; UpdateColors(); }, false); b.Content = new Border { Width = 17, Height = 17, CornerRadius = new CornerRadius(9), Background = Ui.Color(color) }; b.Tag = color; b.ToolTip = labels[index++]; b.Padding = new Thickness(8); b.Margin = new Thickness(0, 0, 10, 0); colors.Children.Add(b); swatches.Add(b);
            }
            Body.Children.Add(colors); UpdateColors(); Label("应用与启动顺序"); Body.Children.Add(itemCount); list.Margin = new Thickness(0, 8, 0, 0); Body.Children.Add(list);
            var add = Ui.Button("＋  添加应用", Add, false); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 12, 0, 0); Body.Children.Add(add);
            Help("同一应用可放进多个分组。调整顺序只影响当前分组。"); Buttons(Save, "保存分组"); RenderItems();
            Loaded += delegate { name.Focus(); name.SelectAll(); };
        }
        void UpdateColors() { foreach (var b in swatches) { bool selected = (string)b.Tag == Group.Color; b.BorderBrush = selected ? Ui.Color(Group.Color) : Ui.Brush("#E1E5ED"); b.Background = selected ? Ui.Pale(Group.Color) : Brushes.White; } }
        void RenderItems()
        {
            list.Children.Clear(); itemCount.Text = Group.Items.Count == 0 ? "尚未添加应用" : Group.Items.Count + " 个应用 · 从上到下依次启动";
            foreach (var i in Group.Items)
            {
                var item = i; int n = Group.Items.IndexOf(item);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 7), Height = 48, Background = Brushes.White };
                var image = Ui.AppIcon(item, 28); image.HorizontalAlignment = HorizontalAlignment.Left; image.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(image);
                var title = Ui.Text(item.Name, 12, Ui.Ink); title.Margin = new Thickness(48, 0, 130, 0); title.ToolTip = item.Target; row.Children.Add(title);
                var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
                var up = Ui.Quiet("↑", delegate { Move(item, -1); }); up.IsEnabled = n > 0; up.ToolTip = "向上移动"; tools.Children.Add(up);
                var down = Ui.Quiet("↓", delegate { Move(item, 1); }); down.IsEnabled = n < Group.Items.Count - 1; down.ToolTip = "向下移动"; tools.Children.Add(down);
                var edit = Ui.Quiet("编辑", delegate { var d = new ItemDialog(this, item, config); if (d.ShowDialog() == true) RenderItems(); }); edit.Padding = new Thickness(5); edit.ToolTip = "编辑此应用"; tools.Children.Add(edit);
                var remove = Ui.Quiet("×", delegate { Group.Items.Remove(item); RenderItems(); }); remove.ToolTip = "从分组移除，保存后生效"; tools.Children.Add(remove); row.Children.Add(tools);
                list.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderBrush = Ui.Brush("#E9ECF2"), BorderThickness = new Thickness(1), Child = row, Margin = new Thickness(0, 0, 0, 5) });
            }
        }
        void Move(LaunchItem item, int direction)
        {
            int n = Group.Items.IndexOf(item), next = n + direction; if (next < 0 || next >= Group.Items.Count) return;
            Group.Items.RemoveAt(n); Group.Items.Insert(next, item); RenderItems();
        }
        void Add()
        {
            if (Group.Items.Count >= 200) { Feedback.Text = "每组最多 200 个应用。"; return; }
            var dialog = new ItemDialog(this, new LaunchItem(), config); if (dialog.ShowDialog() == true) { Group.Items.Add(dialog.Item); RenderItems(); }
        }
        void Save()
        {
            if (String.IsNullOrWhiteSpace(name.Text)) { Feedback.Text = "请填写分组名称。"; return; }
            Group.Name = name.Text.Trim();
            try { ConfigCodec.Validate(new Configuration { Groups = { Group } }); }
            catch (Exception e) { Feedback.Text = e.Message; return; }
            DialogResult = true;
        }
    }
    public sealed class SettingsDialog : Sheet
    {
        readonly Configuration initial;
        readonly ConfigStore store;
        readonly Func<Configuration, Task<bool>> commit;
        readonly CheckBox exit = new CheckBox { Content = "分组启动后，自动退出轻启" };
        readonly CheckBox startup = new CheckBox { Content = "登录 Windows 时打开轻启" };
        readonly StartupRegistration startupRegistration;
        readonly TextBlock startupStatus = Ui.Text("", 11, Ui.Muted);
        bool initialStartup;
        bool startupAvailable = true;
        readonly ComboBox delay = new ComboBox { Height = 36, Width = 210, HorizontalAlignment = HorizontalAlignment.Left };
        readonly int[] delays = { 0, 150, 300, 500, 1000, 2000 };
        public SettingsDialog(Window owner, Configuration config, ConfigStore configStore, Func<Configuration, Task<bool>> save, StartupRegistration registration = null) : base(owner, "让轻启，更合你的习惯", "简单设置，安静运行。", 650)
        {
            initial = ConfigCodec.Clone(config); store = configStore; commit = save; Label("启动习惯"); exit.IsChecked = config.Settings.ExitAfterLaunch; Body.Children.Add(exit);
            Help("全部启动请求正常发出后退出；遇到启动错误时保留窗口。单个应用的测试与启动不会触发退出。");
            startupRegistration = registration ?? new StartupRegistration();
            try { var state = startupRegistration.Read(); initialStartup = state.Registered; startup.IsChecked = initialStartup; startupStatus.Text = state.Description; }
            catch (Exception e) { startupAvailable = false; startup.IsEnabled = false; Feedback.Text = "无法读取开机设置：" + e.Message; }
            startup.Checked += delegate { startupStatus.Text = "点击“保存设置”登记当前程序位置；若 Windows 已禁用，请到系统设置启用。"; };
            startup.Unchecked += delegate { startupStatus.Text = "点击“保存设置”关闭开机启动。"; };
            Body.Children.Add(startup); startupStatus.TextWrapping = TextWrapping.Wrap; startupStatus.Margin = new Thickness(0, 7, 0, 0); Body.Children.Add(startupStatus);
            var systemStartup = Ui.Quiet("打开 Windows 启动应用设置  ↗", delegate { try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); } catch (Exception e) { Feedback.Text = "无法打开系统设置：" + e.Message + "。可在任务管理器的“启动应用”中查看。"; } });
            systemStartup.HorizontalAlignment = HorizontalAlignment.Left; systemStartup.Margin = new Thickness(-8, 3, 0, 0); Body.Children.Add(systemStartup);
            Help("登录后只打开轻启，由你选择分组。开启时仅登记当前用户的一条启动项。"); Label("应用之间的启动间隔");
            foreach (int ms in delays) delay.Items.Add(ms == 0 ? "立即连续启动" : ms + " 毫秒" + (ms == 300 ? " · 推荐" : ""));
            int index = Array.IndexOf(delays, config.Settings.DelayMs);
            if (index < 0) { delay.Items.Add(config.Settings.DelayMs + " 毫秒 · 当前设置"); delay.SelectedIndex = delays.Length; } else delay.SelectedIndex = index;
            Body.Children.Add(delay); Label("配置备份");
            var backups = new StackPanel { Orientation = Orientation.Horizontal }; var export = Ui.Button("导出配置…", delegate { Perform(Export); }, false); export.Margin = new Thickness(0, 0, 9, 0); backups.Children.Add(export); backups.Children.Add(Ui.Button("导入配置…", delegate { Perform(Import); }, false)); Body.Children.Add(backups);
            Help("导入会替换当前分组与启动习惯，原配置会保留为本地备份。导入本身不会启动应用。");
            var folder = Ui.Quiet("打开配置文件夹  ↗", OpenFolder); folder.HorizontalAlignment = HorizontalAlignment.Left; folder.Margin = new Thickness(-8, 12, 0, 0); Body.Children.Add(folder);
            var about = Ui.Text("轻启  1.2.2  ·  本地保存，无需账户", 11, Ui.Muted); about.Margin = new Thickness(0, 25, 0, 0); Body.Children.Add(about); Buttons(delegate { Perform(Save); }, "保存设置");
        }
        async Task Save()
        {
            StartupRegistration.State previous = null;
            bool changed = false, committed = false;
            try
            {
                var next = ConfigCodec.Clone(initial); next.Settings.ExitAfterLaunch = exit.IsChecked == true;
                next.Settings.DelayMs = delay.SelectedIndex >= 0 && delay.SelectedIndex < delays.Length ? delays[delay.SelectedIndex] : initial.Settings.DelayMs;
                if (startupAvailable)
                {
                    previous = startupRegistration.Read();
                    bool enabled = startup.IsChecked == true;
                    if (enabled != initialStartup || (enabled && !previous.CurrentPath))
                    { changed = true; startupRegistration.Set(enabled); }
                }
                if (!await commit(next))
                {
                    if (changed) { startupRegistration.Restore(previous); changed = false; }
                    Feedback.Text = "设置未保存，开机启动项已恢复。"; return;
                }
                committed = true; changed = false;
                if (startupAvailable)
                {
                    initialStartup = startup.IsChecked == true;
                    var state = startupRegistration.Read(); startupStatus.Text = state.Description;
                    if (state.Registered && state.Disabled)
                    { Feedback.Text = "设置已保存，但 Windows 已禁用开机启动。请打开系统设置启用后再查看。"; return; }
                }
                DialogResult = true;
            }
            catch (Exception e)
            {
                string recovery = "";
                if (changed) { try { startupRegistration.Restore(previous); } catch (Exception restoreError) { recovery = "\n启动项恢复失败：" + restoreError.Message; } }
                Feedback.Text = (committed ? "设置已保存，启动状态读取失败：" : "设置未保存：") + e.Message + recovery;
            }
        }
        async Task Export()
        {
            var dialog = new SaveFileDialog { Title = "导出轻启配置", Filter = "轻启配置 (*.json)|*.json", FileName = "轻启-配置-" + DateTime.Now.ToString("yyyyMMdd") + ".json", DefaultExt = ".json", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (String.Equals(Path.GetFullPath(dialog.FileName), store.FilePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请选择配置文件夹以外的位置保存备份。");
                await Task.Run(delegate { File.WriteAllText(dialog.FileName, ConfigCodec.Encode(initial), new System.Text.UTF8Encoding(false)); }); Feedback.Foreground = Ui.Color("green"); Feedback.Text = "配置已导出。";
            }
            catch (Exception e) { Feedback.Text = "导出失败：" + e.Message; }
        }
        async Task Import()
        {
            var dialog = new OpenFileDialog { Title = "导入轻启配置", Filter = "轻启配置 (*.json)|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var imported = await Task.Run(delegate { return ConfigStore.Read(dialog.FileName); }); int total = imported.Groups.Sum(g => g.Items.Count);
                int invalid = await Task.Run(delegate { return imported.Groups.SelectMany(g => g.Items).Count(i => Targets.Error(i) != null); });
                string message = "将用 " + imported.Groups.Count + " 个分组、" + total + " 个应用替换当前配置。\n原配置会保留为本地备份。";
                if (invalid > 0) message += "\n其中 " + invalid + " 个项目的位置需要检查，导入后可修改。";
                message += "\n\n只导入你信任的配置。是否继续？";
                if (!Ui.Confirm(this, "替换现有配置？", message, "替换配置", true)) return;
                if (await commit(imported)) DialogResult = true;
            }
            catch (Exception e) { Feedback.Text = "导入失败，当前配置未改变：" + e.Message; }
        }
        void OpenFolder()
        {
            try { Directory.CreateDirectory(store.Folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.Folder) { UseShellExecute = true }); }
            catch (Exception e) { Feedback.Text = e.Message; }
        }
    }
    sealed class DialogHandle : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public DialogHandle(Window owner) { Handle = new WindowInteropHelper(owner).Handle; }
    }
}
