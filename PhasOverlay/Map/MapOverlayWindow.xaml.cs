using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace PhasOverlay
{
    /// <summary>
    /// The selected map's floor over the whole display while the hold key is down. Click-through
    /// and never activated, so the game keeps focus and input.
    /// </summary>
    public partial class MapOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const double HoverRadius = 26;
        private const double HoverScale = 1.18;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly Brush AccentBrush = Frozen("#FFB455FF");
        private static readonly Brush FloorOnText = Frozen("#FF151515");
        private static readonly Brush FloorOffText = Frozen("#FF9A9AA2");
        private static readonly Brush DotStroke = Frozen("#FF111112");
        private static readonly Brush PowerFill = Frozen("#F20E2A18");
        private static readonly Brush PowerGreen = Frozen("#FF4ADE80");
        private static readonly Geometry Bolt = FrozenGeometry("M 6,0 L 0,7.6 L 3.6,7.6 L 2.4,14 L 9,6.2 L 5,6.2 Z");

        // One floor's photos only, since each decoded photo is several megabytes.
        private readonly Dictionary<string, BitmapImage> _shots = new();
        private FloorDto? _shotsFloor;

        private MapDto? _map;
        private MapVersionDto? _version;
        private FloorDto? _floor;

        private BitmapImage? _plan;
        private string _planPath = "";
        private DateTime _planStamp;

        private MarkerDto? _hovered;

        private const int HideAfterFrames = 3;
        private int _hideGeneration;
        private bool _hidePending;

        public MapOverlayWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private List<FloorDto> Floors => _version?.Floors ?? _map?.Floors ?? new List<FloorDto>();

        /// <summary>Shows the saved selection on the given display. False when no map is selected.</summary>
        public bool ShowSelection(int displayIndex)
        {
            var current = MapSelection.Current();
            if (current == null) return false;

            _hideGeneration++;
            _hidePending = false;

            (_map, _version, _floor) = current.Value;
            Render();
            Root.Opacity = 1;

            var bounds = DisplayService.BoundsFor(displayIndex);
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            if (!IsVisible) Show();
            return true;
        }

        /// <summary>
        /// A hidden layered window re-presents its last frame on the next Show, which would flash the
        /// previous map, so a transparent frame is presented before hiding.
        /// </summary>
        public void HideOverlay()
        {
            ClearHover();
            if (!IsVisible || _hidePending) return;

            int generation = ++_hideGeneration;
            _hidePending = true;
            Root.Opacity = 0;

            int frames = 0;
            EventHandler? onRendering = null;
            onRendering = (s, e) =>
            {
                if (generation != _hideGeneration || !_hidePending)
                {
                    CompositionTarget.Rendering -= onRendering;
                    return;
                }

                if (++frames < HideAfterFrames) return;

                CompositionTarget.Rendering -= onRendering;
                _hidePending = false;
                Hide();
            };
            CompositionTarget.Rendering += onRendering;
        }

        /// <summary>Steps through the current version's floors while the map is up.</summary>
        public void CycleFloor(int delta)
        {
            var floors = Floors;
            if (_map == null || floors.Count < 2) return;

            int i = _floor == null ? -1 : floors.IndexOf(_floor);
            _floor = floors[((i + delta) % floors.Count + floors.Count) % floors.Count];
            Save();
            Render();
        }

        /// <summary>Steps through a restricted map's versions, landing on the new version's Ground.</summary>
        public void CycleVersion(int delta)
        {
            if (_map == null || _map.Versions.Count < 2) return;

            int i = _version == null ? -1 : _map.Versions.IndexOf(_version);
            _version = _map.Versions[((i + delta) % _map.Versions.Count + _map.Versions.Count) % _map.Versions.Count];
            _floor = MapSelection.DefaultFloor(_version.Floors);
            Save();
            Render();
        }

        private void Save()
        {
            if (_map != null) MapSelection.Save(_map.Name, _version?.Name ?? "", _floor?.Name ?? "");
        }

        private void Render()
        {
            if (_map == null) return;

            MapNameText.Text = _map.Name;

            VariationText.Text = _version != null && _map.Versions.Count > 1 ? _version.Name : "";
            VariationTag.Visibility = VariationText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            FloorRow.Children.Clear();
            foreach (var f in Floors)
            {
                bool current = ReferenceEquals(f, _floor);
                FloorRow.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 5, 14, 5),
                    Background = current ? AccentBrush : Brushes.Transparent,
                    Child = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(f.Name) ? "Floor" : f.Name,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = current ? FloorOnText : FloorOffText
                    }
                });
            }
            FloorTrack.Visibility = FloorRow.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            LoadPlan(_floor == null ? "" : MapDataService.Resolve(_floor.Plan));

            PlanImage.Source = _plan;
            NoPlanText.Visibility = _plan == null ? Visibility.Visible : Visibility.Collapsed;

            if (!ReferenceEquals(_floor, _shotsFloor))
            {
                _shots.Clear();
                _shotsFloor = _floor;
            }

            ClearHover();
            BuildMarkers();
        }

        // Keyed on the write time so a Map Designer re-export is picked up without a restart.
        private void LoadPlan(string path)
        {
            DateTime stamp = path.Length > 0 && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (path == _planPath && stamp == _planStamp) return;

            _planPath = path;
            _planStamp = stamp;
            _plan = stamp == DateTime.MinValue ? null : MapDataService.LoadImage(path);
        }

        private void BuildMarkers()
        {
            MarkerLayer.Children.Clear();
            if (_floor == null || _plan == null) return;

            foreach (var m in _floor.Markers)
            {
                MarkerLayer.Children.Add(IsPowerBox(m) ? PowerBoxBadge(m) : CursedMarker(m));
            }

            PositionMarkers();
        }

        private static bool IsPowerBox(MarkerDto m) => string.Equals(m.Kind, "powerbox", StringComparison.OrdinalIgnoreCase);

        private static FrameworkElement CursedMarker(MarkerDto m)
        {
            var g = new Grid { Width = 40, Height = 40, Tag = m };

            if (PossessionIcons.For(m.Item, PossessionIcons.MapGlyph) is { } icon)
            {
                double extent = PossessionIcons.Extent(PossessionIcons.MapGlyph);
                var image = new Image
                {
                    Source = icon,
                    Width = extent,
                    Height = extent,
                    Stretch = Stretch.Fill,
                    Margin = PossessionIcons.Overhang(PossessionIcons.MapGlyph, g.Width),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1, 1)
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                g.Children.Add(image);
                return g;
            }

            g.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = AccentBrush, Stroke = DotStroke, StrokeThickness = 1.5 });
            return g;
        }

        private static FrameworkElement PowerBoxBadge(MarkerDto m)
        {
            var g = new Grid { Width = 30, Height = 30, Tag = m };
            g.Children.Add(new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(4),
                Background = PowerFill, BorderBrush = PowerGreen, BorderThickness = new Thickness(2)
            });
            g.Children.Add(new ShapePath { Data = Bolt, Fill = PowerGreen, Stretch = Stretch.Uniform, Width = 8, Height = 13 });
            return g;
        }

        private void PositionMarkers()
        {
            if (_plan == null) return;

            double hw = PlanHost.ActualWidth, hh = PlanHost.ActualHeight;
            if (hw <= 0 || hh <= 0) return;

            double scale = Math.Min(hw / _plan.PixelWidth, hh / _plan.PixelHeight);
            double w = _plan.PixelWidth * scale, h = _plan.PixelHeight * scale;
            double left = (hw - w) / 2, top = (hh - h) / 2;

            foreach (var child in MarkerLayer.Children)
            {
                if (child is not FrameworkElement fe || fe.Tag is not MarkerDto m) continue;
                Canvas.SetLeft(fe, left + m.X * w - fe.Width / 2);
                Canvas.SetTop(fe, top + m.Y * h - fe.Height / 2);
            }
        }

        /// <summary>Fed from the overlay's cursor poll, in screen pixels, since a click-through window gets no mouse events.</summary>
        public void UpdateHover(int screenX, int screenY)
        {
            if (!IsVisible || _plan == null) { ClearHover(); return; }

            Point local;
            try { local = PointFromScreen(new Point(screenX, screenY)); }
            catch { return; }

            MarkerDto? hit = null;
            double best = HoverRadius;

            foreach (var child in MarkerLayer.Children)
            {
                if (child is not FrameworkElement fe || fe.Tag is not MarkerDto m || IsPowerBox(m)) continue;

                var centre = fe.TransformToAncestor(this)
                               .Transform(new Point(fe.Width / 2, fe.Height / 2));
                double d = Math.Sqrt((centre.X - local.X) * (centre.X - local.X)
                                     + (centre.Y - local.Y) * (centre.Y - local.Y));
                if (d < best) { best = d; hit = m; }
            }

            if (ReferenceEquals(hit, _hovered)) return;
            _hovered = hit;
            RefreshHoverScale();

            if (hit == null || string.IsNullOrWhiteSpace(hit.Image)) { HideShot(); return; }
            ShowShot(hit, local);
        }

        private void RefreshHoverScale()
        {
            foreach (var child in MarkerLayer.Children)
            {
                if (child is not Grid g || g.Tag is not MarkerDto m) continue;
                if (g.Children.Count > 0 && g.Children[0] is Image { RenderTransform: ScaleTransform scale })
                    scale.ScaleX = scale.ScaleY = ReferenceEquals(m, _hovered) ? HoverScale : 1;
            }
        }

        private void ShowShot(MarkerDto m, Point near)
        {
            if (!_shots.TryGetValue(m.ImageFullPath, out var shot))
            {
                var loaded = MapDataService.LoadImage(m.ImageFullPath);
                if (loaded == null) { HideShot(); return; }
                _shots[m.ImageFullPath] = shot = loaded;
            }

            ShotImage.Source = shot;
            ShotTitle.Text = m.Item;
            ShotRoom.Text = m.Room;
            ShotRoom.Visibility = string.IsNullOrWhiteSpace(m.Room) ? Visibility.Collapsed : Visibility.Visible;
            ShotPopover.Visibility = Visibility.Visible;

            ShotPopover.UpdateLayout();
            var host = ShotLayer.PointFromScreen(PointToScreen(near));
            double w = ShotPopover.ActualWidth, h = ShotPopover.ActualHeight;

            double x = Math.Clamp(host.X + 22, 0, Math.Max(0, PlanHost.ActualWidth - w));
            double y = Math.Clamp(host.Y + 22, 0, Math.Max(0, PlanHost.ActualHeight - h));

            Canvas.SetLeft(ShotPopover, x);
            Canvas.SetTop(ShotPopover, y);
        }

        private void HideShot()
        {
            ShotPopover.Visibility = Visibility.Collapsed;
            ShotImage.Source = null;
        }

        private void ClearHover()
        {
            HideShot();
            _hovered = null;
        }

        private void PlanHost_SizeChanged(object sender, SizeChangedEventArgs e) => PositionMarkers();

        private static Brush Frozen(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }

        private static Geometry FrozenGeometry(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }
    }
}
