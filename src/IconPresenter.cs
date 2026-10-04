using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitLauncher
{
    internal sealed class IconPresenter : Border
    {
        static readonly HashSet<IconPresenter> visible = new HashSet<IconPresenter>();
        static readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly LaunchItem item;
        DateTime next;
        int generation, attempts;
        bool loading, pending;
        internal event Action<AssetInfo> Resolved;
        static IconPresenter()
        {
            timer.Tick += delegate
            {
                foreach (var view in visible.ToArray())
                    if (view.IsVisible && !view.loading && DateTime.UtcNow >= view.next) view.RefreshInBackground();
            };
        }
        public IconPresenter(LaunchItem source, double size)
        {
            item = source; Width = Height = size; CornerRadius = new CornerRadius(7); Background = Ui.Brush("#F0F2F6");
            Child = Ui.Glyph(Targets.IsWeb(Targets.Clean(item.Target)) ? "\uE774" : "\uE71D", size * .55, Ui.Brush("#8892A5"));
            ((TextBlock)Child).HorizontalAlignment = HorizontalAlignment.Center;
            Loaded += async delegate { generation++; attempts = 0; pending = loading; visible.Add(this); timer.Start(); await Refresh(false); };
            Unloaded += delegate { generation++; visible.Remove(this); if (visible.Count == 0) timer.Stop(); };
            IsVisibleChanged += async delegate { if (IsVisible && IsLoaded) await Refresh(false); };
        }
        async void RefreshInBackground() { await Refresh(false); }
        internal async Task Refresh(bool force)
        {
            if (force) attempts = 0;
            if (loading) { pending |= force; return; }
            if (!IsLoaded) return;
            loading = true; int version = generation;
            try
            {
                var asset = await Assets.Get(item, force);
                if (IsLoaded && version == generation && !pending)
                {
                    if (asset.Icon != null)
                    {
                        var image = Child as Image;
                        if (image == null || image.Source != asset.Icon) { Background = Brushes.Transparent; Child = new Image { Source = asset.Icon, Stretch = Stretch.Uniform }; }
                    }
                    else if (Child is TextBlock && asset.Folder) ((TextBlock)Child).Text = "\uE8B7";
                    attempts = Math.Min(4, attempts + 1);
                    next = DateTime.UtcNow.AddSeconds(asset.NeedsRetry && attempts < 4 ? 2 : 30);
                    if (Resolved != null) Resolved(asset);
                }
            }
            catch { next = DateTime.UtcNow.AddSeconds(30); }
            finally { loading = false; }
            if (pending && IsLoaded) { pending = false; await Refresh(true); }
        }
    }
}
