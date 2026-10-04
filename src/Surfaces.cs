using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace OrbitLauncher
{
    // The same chrome and modal layering are shared by all app-owned windows.
    public class SurfaceWindow : Window
    {
        readonly Grid layer = new Grid();
        readonly Border scrim = new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 28, 34, 47)), Visibility = Visibility.Collapsed, IsHitTestVisible = false, CornerRadius = new CornerRadius(16) };
        int modalDepth;
        SurfaceWindow dimmedOwner;
        public SurfaceWindow()
        {
            Style = (Style)Application.Current.FindResource(typeof(Window));
            WindowStyle = WindowStyle.None; Icon = Ui.Logo();
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 40, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(16), GlassFrameThickness = new Thickness(1), UseAeroCaptionButtons = false });
            Content = layer;
            SourceInitialized += delegate { NativeDepth.Apply(new WindowInteropHelper(this).Handle); };
            Loaded += delegate
            {
                dimmedOwner = Owner as SurfaceWindow;
                if (dimmedOwner != null) dimmedOwner.Dim(true);
            };
            Closed += delegate { if (dimmedOwner != null) { dimmedOwner.Dim(false); dimmedOwner.OnModalClosed(); dimmedOwner = null; } };
        }
        protected virtual void OnModalClosed() { }
        protected void SetSurface(FrameworkElement element) { layer.Children.Clear(); layer.Children.Add(element); layer.Children.Add(scrim); }
        void Dim(bool visible)
        {
            modalDepth = Math.Max(0, modalDepth + (visible ? 1 : -1));
            scrim.Visibility = modalDepth > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (modalDepth > 0 && SystemParameters.ClientAreaAnimation) scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
        }
        protected Grid Caption(bool allowMinimize, string label)
        {
            var row = new Grid { Height = 40, Margin = new Thickness(20, 0, 8, 0) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(new Image { Source = Ui.Logo(), Width = 22, Height = 22, Margin = new Thickness(0, 0, 9, 0) });
            brand.Children.Add(Ui.Text(label, 12, Ui.Muted)); row.Children.Add(brand);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (allowMinimize)
            {
                controls.Children.Add(CaptionButton("\uE921", "最小化", delegate { WindowState = WindowState.Minimized; }));
                controls.Children.Add(CaptionButton("\uE922", "最大化 / 还原", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            }
            var close = CaptionButton("\uE8BB", "关闭", Close); controls.Children.Add(close); row.Children.Add(controls); return row;
        }
        Button CaptionButton(string icon, string tip, Action action)
        {
            var b = Ui.Quiet(icon, action); b.FontFamily = new FontFamily("Segoe MDL2 Assets"); b.FontSize = 11; b.Width = 38; b.Height = 29; b.Padding = new Thickness(0); b.ToolTip = tip;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true); return b;
        }
    }
    static class NativeDepth
    {
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle, int command);
        public static void Reveal(Window window) { ShowWindow(new WindowInteropHelper(window).Handle, 5); }
        public static void Apply(IntPtr handle)
        {
            try
            {
                int policy = 2; DwmSetWindowAttribute(handle, 2, ref policy, 4);
                int corner = 2; DwmSetWindowAttribute(handle, 33, ref corner, 4);
                var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 }; DwmExtendFrameIntoClientArea(handle, ref margins);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
    public sealed class NoticeDialog : SurfaceWindow
    {
        public NoticeDialog(Window owner, string title, string message, string action, bool confirm, bool danger)
        {
            Owner = owner; Title = title + " · 轻启"; Width = 466; MinWidth = 380; MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 80); SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner; ShowInTaskbar = owner == null;
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(Caption(false, "轻启"));
            var content = new StackPanel { Margin = new Thickness(30, 18, 30, 28) };
            var mark = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(13), Background = danger ? Ui.Brush("#FCEDEE") : Ui.Brush("#EEF2FC"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 17) };
            mark.Child = new TextBlock { Text = danger ? "\uE74D" : "\uE946", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 19, Foreground = danger ? Ui.Brush("#BA4E59") : Ui.Blue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; content.Children.Add(mark);
            var heading = Ui.Text(title, 21, Ui.Ink); heading.FontWeight = FontWeights.SemiBold; heading.TextWrapping = TextWrapping.Wrap; heading.TextTrimming = TextTrimming.None; content.Children.Add(heading);
            var body = Ui.Text(message, 13, Ui.Muted); body.TextWrapping = TextWrapping.Wrap; body.TextTrimming = TextTrimming.None; body.LineHeight = 23;
            content.Children.Add(new ScrollViewer { Content = body, MaxHeight = Math.Max(100, SystemParameters.WorkArea.Height - 360), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 24) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (confirm)
            {
                var cancel = Ui.Button("取消", delegate { DialogResult = false; }, false); cancel.MinWidth = 82; cancel.Margin = new Thickness(0, 0, 10, 0); cancel.IsCancel = true; cancel.IsDefault = true; buttons.Children.Add(cancel);
                Loaded += delegate { cancel.Focus(); };
            }
            var accept = Ui.Button(action, delegate { DialogResult = true; }, true); accept.MinWidth = 98; if (danger) accept.SetResourceReference(StyleProperty, "Danger"); accept.IsDefault = !confirm; buttons.Children.Add(accept); content.Children.Add(buttons);
            Grid.SetRow(content, 1); grid.Children.Add(content);
            SetSurface(new Border { CornerRadius = new CornerRadius(16), Background = Ui.Surface(), BorderBrush = Ui.Brush("#E0E4EC"), BorderThickness = new Thickness(1), Child = grid });
        }
    }
}
