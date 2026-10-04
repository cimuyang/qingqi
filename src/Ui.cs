using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Input;

namespace OrbitLauncher
{
    public static class Ui
    {
        public static readonly Brush Ink = Brush("#273141"), Muted = Brush("#727E91"), Blue = Brush("#526EDB");
        static readonly Dictionary<string, DropShadowEffect> shadows = new Dictionary<string, DropShadowEffect>();
        public static Brush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        public static Brush Surface() { return Gradient("#FFFFFF", "#F9FAFD"); }
        public static Brush Gradient(string top, string bottom)
        {
            var b = new LinearGradientBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(top), (System.Windows.Media.Color)ColorConverter.ConvertFromString(bottom), 90); b.Freeze(); return b;
        }
        public static DropShadowEffect Shadow(double opacity, double blur, double depth)
        {
            string key = opacity + ":" + blur + ":" + depth;
            DropShadowEffect effect;
            if (!shadows.TryGetValue(key, out effect))
            {
                effect = new DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(74, 91, 126), Opacity = opacity, BlurRadius = blur, ShadowDepth = depth, Direction = 270 };
                effect.Freeze(); shadows[key] = effect;
            }
            return effect;
        }
        public static void Fade(FrameworkElement element)
        {
            if (SystemParameters.ClientAreaAnimation) element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
        }
        public static TextBlock Glyph(string icon, double size, Brush color)
        {
            var t = Text(icon, size, color); t.FontFamily = new FontFamily("Segoe MDL2 Assets"); return t;
        }
        public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
            {
                var child = VisualTreeHelper.GetChild(root, n); if (child is T) yield return (T)child;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        public static TextBlock Text(string s, double size, Brush brush)
        {
            return new TextBlock { Text = s, FontSize = size, Foreground = brush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        }
        public static Button Button(string text, Action action, bool primary)
        {
            var b = new Button { Content = text };
            if (primary) b.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
            var scale = new ScaleTransform(1, 1); b.RenderTransform = scale; b.RenderTransformOrigin = new Point(.5, .5);
            Action<bool> press = delegate(bool down)
            {
                double value = down ? .985 : 1;
                foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                {
                    if (SystemParameters.ClientAreaAnimation) scale.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(down ? 75 : 130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
                    else { scale.BeginAnimation(property, null); scale.SetValue(property, value); }
                }
            };
            b.PreviewMouseLeftButtonDown += delegate { if (b.IsEnabled) press(true); };
            b.PreviewMouseLeftButtonUp += delegate { press(false); };
            b.MouseLeave += delegate { press(false); }; b.LostMouseCapture += delegate { press(false); };
            b.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space || e.Key == Key.Enter) press(true); };
            b.PreviewKeyUp += delegate { press(false); }; b.LostKeyboardFocus += delegate { press(false); };
            b.IsEnabledChanged += delegate { if (!b.IsEnabled) press(false); };
            b.Click += delegate { action(); }; return b;
        }
        public static Button Quiet(string text, Action action)
        {
            var b = Button(text, action, false); b.SetResourceReference(FrameworkElement.StyleProperty, "Quiet"); return b;
        }
        public static Brush Color(string name)
        {
            switch (name) { case "green": return Brush("#398A70"); case "purple": return Brush("#8868BD"); case "orange": return Brush("#C89148"); case "pink": return Brush("#BE7195"); case "slate": return Brush("#738294"); default: return Blue; }
        }
        public static Brush Pale(string name)
        {
            switch (name) { case "green": return Brush("#EDF6F1"); case "purple": return Brush("#F3EFF9"); case "orange": return Brush("#FBF3E8"); case "pink": return Brush("#FAEFF5"); case "slate": return Brush("#F0F3F6"); default: return Brush("#EDF1FD"); }
        }
        public static FrameworkElement GroupMark(string color, double size)
        {
            string symbol = color == "green" ? "\uE8F2" : color == "purple" ? "\uE708" : color == "orange" ? "\uE8A5" : color == "pink" ? "\uEB51" : color == "slate" ? "\uE8B7" : "\uE821";
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * .30), BorderThickness = new Thickness(1), BorderBrush = Brush("#E8EDF6"), Background = Gradient("#FFFFFF", ((SolidColorBrush)Pale(color)).Color.ToString()), Child = new TextBlock { Text = symbol, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size * .44, Foreground = Color(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        }
        public static ImageSource Logo()
        {
            var drawing = new DrawingGroup();
            using (var d = drawing.Open())
            {
                d.DrawRoundedRectangle(Brush("#F9FAFC"), new Pen(Brush("#A2A9B5"), 1.8), new Rect(3, 3, 58, 58), 15, 15);
                var pen = new Pen(Brush("#818A9B"), 1.1); pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                d.DrawEllipse(null, pen, new Point(32, 31), 22, 22);
                for (int n = 0; n < 24; n++)
                {
                    double a = n * Math.PI / 12;
                    d.DrawLine(pen, new Point(32 + Math.Cos(a) * 5, 31 + Math.Sin(a) * 5), new Point(32 + Math.Cos(a + .25) * 17, 31 + Math.Sin(a + .25) * 17));
                }
                var orbit = Geometry.Parse("M 31,9 C 2,14 14,56 40,50 M 11,35 C 21,64 61,44 52,20 M 49,15 C 21,0 5,39 19,47");
                d.DrawGeometry(null, pen, orbit);
                d.DrawEllipse(Brush("#818A9B"), null, new Point(32, 31), 2, 2);
                d.DrawGeometry(Brush("#F9FAFC"), pen, Geometry.Parse("M 46,45 L 46,54 L 54,49.5 Z"));
            }
            drawing.Freeze(); return new DrawingImage(drawing);
        }
        public static FrameworkElement AppIcon(LaunchItem item, double size)
        {
            string path = Targets.Clean(item.Target);
            string symbol = Targets.IsWeb(path) ? "\uE774" : "\uE71D";
            var holder = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(7), Background = Brush("#F0F2F6"), Child = new TextBlock { Text = symbol, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size * .55, Foreground = Brush("#8892A5"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            holder.Loaded += async delegate
            {
                try
                {
                    var asset = await Assets.Get(item); if (!holder.IsLoaded) return;
                    if (asset.Icon != null) { holder.Background = Brushes.Transparent; holder.Child = new Image { Source = asset.Icon, Stretch = Stretch.Uniform }; }
                    else if (asset.Folder && holder.Child is TextBlock) ((TextBlock)holder.Child).Text = "\uE8B7";
                }
                catch { }
            };
            return holder;
        }
        public static void ValidateRow(FrameworkElement row, TextBlock name, LaunchItem item)
        {
            row.Loaded += async delegate
            {
                try
                {
                    var asset = await Assets.Get(item); if (!row.IsLoaded) return;
                    name.Foreground = asset.Error == null ? Ink : Brush("#B5604B");
                    row.ToolTip = item.Target + (asset.Deferred ? "\n网络位置将在启动时检查。" : asset.Error == null ? "" : "\n" + asset.Error);
                }
                catch { }
            };
        }
        public static void Snapshot(Window window, string path)
        {
            foreach (var element in Descendants<UIElement>(window)) element.BeginAnimation(UIElement.OpacityProperty, null);
            window.UpdateLayout();
            var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        public static void Error(Window owner, string message) { new NoticeDialog(owner, "暂时无法完成", message, "知道了", false, false).ShowDialog(); }
        public static void Info(Window owner, string title, string message) { new NoticeDialog(owner, title, message, "知道了", false, false).ShowDialog(); }
        public static bool Confirm(Window owner, string title, string message, string action, bool danger) { return new NoticeDialog(owner, title, message, action, true, danger).ShowDialog() == true; }
        public static void SnapshotVisual(FrameworkElement element, string path)
        {
            element.UpdateLayout(); var target = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); target.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
