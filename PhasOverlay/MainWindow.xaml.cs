using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Interop;

namespace PhasOverlay
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("winmm.dll")]
        private static extern long mciSendString(string command, string returnValue, int returnLength, IntPtr winHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;

        private const double TimersHoverOpacity = 0.25;
        private bool _timersHovered = false;

        private IntPtr _hwnd = IntPtr.Zero;
        private bool _clickThrough = false;

        private DispatcherTimer _gameLoop;
        private NotificationWindow _notifWin;

        private uint _phasmoProcessId = 0;

        private bool _k1Last = false, _k2Last = false, _k3Last = false;
        private bool _k4Last = false, _k5Last = false, _k6Last = false, _k7Last = false;
        private bool _spaceLast = false, _homeLast = false;

        private bool _kEvidenceLast = false, _kClearLast = false;
        private bool _kToggleEvLast = false;

        private bool _kEv1Last = false, _kEv2Last = false, _kEv3Last = false;
        private bool _kEv4Last = false, _kEv5Last = false, _kEv6Last = false, _kEv7Last = false;

        public double BaseHuntDuration = 30.0;
        public bool IsCursedHunt = false;
        public bool IsBloodMoonActive = false;

        public double MasterVolume = 1.0;
        public int EvidenceLimit = 3;

        // Kept in sync by Settings/Welcome/Evidence; these produce BaseHuntDuration.
        public int DifficultyIndex = 1;   // 0 Amateur .. 4 Insanity, 5 Weekly, 6 Custom
        public int MapSizeIndex = 0;      // 0 Small, 1 Medium, 2 Large
        public int CustomDurationIndex = 1; // 0 Low, 1 Med, 2 High (only used when Custom)

        public const int DiffWeekly = 5;
        public const int DiffCustom = 6;

        // Non-null only while DifficultyIndex == DiffWeekly.
        public WeeklyEntry? ActiveWeekly = null;

        // Hunt-length lookup (unchanged game values): [durationTier, mapSize] -> seconds.
        private static readonly double[,] HuntTimes = {
            { 15.0, 30.0, 40.0 },
            { 20.0, 40.0, 50.0 },
            { 30.0, 50.0, 60.0 }
        };

        /// <summary>Resolves the Low/Med/High duration tier (0/1/2) from difficulty + custom.</summary>
        public int ResolveHuntTier()
        {
            if (DifficultyIndex == 0) return 0;                       // Amateur -> Low
            if (DifficultyIndex == 1) return 1;                       // Intermediate -> Med
            if (DifficultyIndex >= 2 && DifficultyIndex <= 4) return 2; // Prof/Nightmare/Insanity -> High
            if (DifficultyIndex == DiffWeekly) return ActiveWeekly?.HuntTier ?? 2;
            if (DifficultyIndex == DiffCustom) return Math.Clamp(CustomDurationIndex, 0, 2);
            return 1;
        }

        /// <summary>Recomputes BaseHuntDuration from the current difficulty/custom/map selection.</summary>
        public void RecomputeHuntDuration()
        {
            int map = Math.Clamp(MapSizeIndex, 0, 2);
            BaseHuntDuration = HuntTimes[ResolveHuntTier(), map];
        }

        /// <summary>Applies a weekly challenge's fixed settings and recomputes hunt length.</summary>
        public void ApplyWeekly(WeeklyEntry w)
        {
            ActiveWeekly = w;
            DifficultyIndex = DiffWeekly;
            MapSizeIndex = w.MapSizeIndex;
            SpeedMultiplierSetting = w.GhostSpeed;
            EvidenceLimit = w.EvidenceGiven;
            RecomputeHuntDuration();
        }

        /// <summary>Lets an open Evidence tracker mirror match changes made in Settings live.</summary>
        public void NotifyMatchSettingsChanged() => _evidenceWin?.SyncMatchControls();

        public bool IsCompactMode = true;
        public int OverlayPosition = 1;
        public bool AlwaysShowEvidence = false;
        public bool IsOverlayEvHidden = false; // Tracks manual visibility toggle

        public DateTime LastSettingsPreviewTime = DateTime.MinValue;
        public bool IsTutorialActive = false;

        public bool[] ModStates = new bool[] { true, true, true, true, true, true, true, true, true };

        // Set in a binding's high bits to mean "Shift also required".
        public const int ShiftFlag = 0x10000;

        public int KeySmudge = 0x70;      // F1
        public int KeyCooldown = 0x71;    // F2
        public int KeyHunt = 0x72;        // F3
        public int KeyObambo = 0x73;      // F4
        public int KeySpeedReset = 0x74;  // F5
        public int KeyBloodMoon = 0x75;   // F6
        public int KeyCursedHunt = 0x76;  // F7
        public int KeySpeedTap = 0x20;    // Space
        public int KeySettings = 0x24;    // Home

        public int KeyEvidence = 0x4F;    // O
        public int KeyClear = 0x30;       // 0
        public int KeyToggleEv = 0xC0;    // ` (Tilde/Backtick key)

        public int KeyEv1 = 0x70 | ShiftFlag;  // Shift + F1
        public int KeyEv2 = 0x71 | ShiftFlag;  // Shift + F2
        public int KeyEv3 = 0x72 | ShiftFlag;  // Shift + F3
        public int KeyEv4 = 0x73 | ShiftFlag;  // Shift + F4
        public int KeyEv5 = 0x74 | ShiftFlag;  // Shift + F5
        public int KeyEv6 = 0x75 | ShiftFlag;  // Shift + F6
        public int KeyEv7 = 0x76 | ShiftFlag;  // Shift + F7

        private DateTime _huntEndTime;
        private bool _isHuntActive = false;
        private int _huntTick = 0;
        private bool _huntWarned = false;

        private DateTime _cooldownEndTime;
        private bool _isCooldownActive = false;
        private int _cooldownTick = 0;
        private bool _cdDemonWarned = false, _cdDemonAlert = false;
        private bool _cdStandardWarned = false;

        private DateTime _smudgeEndTime;
        private bool _isSmudgeActive = false;
        private bool _smudgeDemon = false, _warnedDemon = false;
        private bool _smudgeStandard = false, _warnedStandard = false;
        private bool _smudgeSpirit = false, _warnedSpirit = false;
        private int _smudgeTick = 0;

        private DateTime _obamboStartTime;
        private DateTime _obamboNextTargetTime;
        private bool _isObamboActive = false;
        private bool _obamboIsFast = false;

        private List<DateTime> _recentTaps = new List<DateTime>();

        private double _speedMultiplierSetting = 1.0;
        public double SpeedMultiplierSetting
        {
            get { return _speedMultiplierSetting; }
            set
            {
                _speedMultiplierSetting = value;
                UpdateSpeedDisplay();
            }
        }

        private SettingsWindow _settingsWin = null;
        private EvidenceWindow _evidenceWin = null;

        // Parsed key-by-key so one malformed value can't abort the whole load (WriteAllLines isn't
        // atomic — a kill mid-save can leave a half-written config).
        private static int ReadInt(Dictionary<string, string> d, string key, int fallback)
        {
            return d.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : fallback;
        }

        private static double ReadDouble(Dictionary<string, string> d, string key, double fallback)
        {
            if (!d.TryGetValue(key, out var s)) return fallback;
            // Accept both invariant and local separators so a config survives a locale change.
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var inv)) return inv;
            if (double.TryParse(s, out var cur)) return cur;
            return fallback;
        }

        private static readonly Dictionary<string, System.Windows.Media.Brush> _brushCache = new();
        private static System.Windows.Media.Brush GetBrush(string hex)
        {
            if (!_brushCache.TryGetValue(hex, out var brush))
            {
                brush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(hex);
                brush.Freeze();
                _brushCache[hex] = brush;
            }
            return brush;
        }

        private static readonly IEasingFunction EaseOutQuart = FreezeEase(EasingMode.EaseOut);
        private static readonly IEasingFunction EaseInQuart = FreezeEase(EasingMode.EaseIn);
        private static IEasingFunction FreezeEase(EasingMode mode)
        {
            QuarticEase ease = new QuarticEase { EasingMode = mode };
            ease.Freeze();
            return ease;
        }

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();

            _evidenceWin = new EvidenceWindow(this);
            SyncEvidenceUI();

            this.SizeChanged += (s, e) => UpdateWindowPosition();

            _notifWin = new NotificationWindow();
            _notifWin.Show();

            _gameLoop = new DispatcherTimer();
            _gameLoop.Interval = TimeSpan.FromMilliseconds(50);
            _gameLoop.Tick += GameLoop_Tick;
            _gameLoop.Start();

            _ = RefreshWeeklyAsync();
        }

        /// <summary>Pulls a newer weekly.json, re-applying + mirroring it if Weekly is active.</summary>
        public async Task RefreshWeeklyAsync()
        {
            bool changed = await WeeklyDataService.CheckForUpdatesAsync();
            if (!changed) return;

            Dispatcher.Invoke(() =>
            {
                if (DifficultyIndex == DiffWeekly)
                {
                    var weekly = WeeklyDataService.GetWeekly();
                    if (weekly != null)
                    {
                        ApplyWeekly(weekly);
                        RefreshCompactModeVisuals(true);
                    }
                }
                NotifyMatchSettingsChanged();
            });
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThrough(true);
        }

        /// <summary>
        /// Adds/removes WS_EX_TRANSPARENT on the overlay window. This is the only thing that
        /// makes clicks reach another process (Phasmophobia) — answering WM_NCHITTEST with
        /// HTTRANSPARENT only falls through to windows on our own thread, which is useless here.
        /// </summary>
        private void ApplyClickThrough(bool enabled)
        {
            if (_hwnd == IntPtr.Zero || enabled == _clickThrough) return;

            _clickThrough = enabled;

            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, enabled ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
        }

        /// <summary>Screen-pixel hit test against an element's on-screen rectangle.</summary>
        private static bool IsPointOverElement(FrameworkElement element, int screenX, int screenY)
        {
            if (element == null || !element.IsVisible) return false;
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;

            try
            {
                // PointToScreen already accounts for the overlay scale transform and DPI.
                Point topLeft = element.PointToScreen(new Point(0, 0));
                Point bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));

                return screenX >= topLeft.X && screenX < bottomRight.X
                    && screenY >= topLeft.Y && screenY < bottomRight.Y;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Keeps the overlay click-through everywhere except the evidence panel, which is the
        /// only interactive surface (its ghost cards open the tracker). The timers panel is
        /// therefore always click-through, and fades down while the cursor is over it. The
        /// window gets no mouse events while transparent, so the cursor is polled instead.
        /// </summary>
        private void UpdateOverlayInputState()
        {
            if (!GetCursorPos(out NativePoint cursor))
            {
                ApplyClickThrough(true);
                return;
            }

            ApplyClickThrough(!IsPointOverElement(EvidenceColumnBorder, cursor.X, cursor.Y));

            if (TopCenterBorder == null) return;

            bool hovered = IsPointOverElement(TopCenterBorder, cursor.X, cursor.Y);
            if (hovered == _timersHovered) return;

            _timersHovered = hovered;

            DoubleAnimation fade = new DoubleAnimation(hovered ? TimersHoverOpacity : 1.0,
                                                       TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = EaseOutQuart
            };
            TopCenterBorder.BeginAnimation(OpacityProperty, fade);
        }

        public void UpdateWindowPosition()
        {
            var workArea = SystemParameters.WorkArea;

            this.Left = workArea.Left;
            this.Top = workArea.Top;
            this.Width = workArea.Width;

            // Panels sit along the top, so the window only spans a top band; the rest of
            // the screen stays click-through. Height is generous so nothing clips at max scale.
            this.Height = Math.Min(workArea.Height, Math.Max(720, workArea.Height * 0.6));
        }

        private bool IsGameOrOverlayFocused()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hWnd, out uint processId);

            if (processId == (uint)Environment.ProcessId)
            {
                return true;
            }

            if (_phasmoProcessId != 0 && processId == _phasmoProcessId)
            {
                return true;
            }

            try
            {
                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                if (process.ProcessName.Equals("Phasmophobia", StringComparison.OrdinalIgnoreCase))
                {
                    _phasmoProcessId = processId;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private void TriggerPulse(TextBlock target)
        {
            ColorAnimation pulse = new ColorAnimation();
            pulse.From = System.Windows.Media.Color.FromRgb(255, 51, 51);
            pulse.To = System.Windows.Media.Color.FromRgb(68, 0, 0);
            pulse.Duration = new Duration(TimeSpan.FromSeconds(1));

            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 51, 51));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
            target.Foreground = brush;
        }

        private void ResetPulse(TextBlock target)
        {
            target.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        }

        // True if the (possibly Shift-flagged) binding is currently held.
        public static bool KeyHeld(int keyWithFlags, bool shiftDown)
        {
            bool pressed = (GetAsyncKeyState(keyWithFlags & 0xFFFF) & 0x8000) != 0;

            // Shift must match exactly. The timer actions (F1-F7) and the evidence actions
            // (Shift + F1-F7) share physical keys, so a plain binding has to reject Shift or
            // one keypress would trigger both.
            return (keyWithFlags & ShiftFlag) != 0 ? (pressed && shiftDown) : (pressed && !shiftDown);
        }

        public void ResetKeybinds()
        {
            KeySmudge = 0x70;
            KeyCooldown = 0x71;
            KeyHunt = 0x72;
            KeyObambo = 0x73;
            KeySpeedReset = 0x74;
            KeyBloodMoon = 0x75;
            KeyCursedHunt = 0x76;
            KeySpeedTap = 0x20;
            KeySettings = 0x24;

            KeyEvidence = 0x4F;
            KeyClear = 0x30;
            KeyToggleEv = 0xC0;

            KeyEv1 = 0x70 | ShiftFlag;
            KeyEv2 = 0x71 | ShiftFlag;
            KeyEv3 = 0x72 | ShiftFlag;
            KeyEv4 = 0x73 | ShiftFlag;
            KeyEv5 = 0x74 | ShiftFlag;
            KeyEv6 = 0x75 | ShiftFlag;
            KeyEv7 = 0x76 | ShiftFlag;
        }

        public void SyncKeybind(string target, int code)
        {
            if (target == "Smudge") { KeySmudge = code; _k1Last = true; }
            else if (target == "Cooldown") { KeyCooldown = code; _k2Last = true; }
            else if (target == "Hunt") { KeyHunt = code; _k3Last = true; }
            else if (target == "Obambo") { KeyObambo = code; _k4Last = true; }
            else if (target == "SpeedReset") { KeySpeedReset = code; _k5Last = true; }
            else if (target == "BloodMoon") { KeyBloodMoon = code; _k6Last = true; }
            else if (target == "CursedHunt") { KeyCursedHunt = code; _k7Last = true; }
            else if (target == "SpeedTap") { KeySpeedTap = code; _spaceLast = true; }
            else if (target == "Settings") { KeySettings = code; _homeLast = true; }
            else if (target == "Evidence") { KeyEvidence = code; _kEvidenceLast = true; }
            else if (target == "Clear") { KeyClear = code; _kClearLast = true; }
            else if (target == "ToggleEv") { KeyToggleEv = code; _kToggleEvLast = true; }
            else if (target == "Ev1") { KeyEv1 = code; _kEv1Last = true; }
            else if (target == "Ev2") { KeyEv2 = code; _kEv2Last = true; }
            else if (target == "Ev3") { KeyEv3 = code; _kEv3Last = true; }
            else if (target == "Ev4") { KeyEv4 = code; _kEv4Last = true; }
            else if (target == "Ev5") { KeyEv5 = code; _kEv5Last = true; }
            else if (target == "Ev6") { KeyEv6 = code; _kEv6Last = true; }
            else if (target == "Ev7") { KeyEv7 = code; _kEv7Last = true; }
        }

        public void ApplyModuleVisibility(bool[] states)
        {
            ModStates = states;
            RefreshCompactModeVisuals(true);
        }

        public bool HasAnyEvidenceSet()
        {
            if (_evidenceWin == null) return false;
            for (int i = 1; i <= 7; i++)
            {
                if (_evidenceWin.GetEvidenceState(i) != null) return true;
            }
            return false;
        }

        public void SyncEvidenceUI()
        {
            if (_evidenceWin == null) return;
            UpdateEvText(Ev1Text, _evidenceWin.GetEvidenceState(1));
            UpdateEvText(Ev2Text, _evidenceWin.GetEvidenceState(2));
            UpdateEvText(Ev3Text, _evidenceWin.GetEvidenceState(3));
            UpdateEvText(Ev4Text, _evidenceWin.GetEvidenceState(4));
            UpdateEvText(Ev5Text, _evidenceWin.GetEvidenceState(5));
            UpdateEvText(Ev6Text, _evidenceWin.GetEvidenceState(6));
            UpdateEvText(Ev7Text, _evidenceWin.GetEvidenceState(7));

            RefreshCompactModeVisuals();
        }

        private void UpdateEvText(TextBlock tb, bool? state)
        {
            if (state == true)
            {
                tb.Foreground = GetBrush("#FFB455FF");
                tb.TextDecorations = null;
            }
            else if (state == false)
            {
                tb.Foreground = GetBrush("#FFFF5555");
                tb.TextDecorations = TextDecorations.Strikethrough;
            }
            else
            {
                tb.Foreground = GetBrush("#FF55555A");
                tb.TextDecorations = null;
            }
        }

        public void UpdatePossibleGhostsUI(IEnumerable<GhostData> ghosts)
        {
            if (OverlayGhostList == null) return;
            OverlayGhostList.Children.Clear();

            bool isSpeedActive = _recentTaps.Count > 1;
            int shownCount = 0;

            foreach (var ghost in ghosts)
            {
                if (isSpeedActive && !ghost.IsSpeedHighlighted) continue;
                shownCount++;

                Border ghostCard = new Border
                {
                    Background = GetBrush("#FF1C1C1F"),
                    BorderBrush = GetBrush(isSpeedActive && ghost.IsSpeedHighlighted ? "#FFB455FF" : "#FF2A2A2E"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 5, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                StackPanel innerStack = new StackPanel { Orientation = Orientation.Vertical };

                TextBlock ghostName = new TextBlock
                {
                    Text = ghost.Name,
                    Foreground = GetBrush(isSpeedActive && ghost.IsSpeedHighlighted ? "White" : "#FFDDDDDD"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                TextBlock ghostFact = new TextBlock
                {
                    Text = ghost.ShortFact,
                    Foreground = GetBrush("#FF888888"),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap
                };

                innerStack.Children.Add(ghostName);
                innerStack.Children.Add(ghostFact);
                ghostCard.Child = innerStack;

                ghostCard.MouseLeftButtonDown += (s, e) =>
                {
                    if (_evidenceWin != null)
                    {
                        _evidenceWin.Show();
                        _evidenceWin.ExpandGhostView(ghost.Name);
                    }
                };
                OverlayGhostScroller.ScrollToTop();
                OverlayGhostList.Children.Add(ghostCard);
            }

            if (HdrGhosts != null) HdrGhosts.Text = $"POSSIBLE GHOSTS: {shownCount}";

            RefreshCompactModeVisuals();
        }

        public void RefreshCompactModeVisuals(bool instant = false, bool noDelay = false)
        {
            bool isPreviewing = LastSettingsPreviewTime != DateTime.MinValue && (DateTime.Now - LastSettingsPreviewTime).TotalSeconds < 2.0;

            bool smudge = (IsCompactMode && !isPreviewing) ? _isSmudgeActive && ModStates[0] : ModStates[0];
            bool cooldown = (IsCompactMode && !isPreviewing) ? _isCooldownActive && ModStates[1] : ModStates[1];
            bool hunt = (IsCompactMode && !isPreviewing) ? _isHuntActive && ModStates[2] : ModStates[2];
            bool obambo = (IsCompactMode && !isPreviewing) ? _isObamboActive && ModStates[3] : ModStates[3];
            bool speed = (IsCompactMode && !isPreviewing) ? _recentTaps.Count > 1 && ModStates[4] : ModStates[4];

            bool evidence = (IsCompactMode && !isPreviewing) ? (HasAnyEvidenceSet() || AlwaysShowEvidence) && ModStates[7] : ModStates[7];
            bool ghostsList = (IsCompactMode && !isPreviewing) ? OverlayGhostList.Children.Count > 0 && ModStates[8] : ModStates[8];

            if (IsOverlayEvHidden && !isPreviewing)
            {
                evidence = false;
                ghostsList = false;
            }

            // The welcome/tutorial is curated: only what it explicitly triggers (the hunt-timer
            // demo) should appear, never the evidence or possible-ghosts panels.
            if (IsTutorialActive)
            {
                evidence = false;
                ghostsList = false;
            }

            bool timersActive = smudge || cooldown || hunt || obambo || speed;

            bool showBloodMoonIcon = ModStates[5] && (!IsCompactMode || IsBloodMoonActive || isPreviewing);
            bool showCursedIcon = ModStates[6] && (!IsCompactMode || IsCursedHunt || isPreviewing);
            bool rightActive = showBloodMoonIcon || showCursedIcon;

            bool anyActive = timersActive || evidence || ghostsList || rightActive;

            SetModuleDisplay(ModSmudge, smudge, instant, noDelay);
            SetModuleDisplay(ModCooldown, cooldown, instant, noDelay);
            SetModuleDisplay(ModHunt, hunt, instant, noDelay);
            SetModuleDisplay(ModObambo, obambo, instant, noDelay);
            SetModuleDisplay(ModSpeed, speed, instant, noDelay);
            SetModuleDisplay(ModEvidence, evidence, instant, noDelay);
            SetModuleDisplay(ModGhosts, ghostsList, instant, noDelay);

            SetModuleDisplay(ModBloodMoon, showBloodMoonIcon, instant, noDelay);
            SetModuleDisplay(ModCursed, showCursedIcon, instant, noDelay);

            SetModuleDisplay(TimersPanel, timersActive, instant, noDelay);
            SetModuleDisplay(EvidenceColumnBorder, evidence || ghostsList, instant, noDelay);
            SetModuleDisplay(RightColumnBorder, rightActive, instant, noDelay);

            if (anyActive && MainContainer.Visibility != Visibility.Visible)
            {
                MainContainer.Visibility = Visibility.Visible;
                MainContainer.Opacity = 1;
            }
            else if (!anyActive)
            {
                MainContainer.Visibility = Visibility.Collapsed;
                MainContainer.Opacity = 0;
            }
        }

        private void UpdateDynamicLayout()
        {
            if (ModSmudge == null) return;

            bool timersVisible = ModSmudge.Visibility == Visibility.Visible || ModCooldown.Visibility == Visibility.Visible || ModHunt.Visibility == Visibility.Visible || ModObambo.Visibility == Visibility.Visible || ModSpeed.Visibility == Visibility.Visible;
            bool modifiersVisible = ModBloodMoon.Visibility == Visibility.Visible || ModCursed.Visibility == Visibility.Visible;

            if (modifiersVisible && timersVisible)
            {
                RightColumnBorder.BorderThickness = new Thickness(1, 0, 0, 0);
                RightColumnBorder.Margin = new Thickness(15, 0, 0, 0);
                RightColumnBorder.Padding = new Thickness(15, 0, 0, 0);
            }
            else
            {
                RightColumnBorder.BorderThickness = new Thickness(0);
                RightColumnBorder.Margin = new Thickness(0);
                RightColumnBorder.Padding = new Thickness(0);
            }

            if (!timersVisible && !modifiersVisible)
            {
                TopCenterBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                TopCenterBorder.Visibility = Visibility.Visible;
            }

            if (OverlayPosition == 0) // Top Left
            {
                TopCenterBorder.HorizontalAlignment = HorizontalAlignment.Left;
                TopCenterBorder.Margin = new Thickness(15, 10, 0, 0); // 5px top gap

                EvidenceColumnBorder.HorizontalAlignment = HorizontalAlignment.Right;
                EvidenceColumnBorder.Margin = new Thickness(0, 10, 15, 0); // 5px top gap
            }
            else if (OverlayPosition == 1) // Top Centre
            {
                TopCenterBorder.HorizontalAlignment = HorizontalAlignment.Center;
                TopCenterBorder.Margin = new Thickness(0, 10, 0, 0); // 5px top gap

                EvidenceColumnBorder.HorizontalAlignment = HorizontalAlignment.Right;
                EvidenceColumnBorder.Margin = new Thickness(0, 10, 15, 0); // 5px top gap
            }
            else if (OverlayPosition == 2) // Top Right
            {
                TopCenterBorder.HorizontalAlignment = HorizontalAlignment.Right;
                TopCenterBorder.Margin = new Thickness(0, 10, 15, 0); // 5px top gap

                EvidenceColumnBorder.HorizontalAlignment = HorizontalAlignment.Left;
                EvidenceColumnBorder.Margin = new Thickness(15, 10, 0, 0); // 5px top gap
            }
        }

        private void SetModuleDisplay(FrameworkElement element, bool shouldBeVisible, bool instant, bool noDelay)
        {
            if (element.Tag is bool currentTarget && currentTarget == shouldBeVisible && !instant)
                return;

            element.Tag = shouldBeVisible;

            TransformGroup renderTransform = element.RenderTransform as TransformGroup;
            ScaleTransform renderScale = null;
            TranslateTransform renderTranslate = null;

            if (renderTransform != null)
            {
                renderScale = renderTransform.Children[0] as ScaleTransform;
                renderTranslate = renderTransform.Children[1] as TranslateTransform;
            }

            bool canAnimate = renderScale != null && renderTranslate != null && element.Name != "MainContainer";

            double outY = -15;
            double outX = 0;

            if (shouldBeVisible)
            {
                element.Visibility = Visibility.Visible;
                UpdateDynamicLayout();

                if (instant)
                {
                    element.BeginAnimation(OpacityProperty, null);
                    if (canAnimate)
                    {
                        renderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        renderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        renderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                        renderTranslate.BeginAnimation(TranslateTransform.XProperty, null);

                        renderScale.ScaleX = 1; renderScale.ScaleY = 1;
                        renderTranslate.Y = 0; renderTranslate.X = 0;
                    }
                    element.Opacity = 1;
                }
                else
                {
                    IEasingFunction ease = EaseOutQuart;

                    if (canAnimate)
                    {
                        renderTranslate.Y = outY;
                        renderTranslate.X = outX;
                        renderScale.ScaleX = 0.8; renderScale.ScaleY = 0.8;

                        DoubleAnimation renderScaleIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(150) };
                        DoubleAnimation renderSlideYIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(150) };
                        DoubleAnimation renderSlideXIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(150) };
                        DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(150) };

                        renderScale.BeginAnimation(ScaleTransform.ScaleXProperty, renderScaleIn);
                        renderScale.BeginAnimation(ScaleTransform.ScaleYProperty, renderScaleIn);
                        renderTranslate.BeginAnimation(TranslateTransform.YProperty, renderSlideYIn);
                        renderTranslate.BeginAnimation(TranslateTransform.XProperty, renderSlideXIn);
                        element.BeginAnimation(OpacityProperty, fadeIn);
                    }
                    else
                    {
                        DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease };
                        element.BeginAnimation(OpacityProperty, fadeIn);
                    }
                }
            }
            else
            {
                if (instant)
                {
                    element.BeginAnimation(OpacityProperty, null);
                    if (canAnimate)
                    {
                        renderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        renderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        renderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                        renderTranslate.BeginAnimation(TranslateTransform.XProperty, null);

                        renderScale.ScaleX = 0; renderScale.ScaleY = 0;
                    }
                    element.Opacity = 0;
                    element.Visibility = Visibility.Collapsed;
                    UpdateDynamicLayout();
                }
                else
                {
                    IEasingFunction easeIn = EaseInQuart;
                    double initialDelay = noDelay ? 0 : 0.75;

                    if (canAnimate)
                    {
                        DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeIn, BeginTime = TimeSpan.FromSeconds(initialDelay) };
                        DoubleAnimation renderScaleOut = new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeIn, BeginTime = TimeSpan.FromSeconds(initialDelay) };
                        DoubleAnimation renderSlideYOut = new DoubleAnimation(outY, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeIn, BeginTime = TimeSpan.FromSeconds(initialDelay) };
                        DoubleAnimation renderSlideXOut = new DoubleAnimation(outX, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeIn, BeginTime = TimeSpan.FromSeconds(initialDelay) };

                        fadeOut.Completed += (s, e) => {
                            if (element.Tag is bool target && !target)
                            {
                                element.Visibility = Visibility.Collapsed;
                                UpdateDynamicLayout();
                            }
                        };

                        element.BeginAnimation(OpacityProperty, fadeOut);
                        renderScale.BeginAnimation(ScaleTransform.ScaleXProperty, renderScaleOut);
                        renderScale.BeginAnimation(ScaleTransform.ScaleYProperty, renderScaleOut);
                        renderTranslate.BeginAnimation(TranslateTransform.YProperty, renderSlideYOut);
                        renderTranslate.BeginAnimation(TranslateTransform.XProperty, renderSlideXOut);
                    }
                    else
                    {
                        DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = easeIn, BeginTime = TimeSpan.FromSeconds(initialDelay) };
                        fadeOut.Completed += (s, e) => {
                            if (element.Tag is bool target && !target)
                            {
                                element.Visibility = Visibility.Collapsed;
                                UpdateDynamicLayout();
                            }
                        };
                        element.BeginAnimation(OpacityProperty, fadeOut);
                    }
                }
            }
        }

        public void PlayAudio(string fileNameKey)
        {
            string alias = "audio_" + Guid.NewGuid().ToString("N");
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", fileNameKey + ".mp3");

            mciSendString($"open \"{filePath}\" type mpegvideo alias {alias}", null, 0, IntPtr.Zero);

            double baseVolume = fileNameKey == "alert" ? 0.75 : 1.0;
            int vol = (int)(baseVolume * MasterVolume * 1000);
            mciSendString($"setaudio {alias} volume to {vol}", null, 0, IntPtr.Zero);

            mciSendString($"play {alias}", null, 0, IntPtr.Zero);

            DispatcherTimer killTimer = new DispatcherTimer();
            killTimer.Interval = TimeSpan.FromSeconds(4);
            killTimer.Tick += (s, e) =>
            {
                killTimer.Stop();
                mciSendString($"close {alias}", null, 0, IntPtr.Zero);
            };
            killTimer.Start();
        }

        public void TestAudio()
        {
            PlayAudio("alert");
        }

        private DateTime _lastEvSoundTime = DateTime.MinValue;

        private void HandleEvidenceShortcut(int index)
        {
            if (_evidenceWin != null)
            {
                string notificationText = _evidenceWin.CycleEvidence(index);
                if (notificationText != null)
                {
                    if ((DateTime.Now - _lastEvSoundTime).TotalMilliseconds > 100)
                    {
                        PlayAudio("alert");
                        _lastEvSoundTime = DateTime.Now;
                    }

                    if (!ModStates[7])
                    {
                        ShowNotification(notificationText);
                    }
                }
            }
        }

        public void ShowNotification(string message)
        {
            _notifWin?.ShowMessage(message);
        }

        private void LoadSettings()
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay");
            Directory.CreateDirectory(appDataFolder);
            string configPath = Path.Combine(appDataFolder, "settings.txt");

            string oldConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(oldConfigPath))
            {
                try { File.Delete(oldConfigPath); } catch { }
            }

            bool isFirstRun = !File.Exists(configPath);

            if (!isFirstRun)
            {
                try
                {
                    string rawText = File.ReadAllText(configPath);

                    if (rawText.Contains("=") && !rawText.StartsWith("0|") && !rawText.StartsWith("1|"))
                    {
                        var dict = new Dictionary<string, string>();
                        foreach (var line in File.ReadAllLines(configPath))
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("[")) continue;
                            var parts = line.Split(new[] { '=' }, 2);
                            if (parts.Length == 2) dict[parts[0].Trim()] = parts[1].Trim();
                        }

                        // Clamped so a hand-edited or corrupt index can't blow up the huntTimes lookup.
                        int mapIdx = Math.Clamp(ReadInt(dict, "MapSize", 0), 0, 2);
                        int spdIdx = ReadInt(dict, "GhostSpeed", 2);
                        int durIdx = Math.Clamp(ReadInt(dict, "HuntDuration", 1), 0, 2);

                        MapSizeIndex = mapIdx;
                        DifficultyIndex = ReadInt(dict, "Difficulty", DifficultyIndex);
                        CustomDurationIndex = ReadInt(dict, "CustomDuration", CustomDurationIndex);

                        // Pre-Weekly, index 5 meant Custom. Old files (no SettingsVersion) remap 5 -> 6.
                        if (!dict.ContainsKey("SettingsVersion") && DifficultyIndex == DiffWeekly)
                            DifficultyIndex = DiffCustom;

                        BgBrush.Opacity = ReadDouble(dict, "Opacity", 0.8);
                        double scale = ReadDouble(dict, "Scale", 1.0);
                        OverlayScale.ScaleX = scale;
                        OverlayScale.ScaleY = scale;

                        if (spdIdx == 0) SpeedMultiplierSetting = 0.5;
                        else if (spdIdx == 1) SpeedMultiplierSetting = 0.75;
                        else if (spdIdx == 2) SpeedMultiplierSetting = 1.0;
                        else if (spdIdx == 3) SpeedMultiplierSetting = 1.25;
                        else if (spdIdx == 4) SpeedMultiplierSetting = 1.5;
                        else SpeedMultiplierSetting = 1.0;

                        OverlayPosition = ReadInt(dict, "Position", OverlayPosition);

                        if (dict.ContainsKey("ModulesActive"))
                        {
                            string statesStr = dict["ModulesActive"];
                            for (int i = 0; i < 9; i++)
                            {
                                if (i < statesStr.Length) ModStates[i] = (statesStr[i] == '1');
                                else ModStates[i] = true;
                            }
                        }

                        if (dict.ContainsKey("CompactMode")) IsCompactMode = dict["CompactMode"] == "1";
                        if (dict.ContainsKey("AlwaysShowEvidence")) AlwaysShowEvidence = dict["AlwaysShowEvidence"] == "1";
                        MasterVolume = ReadDouble(dict, "Volume", MasterVolume);
                        EvidenceLimit = ReadInt(dict, "EvidenceLimit", EvidenceLimit);
                        KeySmudge = ReadInt(dict, "KeySmudge", KeySmudge);
                        KeyCooldown = ReadInt(dict, "KeyCooldown", KeyCooldown);
                        KeyHunt = ReadInt(dict, "KeyHunt", KeyHunt);
                        KeyObambo = ReadInt(dict, "KeyObambo", KeyObambo);
                        KeySpeedReset = ReadInt(dict, "KeySpeedReset", KeySpeedReset);
                        KeyBloodMoon = ReadInt(dict, "KeyBloodMoon", KeyBloodMoon);
                        KeyCursedHunt = ReadInt(dict, "KeyCursedHunt", KeyCursedHunt);
                        KeySpeedTap = ReadInt(dict, "KeySpeedTap", KeySpeedTap);
                        KeySettings = ReadInt(dict, "KeySettings", KeySettings);

                        KeyEvidence = ReadInt(dict, "KeyEvidence", KeyEvidence);
                        KeyClear = ReadInt(dict, "KeyClear", KeyClear);
                        KeyToggleEv = ReadInt(dict, "KeyToggleEv", KeyToggleEv);

                        KeyEv1 = ReadInt(dict, "KeyEv1", KeyEv1);
                        KeyEv2 = ReadInt(dict, "KeyEv2", KeyEv2);
                        KeyEv3 = ReadInt(dict, "KeyEv3", KeyEv3);
                        KeyEv4 = ReadInt(dict, "KeyEv4", KeyEv4);
                        KeyEv5 = ReadInt(dict, "KeyEv5", KeyEv5);
                        KeyEv6 = ReadInt(dict, "KeyEv6", KeyEv6);
                        KeyEv7 = ReadInt(dict, "KeyEv7", KeyEv7);

                        double[,] huntTimes = new double[,] {
                            { 15.0, 30.0, 40.0 },
                            { 20.0, 40.0, 50.0 },
                            { 30.0, 50.0, 60.0 }
                        };
                        BaseHuntDuration = huntTimes[durIdx, mapIdx];

                        // A saved Weekly re-applies the current challenge; falls back to Insanity if none.
                        if (DifficultyIndex == DiffWeekly)
                        {
                            var weekly = WeeklyDataService.GetWeekly();
                            if (weekly != null) ApplyWeekly(weekly);
                            else { DifficultyIndex = 4; RecomputeHuntDuration(); }
                        }
                    }
                    else
                    {
                        string[] settings = rawText.Split('|');
                        if (settings.Length >= 22) OverlayPosition = int.Parse(settings[21]);
                    }
                }
                catch { }
            }

            if (isFirstRun)
            {
                IsTutorialActive = true;
                WelcomeWindow welcome = new WelcomeWindow(this);
                welcome.Closed += (s, e) => {
                    IsTutorialActive = false;
                    OpenSettings(true);
                };
                welcome.Show();
            }

            ApplyModuleVisibility(ModStates);
        }

        private void OpenSettings(bool isFirstRun)
        {
            if (_settingsWin == null)
            {
                _settingsWin = new SettingsWindow(this, isFirstRun);
                _settingsWin.Closed += (s, e) => _settingsWin = null;
                _settingsWin.Show();
            }
        }

        private void GameLoop_Tick(object? sender, EventArgs e)
        {
            bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0; // VK_SHIFT

            bool k1 = KeyHeld(KeySmudge, shiftDown);
            bool k2 = KeyHeld(KeyCooldown, shiftDown);
            bool k3 = KeyHeld(KeyHunt, shiftDown);
            bool k4 = KeyHeld(KeyObambo, shiftDown);
            bool k5 = KeyHeld(KeySpeedReset, shiftDown);
            bool k6 = KeyHeld(KeyBloodMoon, shiftDown);
            bool k7 = KeyHeld(KeyCursedHunt, shiftDown);
            bool space = KeyHeld(KeySpeedTap, shiftDown);
            bool home = KeyHeld(KeySettings, shiftDown);
            bool kEv = KeyHeld(KeyEvidence, shiftDown);
            bool kClr = KeyHeld(KeyClear, shiftDown);
            bool kToggleEv = KeyHeld(KeyToggleEv, shiftDown);

            bool kEv1 = KeyHeld(KeyEv1, shiftDown);
            bool kEv2 = KeyHeld(KeyEv2, shiftDown);
            bool kEv3 = KeyHeld(KeyEv3, shiftDown);
            bool kEv4 = KeyHeld(KeyEv4, shiftDown);
            bool kEv5 = KeyHeld(KeyEv5, shiftDown);
            bool kEv6 = KeyHeld(KeyEv6, shiftDown);
            bool kEv7 = KeyHeld(KeyEv7, shiftDown);

            bool pageUp = (GetAsyncKeyState(0x21) & 0x8000) != 0;
            bool pageDown = (GetAsyncKeyState(0x22) & 0x8000) != 0;

            bool isAppFocused = IsGameOrOverlayFocused();

            if (home && !_homeLast && isAppFocused)
            {
                if (_settingsWin == null) OpenSettings(false);
                else _settingsWin.Close();
            }

            if (!IsTutorialActive && isAppFocused)
            {
                if (k1 && !_k1Last && ModStates[0]) ToggleSmudge();
                if (k2 && !_k2Last && ModStates[1]) ToggleCooldown();
                if (k3 && !_k3Last && ModStates[2]) ToggleHunt();
                if (k4 && !_k4Last && ModStates[3]) ToggleObambo();
                if (k5 && !_k5Last && ModStates[4]) ResetSpeedTap(false);
                if (space && !_spaceLast && ModStates[4]) RecordSpeedTap();

                if (k6 && !_k6Last) ToggleBloodMoon();
                if (k7 && !_k7Last) ToggleCursedHunt();

                if (kEv && !_kEvidenceLast) ToggleEvidenceWindow();
                if (kClr && !_kClearLast) ClearAll();

                if (kToggleEv && !_kToggleEvLast)
                {
                    IsOverlayEvHidden = !IsOverlayEvHidden;
                    RefreshCompactModeVisuals();
                    PlayAudio("alert");
                    ShowNotification(IsOverlayEvHidden ? "Overlay Evidence: Hidden" : "Overlay Evidence: Visible");
                }

                if (kEv1 && !_kEv1Last) HandleEvidenceShortcut(1);
                if (kEv2 && !_kEv2Last) HandleEvidenceShortcut(2);
                if (kEv3 && !_kEv3Last) HandleEvidenceShortcut(3);
                if (kEv4 && !_kEv4Last) HandleEvidenceShortcut(4);
                if (kEv5 && !_kEv5Last) HandleEvidenceShortcut(5);
                if (kEv6 && !_kEv6Last) HandleEvidenceShortcut(6);
                if (kEv7 && !_kEv7Last) HandleEvidenceShortcut(7);

                if (pageUp) { BgBrush.Opacity = Math.Min(1.0, BgBrush.Opacity + 0.05); }
                if (pageDown) { BgBrush.Opacity = Math.Max(0.0, BgBrush.Opacity - 0.05); }
            }

            _k1Last = k1; _k2Last = k2; _k3Last = k3; _k4Last = k4;
            _k5Last = k5; _k6Last = k6; _k7Last = k7; _spaceLast = space; _homeLast = home;
            _kEvidenceLast = kEv; _kClearLast = kClr; _kToggleEvLast = kToggleEv;

            _kEv1Last = kEv1; _kEv2Last = kEv2; _kEv3Last = kEv3;
            _kEv4Last = kEv4; _kEv5Last = kEv5; _kEv6Last = kEv6; _kEv7Last = kEv7;

            if (IsCompactMode && LastSettingsPreviewTime != DateTime.MinValue)
            {
                if ((DateTime.Now - LastSettingsPreviewTime).TotalSeconds > 2.0)
                {
                    LastSettingsPreviewTime = DateTime.MinValue;
                    RefreshCompactModeVisuals();
                }
            }

            // Keep the shared evidence/ghosts headers in step with their content columns so
            // the single underline spans exactly the columns that are currently visible.
            if (HdrEvidence != null) HdrEvidence.Visibility = ModEvidence.Visibility;
            if (HdrGhosts != null) HdrGhosts.Visibility = ModGhosts.Visibility;

            UpdateOverlayInputState();
            UpdateTimers();
        }

        public void ToggleEvidenceWindow()
        {
            if (_evidenceWin == null)
            {
                _evidenceWin = new EvidenceWindow(this);
                _evidenceWin.Show();
            }
            else
            {
                if (_evidenceWin.Visibility == Visibility.Visible)
                {
                    _evidenceWin.Hide();
                }
                else
                {
                    _evidenceWin.Show();
                }
            }
        }

        public void ClearAll()
        {
            _isSmudgeActive = false;
            _isCooldownActive = false;
            _isHuntActive = false;
            _isObamboActive = false;

            SmudgeText.Text = "0:00";
            CooldownText.Text = "0:00";
            HuntText.Text = "0:00";
            ObamboText.Text = "0:00";

            SmudgeTitle.Foreground = GetBrush("#FFDDDDDD");
            CooldownTitle.Foreground = GetBrush("#FFDDDDDD");
            HuntTitle.Foreground = GetBrush("#FFDDDDDD");
            ObamboLabel.Foreground = GetBrush("#FFDDDDDD");
            SpeedLabel.Foreground = GetBrush("#FFDDDDDD");

            ResetPulse(SmudgeText);
            ResetPulse(CooldownText);
            ResetPulse(HuntText);

            ResetSpeedTap(false);
            _evidenceWin?.ResetTracker();

            IsBloodMoonActive = false;
            IsCursedHunt = false;

            SpeedText.Foreground = System.Windows.Media.Brushes.White;
            BloodMoonIcon.Fill = GetBrush("#FF2A2A2E");
            CursedIcon.Fill = GetBrush("#FF2A2A2E");

            UpdateSpeedDisplay();
            RefreshCompactModeVisuals(false, true);
            PlayAudio("alert");
        }

        private void UpdateSpeedDisplay()
        {
            if (SpeedLabel != null)
            {
                double effectiveSpeed = SpeedMultiplierSetting + (IsBloodMoonActive ? 0.15 : 0.0);
                SpeedLabel.Text = $"SPEED ({Math.Round(effectiveSpeed * 100)}%)";
            }
        }

        public void ToggleBloodMoon()
        {
            IsBloodMoonActive = !IsBloodMoonActive;

            PlayAudio(IsBloodMoonActive ? "moonenabled" : "moondisabled");

            if (IsBloodMoonActive)
            {
                SpeedLabel.Foreground = GetBrush("#FFFF5555");
                SpeedText.Foreground = GetBrush("#FFFF5555");
                BloodMoonIcon.Fill = GetBrush("#FFFF5555");
            }
            else
            {
                SpeedLabel.Foreground = _recentTaps.Count > 0 ? GetBrush("#FFB455FF") : GetBrush("#FFDDDDDD");
                SpeedText.Foreground = System.Windows.Media.Brushes.White;
                BloodMoonIcon.Fill = GetBrush("#FF2A2A2E");
            }

            UpdateSpeedDisplay();
            RefreshCompactModeVisuals(false, !IsBloodMoonActive);

            if (ModBloodMoon.Visibility != Visibility.Visible)
            {
                ShowNotification("Blood Moon: " + (IsBloodMoonActive ? "Enabled" : "Disabled"));
            }
        }

        public void ToggleCursedHunt()
        {
            IsCursedHunt = !IsCursedHunt;

            PlayAudio(IsCursedHunt ? "cursedenabled" : "curseddisabled");

            if (IsCursedHunt)
            {
                CursedIcon.Fill = GetBrush("#FFB455FF");
            }
            else
            {
                CursedIcon.Fill = GetBrush("#FF2A2A2E");
            }

            RefreshCompactModeVisuals(false, !IsCursedHunt);

            if (ModCursed.Visibility != Visibility.Visible)
            {
                ShowNotification("Cursed Hunt: " + (IsCursedHunt ? "Enabled" : "Disabled"));
            }
        }

        public void RecordSpeedTap()
        {
            DateTime now = DateTime.Now;

            if (_recentTaps.Count > 0 && (now - _recentTaps[_recentTaps.Count - 1]).TotalSeconds > 2.5)
            {
                _recentTaps.Clear();
            }

            _recentTaps.Add(now);

            if (!IsBloodMoonActive) SpeedLabel.Foreground = GetBrush("#FFB455FF");

            if (_recentTaps.Count > 1)
            {
                double totalSeconds = (now - _recentTaps[0]).TotalSeconds;
                int intervals = _recentTaps.Count - 1;
                double avgSecondsBetweenSteps = totalSeconds / intervals;

                double effectiveSpeedSetting = SpeedMultiplierSetting + (IsBloodMoonActive ? 0.15 : 0.0);

                double rawSpeed = 0.85 / avgSecondsBetweenSteps;
                double trueSpeed = rawSpeed / effectiveSpeedSetting;

                SpeedText.Text = trueSpeed.ToString("0.00");

                _evidenceWin?.SetHighlightedSpeed(trueSpeed);
            }
            else
            {
                SpeedText.Text = "...";
            }

            if (_recentTaps.Count > 6)
            {
                _recentTaps.RemoveAt(0);
            }

            RefreshCompactModeVisuals();
        }

        public void ResetSpeedTap(bool autoTimeout)
        {
            _recentTaps.Clear();
            SpeedText.Text = "-";

            if (!IsBloodMoonActive) SpeedLabel.Foreground = GetBrush("#FFDDDDDD");

            if (!autoTimeout)
            {
                _evidenceWin?.ClearSpeedHighlight();
            }

            RefreshCompactModeVisuals(false, !autoTimeout);
        }

        public void ToggleSmudge()
        {
            if (_isSmudgeActive)
            {
                _isSmudgeActive = false;
                SmudgeText.Text = "0:00";
                SmudgeTitle.Foreground = GetBrush("#FFDDDDDD");
                ResetPulse(SmudgeText);
                PlayAudio("alert");
                RefreshCompactModeVisuals(false, true);
            }
            else
            {
                _smudgeEndTime = DateTime.Now.AddSeconds(180);
                _smudgeDemon = false; _warnedDemon = false;
                _smudgeStandard = false; _warnedStandard = false;
                _smudgeSpirit = false; _warnedSpirit = false;
                _smudgeTick = 0;
                _isSmudgeActive = true;
                SmudgeTitle.Foreground = GetBrush("#FFB455FF");
                PlayAudio("alert");
                RefreshCompactModeVisuals();
            }
        }

        public void ToggleCooldown()
        {
            if (_isCooldownActive)
            {
                _isCooldownActive = false;
                CooldownText.Text = "0:00";
                CooldownTitle.Foreground = GetBrush("#FFDDDDDD");
                ResetPulse(CooldownText);
                PlayAudio("alert");
                RefreshCompactModeVisuals(false, true);
            }
            else
            {
                _cooldownEndTime = DateTime.Now.AddSeconds(25);
                _cdDemonWarned = false; _cdDemonAlert = false;
                _cdStandardWarned = false;
                _cooldownTick = 26;
                _isCooldownActive = true;
                CooldownTitle.Foreground = GetBrush("#FFB455FF");
                PlayAudio("alert");
                RefreshCompactModeVisuals();
            }
        }

        public void ToggleHunt()
        {
            if (_isHuntActive)
            {
                _isHuntActive = false;
                HuntText.Text = "0:00";
                HuntTitle.Foreground = GetBrush("#FFDDDDDD");
                ResetPulse(HuntText);
                PlayAudio("alert");
                RefreshCompactModeVisuals(false, true);
            }
            else
            {
                double duration = BaseHuntDuration + (IsCursedHunt ? 20 : 0);
                _huntEndTime = DateTime.Now.AddSeconds(duration);
                _huntWarned = false;
                _huntTick = (int)duration + 1;
                _isHuntActive = true;
                HuntTitle.Foreground = GetBrush("#FFB455FF");
                PlayAudio("alert");
                RefreshCompactModeVisuals();
            }
        }

        public void ToggleObambo()
        {
            if (_isObamboActive)
            {
                _isObamboActive = false;
                ObamboText.Text = "0:00";
                ObamboLabel.Text = "OBAMBO";
                ObamboLabel.Foreground = GetBrush("#FFDDDDDD");
                PlayAudio("alert");
                RefreshCompactModeVisuals(false, true);
            }
            else
            {
                _obamboStartTime = DateTime.Now;
                _obamboNextTargetTime = _obamboStartTime.AddSeconds(60);
                _obamboIsFast = false;
                _isObamboActive = true;
                ObamboLabel.Text = "OBAMBO (1.45m/s)";
                ObamboLabel.Foreground = GetBrush("#FFB455FF");
                PlayAudio("alert");
                RefreshCompactModeVisuals();
            }
        }

        private string FormatTime(double seconds, bool isCountdown = true)
        {
            double displaySeconds = isCountdown ? Math.Ceiling(Math.Max(0, seconds)) : Math.Max(0, seconds);
            return TimeSpan.FromSeconds(displaySeconds).ToString(@"m\:ss");
        }

        private void UpdateTimers()
        {
            DateTime now = DateTime.Now;

            if (_isSmudgeActive)
            {
                double remaining = (_smudgeEndTime - now).TotalSeconds;
                int currentSec = (int)Math.Ceiling(remaining);

                if (remaining <= 126.5 && !_warnedDemon) { _warnedDemon = true; PlayAudio("demonsmudge"); }
                if (currentSec <= 125 && currentSec > 120 && currentSec != _smudgeTick)
                {
                    PlayAudio((currentSec - 120).ToString());
                    _smudgeTick = currentSec;
                    TriggerPulse(SmudgeText);
                }
                if (remaining <= 120 && !_smudgeDemon)
                {
                    _smudgeDemon = true;
                    PlayAudio("alert");
                    ResetPulse(SmudgeText);
                }

                if (remaining <= 96.5 && !_warnedStandard) { _warnedStandard = true; PlayAudio("standardsmudge"); }
                if (currentSec <= 95 && currentSec > 90 && currentSec != _smudgeTick)
                {
                    PlayAudio((currentSec - 90).ToString());
                    _smudgeTick = currentSec;
                    TriggerPulse(SmudgeText);
                }
                if (remaining <= 90 && !_smudgeStandard)
                {
                    _smudgeStandard = true;
                    PlayAudio("alert");
                    ResetPulse(SmudgeText);
                }

                if (remaining <= 6.5 && !_warnedSpirit) { _warnedSpirit = true; PlayAudio("spiritsmudge"); }
                if (currentSec <= 5 && currentSec > 0 && currentSec != _smudgeTick)
                {
                    PlayAudio(currentSec.ToString());
                    _smudgeTick = currentSec;
                    TriggerPulse(SmudgeText);
                }
                if (remaining <= 0 && !_smudgeSpirit)
                {
                    _smudgeSpirit = true;
                    PlayAudio("alert");
                    _isSmudgeActive = false;
                    SmudgeTitle.Foreground = GetBrush("#FFDDDDDD");
                    RefreshCompactModeVisuals();
                    ResetPulse(SmudgeText);
                }

                if (_isSmudgeActive) { SmudgeText.Text = FormatTime(remaining); }
                else { SmudgeText.Text = "0:00"; }
            }

            if (_isCooldownActive)
            {
                double remaining = (_cooldownEndTime - now).TotalSeconds;
                int currentSec = (int)Math.Ceiling(remaining);

                if (remaining <= 11.5 && !_cdDemonWarned) { _cdDemonWarned = true; PlayAudio("demoncooldown"); }
                if (currentSec <= 10 && currentSec > 5 && currentSec != _cooldownTick)
                {
                    PlayAudio((currentSec - 5).ToString());
                    _cooldownTick = currentSec;
                    TriggerPulse(CooldownText);
                }
                if (remaining <= 5 && !_cdDemonAlert)
                {
                    _cdDemonAlert = true;
                    PlayAudio("alert");
                    ResetPulse(CooldownText);
                }

                if (remaining <= 6.5 && !_cdStandardWarned) { _cdStandardWarned = true; PlayAudio("standardcooldown"); }
                if (currentSec <= 5 && currentSec > 0 && currentSec != _cooldownTick)
                {
                    PlayAudio(currentSec.ToString());
                    _cooldownTick = currentSec;
                    TriggerPulse(CooldownText);
                }

                if (remaining <= 0)
                {
                    _isCooldownActive = false;
                    PlayAudio("alert");
                    CooldownTitle.Foreground = GetBrush("#FFDDDDDD");
                    CooldownText.Text = "0:00";
                    RefreshCompactModeVisuals();
                    ResetPulse(CooldownText);
                }
                else { CooldownText.Text = FormatTime(remaining); }
            }

            if (_isHuntActive)
            {
                double remaining = (_huntEndTime - now).TotalSeconds;
                int currentSec = (int)Math.Ceiling(remaining);

                if (remaining <= 6.5 && !_huntWarned) { _huntWarned = true; PlayAudio("huntending"); }
                if (currentSec <= 5 && currentSec > 0 && currentSec != _huntTick)
                {
                    PlayAudio(currentSec.ToString());
                    _huntTick = currentSec;
                    TriggerPulse(HuntText);
                }

                if (remaining <= 0)
                {
                    _isHuntActive = false;
                    PlayAudio("alert");
                    HuntTitle.Foreground = GetBrush("#FFDDDDDD");
                    HuntText.Text = "0:00";
                    RefreshCompactModeVisuals();
                    ResetPulse(HuntText);
                }
                else { HuntText.Text = FormatTime(remaining); }
            }

            if (_isObamboActive)
            {
                double elapsed = (now - _obamboStartTime).TotalSeconds;
                double remainingToShift = (_obamboNextTargetTime - now).TotalSeconds;

                if (remainingToShift <= 0)
                {
                    _obamboIsFast = !_obamboIsFast;
                    ObamboLabel.Text = _obamboIsFast ? "OBAMBO (1.96m/s)" : "OBAMBO (1.45m/s)";
                    ObamboLabel.Foreground = GetBrush(_obamboIsFast ? "#FFFF5555" : "#FFB455FF");
                    _obamboNextTargetTime = _obamboNextTargetTime.AddSeconds(120);
                    PlayAudio("alert");
                }

                ObamboText.Text = FormatTime(elapsed, false);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }
    }
}