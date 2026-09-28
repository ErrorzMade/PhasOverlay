using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasOverlay
{
    public partial class MapWindow : Window
    {
        // Relative to the fitted view, which is also the furthest out zoom goes.
        private const double MaxZoomOverFit = 8.0;
        private const double ZoomStep = 1.25;

        private readonly MainWindow _main;

        private MapDto? _map;
        private MapVersionDto? _version;
        private FloorDto? _floor;
        private double _planWidth, _planHeight;
        private double _fitScale;

        private bool _needsFit;

        private const int TutorialPickMap = 0, TutorialPossession = 1, TutorialDone = 2;
        private int _tutorialStep = -1;
        private int _tutorialRun;
        private Action<bool>? _tutorialFinished;
        private bool _closed;
        private bool _dragging;
        private Point _dragStart;
        private double _panStartX, _panStartY;

        public MapWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            BuildPicker();
            BuildPossessionKey();
            RestoreSelection();
            ShowUpdateTag();

            MapDataUpdater.UpdateReady += OnMapUpdateReady;
            this.Closed += (s, e) =>
            {
                MapDataUpdater.UpdateReady -= OnMapUpdateReady;
                _closed = true;
                EndTutorial(false);
            };

            this.Loaded += (s, e) => DisplayService.CenterOn(this, _main.DisplayIndex);
        }

        private void OnMapUpdateReady() => Dispatcher.BeginInvoke(ShowUpdateTag);

        private void ShowUpdateTag()
            => PickerUpdateTag.Visibility = MapDataUpdater.UpdatePending ? Visibility.Visible : Visibility.Collapsed;

        private void BuildPossessionKey()
        {
            PossessionKey.Children.Clear();
            foreach (string item in PossessionIcons.Items)
            {
                if (PossessionIcons.For(item, PossessionIcons.KeyGlyph) is not { } icon) continue;

                double extent = PossessionIcons.Extent(PossessionIcons.KeyGlyph);
                var slot = new Grid { Width = PossessionIcons.KeyGlyph, Height = PossessionIcons.KeyGlyph, VerticalAlignment = VerticalAlignment.Center };
                var image = new Image { Source = icon, Width = extent, Height = extent, Stretch = Stretch.Fill, Margin = PossessionIcons.Overhang(PossessionIcons.KeyGlyph, PossessionIcons.KeyGlyph) };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                slot.Children.Add(image);

                PossessionKey.Children.Add(slot);
                PossessionKey.Children.Add(new TextBlock
                {
                    Text = item,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 14, 0)
                });
            }
        }

        private void BuildPicker()
        {
            var all = MapDataService.GetMaps();
            foreach (var m in all) m.Thumb = LoadThumb(m.OverviewFullPath, m.HasPlan);

            ListSmall.ItemsSource = MapDataService.BySize("small");
            ListMedium.ItemsSource = MapDataService.BySize("medium");
            ListLarge.ItemsSource = MapDataService.BySize("large");

            EmptyNotice.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static ImageSource? LoadThumb(string path, bool hasPlan)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = 320;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                if (hasPlan) return bmp;

                var grey = new FormatConvertedBitmap(bmp, PixelFormats.Gray32Float, null, 0);
                grey.Freeze();
                return grey;
            }
            catch { return null; }
        }

        private void MapCard_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not MapDto map) return;
            OpenMap(map, null, null);
            if (_tutorialStep == TutorialPickMap) ShowTutorialStep(TutorialPossession);
        }

        private void RestoreSelection()
        {
            if (MapSelection.Current() is { } saved) OpenMap(saved.Map, saved.Version, saved.Floor);
        }

        /// <summary>Catches up with a floor or variation changed on the held map, keeping zoom and pan if nothing did.</summary>
        public void SyncSelection()
        {
            if (_map == null || MapSelection.Current() is not { } saved) return;
            if (ReferenceEquals(saved.Map, _map) && ReferenceEquals(saved.Version, _version) && ReferenceEquals(saved.Floor, _floor)) return;
            OpenMap(saved.Map, saved.Version, saved.Floor);
        }

        private void OpenMap(MapDto map, MapVersionDto? version, FloorDto? floor)
        {
            _map = map;
            _version = null;
            DetailName.Text = map.Name;
            DetailName.ToolTip = map.Name;
            DetailSizeText.Text = map.Size.ToUpperInvariant();

            BuildVersionBar(map);

            if (map.Versions.Count > 0)
            {
                ShowVersion(version ?? map.Versions[0], floor);
            }
            else
            {
                BuildFloorBar(map.Floors);
                ShowFloor(floor ?? MapSelection.DefaultFloor(map.Floors));
            }

            PickerView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Visible;
        }

        private void BuildVersionBar(MapDto map)
        {
            VersionBar.Children.Clear();

            bool show = map.Versions.Count > 1;
            VersionSegment.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            VersionLabel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            foreach (var v in map.Versions)
            {
                var b = new Button
                {
                    Style = (Style)FindResource("SegmentButton"),
                    Content = string.IsNullOrWhiteSpace(v.Name) ? "Variation" : v.Name,
                    DataContext = v
                };
                b.Click += Version_Click;
                VersionBar.Children.Add(b);
            }
        }

        private void Version_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is MapVersionDto v) ShowVersion(v);
        }

        private void ShowVersion(MapVersionDto version, FloorDto? floor = null)
        {
            _version = version;

            foreach (var child in VersionBar.Children)
            {
                if (child is Button b) b.Tag = ReferenceEquals(b.DataContext, version) ? "on" : null;
            }

            BuildFloorBar(version.Floors);
            ShowFloor(floor ?? MapSelection.DefaultFloor(version.Floors));
        }

        private void BuildFloorBar(List<FloorDto> floors)
        {
            FloorBar.Children.Clear();

            bool show = floors.Count > 1;
            FloorSegment.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            FloorLabel.Visibility = show && VersionSegment.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!show) return;

            foreach (var f in floors)
            {
                var b = new Button
                {
                    Style = (Style)FindResource("SegmentButton"),
                    Content = string.IsNullOrWhiteSpace(f.Name) ? "Floor" : f.Name,
                    DataContext = f
                };
                b.Click += Floor_Click;
                FloorBar.Children.Add(b);
            }
        }

        private void Floor_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is FloorDto f) ShowFloor(f);
        }

        private void ShowFloor(FloorDto? floor)
        {
            _floor = floor;
            HideShot();

            if (_map != null) MapSelection.Save(_map.Name, _version?.Name ?? "", floor?.Name ?? "");

            foreach (var child in FloorBar.Children)
            {
                if (child is Button b) b.Tag = ReferenceEquals(b.DataContext, floor) ? "on" : null;
            }

            var plan = floor == null ? null : MapDataService.LoadImage(MapDataService.Resolve(floor.Plan));
            if (plan == null)
            {
                _planWidth = _planHeight = 0;
                PlanImage.Source = null;
                MarkerLayer.Children.Clear();
                PlanViewport.Visibility = Visibility.Collapsed;
                StatusStrip.Visibility = Visibility.Collapsed;
                NoPlanNotice.Visibility = Visibility.Visible;
                return;
            }

            _planWidth = plan.PixelWidth;
            _planHeight = plan.PixelHeight;

            PlanImage.Source = plan;
            PlanImage.Width = _planWidth;
            PlanImage.Height = _planHeight;
            PlanCanvas.Width = _planWidth;
            PlanCanvas.Height = _planHeight;

            BuildMarkers(floor!);

            NoPlanNotice.Visibility = Visibility.Collapsed;
            PlanViewport.Visibility = Visibility.Visible;
            StatusStrip.Visibility = Visibility.Visible;

            _needsFit = true;
            FitPlan();
        }

        private void BuildMarkers(FloorDto floor)
        {
            MarkerLayer.Children.Clear();

            foreach (var m in floor.Markers)
            {
                bool powerBox = string.Equals(m.Kind, "powerbox", StringComparison.OrdinalIgnoreCase);
                var icon = powerBox ? null : PossessionIcons.For(m.Item, PossessionIcons.MapGlyph);

                var b = new Button
                {
                    Style = (Style)FindResource(powerBox ? "PowerBoxMarker" : icon != null ? "PossessionMarker" : "MarkerButton"),
                    DataContext = m,
                    Tag = icon
                };
                AutomationProperties.SetName(b, m.Item);
                b.Click += Marker_Click;
                MarkerLayer.Children.Add(b);
            }

            PositionMarkers();
        }

        // Markers sit outside the zoom transform to keep a constant size, so they are placed by hand.
        private void PositionMarkers()
        {
            if (_floor == null || _planWidth <= 0) return;

            double scale = PlanScale.ScaleX;
            foreach (var child in MarkerLayer.Children)
            {
                if (child is not Button b || b.DataContext is not MarkerDto m) continue;
                Canvas.SetLeft(b, m.X * _planWidth * scale + PlanPan.X - b.Width / 2);
                Canvas.SetTop(b, m.Y * _planHeight * scale + PlanPan.Y - b.Height / 2);
            }
        }

        private void FitPlan()
        {
            if (_planWidth <= 0) return;

            double vw = PlanViewport.ActualWidth, vh = PlanViewport.ActualHeight;
            if (vw <= 0 || vh <= 0) return;

            _needsFit = false;
            _fitScale = Math.Min(vw / _planWidth, vh / _planHeight);

            PlanScale.ScaleX = PlanScale.ScaleY = _fitScale;
            ClampPan();
            PositionMarkers();
            UpdateZoomControls();
        }

        private void SetZoom(double scale, Point anchor)
        {
            if (_fitScale <= 0) return;

            scale = Math.Clamp(scale, _fitScale, _fitScale * MaxZoomOverFit);

            double old = PlanScale.ScaleX;
            if (old <= 0) old = scale;
            double planX = (anchor.X - PlanPan.X) / old;
            double planY = (anchor.Y - PlanPan.Y) / old;

            PlanScale.ScaleX = PlanScale.ScaleY = scale;
            PlanPan.X = anchor.X - planX * scale;
            PlanPan.Y = anchor.Y - planY * scale;

            ClampPan();
            PositionMarkers();
            UpdateZoomControls();
        }

        // Centres an axis the plan fits in, otherwise keeps its edges from being dragged inside the viewport.
        private void ClampPan()
        {
            double vw = PlanViewport.ActualWidth, vh = PlanViewport.ActualHeight;
            double sw = _planWidth * PlanScale.ScaleX, sh = _planHeight * PlanScale.ScaleY;

            PlanPan.X = sw <= vw + 0.5 ? (vw - sw) / 2 : Math.Clamp(PlanPan.X, vw - sw, 0);
            PlanPan.Y = sh <= vh + 0.5 ? (vh - sh) / 2 : Math.Clamp(PlanPan.Y, vh - sh, 0);
        }

        private void UpdateZoomControls()
        {
            if (_fitScale <= 0) return;

            double relative = PlanScale.ScaleX / _fitScale;
            ZoomText.Text = $"{Math.Round(relative * 100)}%";
            ZoomOutButton.IsEnabled = relative > 1.0005;
            ZoomInButton.IsEnabled = relative < MaxZoomOverFit - 0.0005;
        }

        private void PlanViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_needsFit) FitPlan(); else PositionMarkers();
        }

        private void Plan_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_planWidth <= 0) return;
            double factor = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;
            SetZoom(PlanScale.ScaleX * factor, e.GetPosition(PlanViewport));
            e.Handled = true;
        }

        private void Plan_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_planWidth <= 0) return;
            _dragging = true;
            _dragStart = e.GetPosition(PlanViewport);
            _panStartX = PlanPan.X;
            _panStartY = PlanPan.Y;
            PlanViewport.CaptureMouse();
        }

        private void Plan_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var now = e.GetPosition(PlanViewport);
            PlanPan.X = _panStartX + (now.X - _dragStart.X);
            PlanPan.Y = _panStartY + (now.Y - _dragStart.Y);
            ClampPan();
            PositionMarkers();
        }

        private void Plan_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            PlanViewport.ReleaseMouseCapture();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomFromCentre(ZoomStep);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomFromCentre(1 / ZoomStep);

        private void ZoomFromCentre(double factor)
        {
            if (_planWidth <= 0) return;
            var centre = new Point(PlanViewport.ActualWidth / 2, PlanViewport.ActualHeight / 2);
            SetZoom(PlanScale.ScaleX * factor, centre);
        }

        private void Marker_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not MarkerDto m) return;
            if (_tutorialStep == TutorialPossession) CompleteTutorial();
            if (string.IsNullOrWhiteSpace(m.Image)) return;

            var shot = MapDataService.LoadImage(m.ImageFullPath);
            if (shot == null) return;

            ShotImage.Source = shot;
            ShotTitle.Text = m.Item;
            ShotRoom.Text = m.Room;
            ShotRoom.Visibility = string.IsNullOrWhiteSpace(m.Room) ? Visibility.Collapsed : Visibility.Visible;
            ShotPopover.Visibility = Visibility.Visible;
        }

        private void ShotPopover_Click(object sender, MouseButtonEventArgs e) => HideShot();

        private void HideShot()
        {
            ShotPopover.Visibility = Visibility.Collapsed;
            ShotImage.Source = null;
        }

        private void Back_Click(object sender, RoutedEventArgs e) => ResetToPicker();

        private void ResetToPicker()
        {
            HideShot();
            PlanImage.Source = null;
            MarkerLayer.Children.Clear();
            _map = null;
            _version = null;
            _floor = null;
            MapSelection.Clear();
            VersionBar.Children.Clear();
            VersionSegment.Visibility = Visibility.Collapsed;
            VersionLabel.Visibility = Visibility.Collapsed;
            FloorLabel.Visibility = Visibility.Collapsed;
            _planWidth = _planHeight = 0;
            _fitScale = 0;

            DetailView.Visibility = Visibility.Collapsed;
            PickerView.Visibility = Visibility.Visible;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => this.Hide();

        /// <summary>
        /// Onboarding's maps lesson: pick a map, then click a possession. <paramref name="finished"/>
        /// gets false if the window is closed first.
        /// </summary>
        public void StartTutorial(Action<bool> finished)
        {
            if (_tutorialStep >= 0) return;

            _tutorialFinished = finished;
            _tutorialRun++;
            ResetToPicker();

            CloseButton.IsEnabled = false;
            CloseButton.Opacity = 0.28;
            BackButton.IsEnabled = false;
            BackButton.Opacity = 0.28;

            ShowTutorialStep(TutorialPickMap);
        }

        private void ShowTutorialStep(int step)
        {
            _tutorialStep = step;
            TutorialCoach.Visibility = Visibility.Visible;

            switch (step)
            {
                case TutorialPickMap:
                    TutorialStepText.Text = "1 OF 2";
                    TutorialTitle.Text = "Pick A Map";
                    TutorialBody.Text = "Choose the map you are investigating to open its floor plan.";
                    break;
                case TutorialPossession:
                    TutorialStepText.Text = "2 OF 2";
                    TutorialTitle.Text = "Find A Cursed Object";
                    TutorialBody.Text = "Each icon marks where a cursed possession spawns. Click one to see its photo.";
                    break;
                case TutorialDone:
                    TutorialStepText.Text = "DONE";
                    TutorialTitle.Text = "Maps Basics Complete";
                    TutorialBody.Text = "The map you picked stays selected, ready to bring up in game.";
                    break;
            }
        }

        private async void CompleteTutorial()
        {
            int run = _tutorialRun;
            ShowTutorialStep(TutorialDone);

            await Task.Delay(TimeSpan.FromSeconds(2));
            if (_tutorialStep == TutorialDone && _tutorialRun == run) EndTutorial(true);
        }

        private void EndTutorial(bool completed)
        {
            if (_tutorialStep < 0) return;

            var finished = _tutorialFinished;
            _tutorialFinished = null;
            _tutorialStep = -1;
            _tutorialRun++;

            TutorialCoach.Visibility = Visibility.Collapsed;
            CloseButton.ClearValue(IsEnabledProperty);
            CloseButton.ClearValue(OpacityProperty);
            BackButton.ClearValue(IsEnabledProperty);
            BackButton.ClearValue(OpacityProperty);
            HideShot();

            if (!_closed) Hide();
            finished?.Invoke(completed);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }
    }
}
