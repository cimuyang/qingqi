using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OrbitLauncher
{
    // Keep the existing popup and row bounds, including near screen edges and at high DPI.
    sealed class MenuConfirmation : ContextMenu
    {
        Action reset;
        public MenuConfirmation() { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)); }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == IsOpenProperty && !(bool)e.NewValue && reset != null) reset();
        }
        public void AddConfirmation(string label, string confirm, string description, Action action)
        {
            var menu = this;
            var item = new MenuItem { Header = label, StaysOpenOnClick = true, Foreground = Ui.Brush("#B45561"), ToolTip = description };
            item.Icon = Ui.Glyph("\uE74D", 14, item.Foreground);
            var elapsed = new Stopwatch();
            double width = Double.NaN;
            reset = delegate { if (elapsed.IsRunning) menu.Width = width; elapsed.Reset(); item.Header = label; };
            menu.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { menu.IsOpen = false; e.Handled = true; } };
            item.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ClickCount > 1) e.Handled = true; };
            item.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.IsRepeat) e.Handled = true; };
            item.Click += delegate
            {
                if (!elapsed.IsRunning)
                {
                    width = menu.Width;
                    if (menu.ActualWidth > 0) menu.Width = menu.ActualWidth;
                    item.Header = new TextBlock { Text = confirm + " · Esc 取消", TextTrimming = TextTrimming.CharacterEllipsis };
                    elapsed.Start();
                    return;
                }
                if (elapsed.ElapsedMilliseconds <= System.Windows.Forms.SystemInformation.DoubleClickTime) return;
                menu.IsOpen = false;
                action();
            };
            menu.Items.Add(item);
        }
    }
}
