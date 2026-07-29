using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhasOverlay
{
    public partial class WelcomeWindow : Window
    {
        private MainWindow _main;
        private DispatcherTimer _inputTimer;
        private bool _isLoaded = false;

        private class TeachStep
        {
            public string Subtitle = "";
            public string Title = "";
            public string Body = "";
            public string Hint = "";
            public string HintTail = "";
            public string Note = "";
            public int PressesEach = 1;
            public string CountLabel = "presses";
            public bool PreserveDemoForNextStep = false;
            public bool PlaysTutorialFootsteps = false;
            public bool StartsEvidenceTutorial = false;
            public string FollowUpHint = "";
            public string FollowUpHintTail = "";
            public (Func<MainWindow, int> Key, Action<MainWindow> Act)? FollowUp = null;
            public (Func<MainWindow, int> Key, Action<MainWindow> Act)[] Inputs =
                Array.Empty<(Func<MainWindow, int>, Action<MainWindow>)>();
        }

        private static readonly TeachStep[] Steps =
        {
            new TeachStep
            {
                Subtitle = "SMUDGE TIMER",
                Title = "Smudge Timer",
                Body = "Using incense on a ghost before it hunts prevents the ghost from hunting for a set duration. Start this timer as you smudge to track this duration.",
                Hint = "Press", HintTail = "to start a Smudge timer",
                PreserveDemoForNextStep = true,
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[] { (m => m.KeySmudge, m => m.ToggleSmudge()) }
            },
            new TeachStep
            {
                Subtitle = "CANCELLING",
                Title = "Cancel A Timer",
                Body = "Timers vanish on their own when they reach zero, but you are able to prematurely cancel a timer by pressing the same key as the one that activated it.",
                Note = "Timers only show while they are running, so the overlay stays out of your way until you need it.",
                Hint = "Press", HintTail = "again to cancel it",
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[] { (m => m.KeySmudge, m => m.ToggleSmudge()) }
            },
            new TeachStep
            {
                Subtitle = "COOLDOWN",
                Title = "Track Grace Periods",
                Body = "After a hunt ends or after the ghost burns a crucifix, there's a window where the ghost is unable to hunt. Use this cooldown timer to keep track of how much time remains until the ghost can hunt again.",
                Hint = "Press", HintTail = "to track a cooldown",
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[] { (m => m.KeyCooldown, m => m.ToggleCooldown()) }
            },
            new TeachStep
            {
                Subtitle = "HUNT TIMER",
                Title = "Track A Hunt",
                Body = "Start this timer when the ghost begins hunting. It tells you exactly how much time you have left in the hunt. This helps you remain safe or simply know when a hunt is over.",
                Hint = "Press", HintTail = "to start a Hunt timer",
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[] { (m => m.KeyHunt, m => m.ToggleHunt()) }
            },
            new TeachStep
            {
                Subtitle = "OBAMBO",
                Title = "Keep Track of Obambos",
                Body = "As soon as you open the front door, start this timer. It allows you to keep track of an Obambo's state, helping you identify an Obambo later if the ghost happens to be one.",
                Hint = "Press", HintTail = "to track it",
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[] { (m => m.KeyObambo, m => m.ToggleObambo()) }
            },
            new TeachStep
            {
                Subtitle = "GHOST SPEED",
                Title = "Identify Ghost Speeds",
                Body = "Use tap to speed to help identify the speed of a ghost during a hunt, allowing you to narrow down the possible ghost list even further.",
                Hint = "Tap", HintTail = "in rhythm, ten times",
                PressesEach = 10,
                CountLabel = "taps",
                PlaysTutorialFootsteps = true,
                FollowUpHint = "Reading locked. Press",
                FollowUpHintTail = "to clear the speed",
                FollowUp = (m => m.KeySpeedReset, m => m.ResetSpeedTap(false)),
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[] { (m => m.KeySpeedTap, m => m.RecordSpeedTap()) }
            },
            new TeachStep
            {
                Subtitle = "MODIFIERS",
                Title = "Environmental Modifiers",
                Body = "A Blood Moon makes the ghost faster while a cursed hunt increases the duration of a hunt. Toggle these modifiers to make sure the overlay keeps track of them.",
                Hint = "Press", HintTail = "to toggle both",
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[]
                {
                    (m => m.KeyBloodMoon, m => m.ToggleBloodMoon()),
                    (m => m.KeyCursedHunt, m => m.ToggleCursedHunt())
                }
            },
            new TeachStep
            {
                Subtitle = "EVIDENCE",
                Title = "Learn The Evidence Tracker",
                Body = "The evidence tracker narrows down possible ghosts using evidence, hunt behaviour and speed. Open it for a short guided walkthrough of the basics.",
                Hint = "Press", HintTail = "to begin the walkthrough",
                StartsEvidenceTutorial = true,
                Inputs = new (Func<MainWindow, int>, Action<MainWindow>)[] { (m => m.KeyEvidence, m => { }) }
            }
        };

        private int _stepIndex = -1;              // -1 = welcome screen
        private int[] _pressCounts = Array.Empty<int>();
        private bool[] _keyLast = Array.Empty<bool>();
        private bool _stepSatisfied = false;
        private bool _awaitingFollowUp = false;
        private bool _followUpKeyLast = false;
        private bool _evidenceTutorialRunning = false;
        private bool _capturingSettingsKey = false;
        private CancellationTokenSource? _tutorialFootstepCancel;
        private bool _tutorialFootstepLoaded = false;
        private const string TutorialFootstepAlias = "welcome_footstep";

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern long mciSendString(string command, string? returnValue, int returnLength, IntPtr winHandle);

        public WelcomeWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            _inputTimer = new DispatcherTimer();
            _inputTimer.Interval = TimeSpan.FromMilliseconds(50);
            _inputTimer.Tick += InputTimer_Tick;
            _inputTimer.Start();

            _isLoaded = true;
            UpdateSliderLabels();
            RefreshSetupSettingsKey();
            RefreshWeeklyComboItem();

            Difficulty_SelectionChanged(null, null);

            _ = RefreshWeeklyDataAsync();

            this.Loaded += (s, e) => DisplayService.CenterOn(this, _main.DisplayIndex);
            this.Closed += (s, e) =>
            {
                _inputTimer.Stop();
                CloseTutorialFootsteps();
            };
        }

        private void UpdateSliderLabels()
        {
            if (ScaleValueLabel != null) ScaleValueLabel.Text = $"{Math.Round(SldScale.Value * 100)}%";
            if (OpacityValueLabel != null) OpacityValueLabel.Text = $"{Math.Round(SldOpacity.Value * 100)}%";
        }

        private void RefreshSetupSettingsKey()
        {
            BtnSetupSettingsKey.Content = $"[ {MainWindow.FormatKeyName(_main.KeySettings)} ]  CHANGE KEY";
            BtnSetupSettingsKey.ClearValue(Control.ForegroundProperty);
        }

        private void SetupSettingsKey_Click(object sender, RoutedEventArgs e)
        {
            _capturingSettingsKey = true;
            BtnSetupSettingsKey.Content = "[ PRESS ANY KEY ]";
            BtnSetupSettingsKey.Foreground = GetBrush("#FFB455FF");
            BtnSetupSettingsKey.Focus();
        }

        private void SetupShortcut_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturingSettingsKey) return;

            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                _capturingSettingsKey = false;
                RefreshSetupSettingsKey();
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt || key == Key.System)
            {
                return;
            }

            int newKey = KeyInterop.VirtualKeyFromKey(key);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) newKey |= MainWindow.ShiftFlag;

            _main.SyncKeybind("Settings", newKey);
            _capturingSettingsKey = false;
            RefreshSetupSettingsKey();
        }

        /// <summary>Shows the Weekly combo item with its label only when a weekly is cached.</summary>
        private void RefreshWeeklyComboItem()
        {
            var w = WeeklyDataService.GetWeekly();
            if (w != null)
            {
                ItemWeekly.Content = w.Label;
                ItemWeekly.ToolTip = w.Tooltip;
                ItemWeekly.Visibility = Visibility.Visible;
            }
            else
            {
                ItemWeekly.Content = "Weekly";
                ItemWeekly.ToolTip = null;
                ItemWeekly.Visibility = Visibility.Collapsed;
            }
        }

        private async Task RefreshWeeklyDataAsync()
        {
            bool changed = await WeeklyDataService.CheckForUpdatesAsync();
            if (!changed) return;

            Dispatcher.Invoke(() =>
            {
                RefreshWeeklyComboItem();
                if (CmbDifficulty.SelectedIndex == MainWindow.DiffWeekly)
                    Difficulty_SelectionChanged(null, null);
            });
        }

        // ------------------------------------------------------------------
        //  Step navigation
        // ------------------------------------------------------------------
        private void NextStep_Click(object sender, RoutedEventArgs e)
        {
            Step1.Visibility = Visibility.Collapsed;
            StepTeach.Visibility = Visibility.Visible;
            TxtProgress.Visibility = Visibility.Collapsed;

            ShowStep(0);
        }

        private void MoveToNextStep()
        {
            if (_stepIndex + 1 < Steps.Length)
            {
                ShowStep(_stepIndex + 1);
                return;
            }

            CleanUpDemo();

            StepTeach.Visibility = Visibility.Collapsed;
            Step3.Visibility = Visibility.Visible;
            StepSubtitle.Text = "YOUR SETUP";
            BtnFinish.Visibility = Visibility.Visible;
        }

        private void ShowStep(int index)
        {
            StopTutorialFootsteps();

            _stepIndex = index;
            _stepSatisfied = false;
            _awaitingFollowUp = false;
            _followUpKeyLast = false;

            TeachStep s = Steps[index];
            _pressCounts = new int[s.Inputs.Length];
            _keyLast = new bool[s.Inputs.Length];
            bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            for (int i = 0; i < s.Inputs.Length; i++)
            {
                _keyLast[i] = MainWindow.KeyHeld(s.Inputs[i].Key(_main), shiftDown);
            }

            StepSubtitle.Text = s.Subtitle;
            TeachTitle.Text = s.Title;
            TeachBody.Text = s.Body;

            TeachNote.Text = s.Note;
            TeachNoteBox.Visibility = string.IsNullOrEmpty(s.Note) ? Visibility.Collapsed : Visibility.Visible;

            TeachHint.Text = s.Hint;
            TeachHintTail.Text = s.HintTail;
            TeachHint.Visibility = Visibility.Visible;
            TeachHintTail.Visibility = Visibility.Visible;

            TeachKeyText.Text = MainWindow.FormatKeyName(s.Inputs[0].Key(_main));
            TeachKeyCap.Visibility = Visibility.Visible;

            bool twoKeys = s.Inputs.Length > 1;
            if (twoKeys) TeachKeyText2.Text = MainWindow.FormatKeyName(s.Inputs[1].Key(_main));
            TeachKeyCap2.Visibility = twoKeys ? Visibility.Visible : Visibility.Collapsed;
            ResetKeyCap(TeachKeyCap, TeachKeyText);
            ResetKeyCap(TeachKeyCap2, TeachKeyText2);

            TeachBox.Background = GetBrush("#14B455FF");
            TeachBox.BorderBrush = GetBrush("#40B455FF");

            RefreshProgressHint();

            if (s.PlaysTutorialFootsteps) StartTutorialFootsteps();
        }

        private void RefreshProgressHint()
        {
            TeachStep s = Steps[_stepIndex];
            bool counted = s.PressesEach > 1;

            if (!counted || _stepSatisfied)
            {
                TeachProgressHint.Visibility = Visibility.Collapsed;
                return;
            }

            TeachProgressHint.Text = $"{_pressCounts[0]} / {s.PressesEach} {s.CountLabel}";
            TeachProgressHint.Visibility = Visibility.Visible;
        }

        private void BeginFollowUp(TeachStep s, bool shiftDown)
        {
            if (!s.FollowUp.HasValue) return;

            _awaitingFollowUp = true;
            StopTutorialFootsteps();

            var followUp = s.FollowUp.Value;
            _followUpKeyLast = MainWindow.KeyHeld(followUp.Key(_main), shiftDown);

            TeachBox.Background = GetBrush("#20B455FF");
            TeachBox.BorderBrush = GetBrush("#FFB455FF");
            ResetKeyCap(TeachKeyCap, TeachKeyText);
            ResetKeyCap(TeachKeyCap2, TeachKeyText2);
            TeachHint.Text = s.FollowUpHint;
            TeachHintTail.Text = s.FollowUpHintTail;
            TeachHintTail.Visibility = Visibility.Visible;
            TeachKeyText.Text = MainWindow.FormatKeyName(followUp.Key(_main));
            TeachKeyCap.Visibility = Visibility.Visible;
            TeachKeyCap2.Visibility = Visibility.Collapsed;
            TeachProgressHint.Visibility = Visibility.Collapsed;
        }

        private async void MarkStepSatisfied()
        {
            _stepSatisfied = true;
            int completedStep = _stepIndex;

            TeachStep s = Steps[_stepIndex];
            TeachBox.Background = GetBrush("#20B455FF");
            TeachBox.BorderBrush = GetBrush("#FFB455FF");
            if (_awaitingFollowUp)
            {
                MarkKeyCapPressed(TeachKeyCap, TeachKeyText);
            }
            else
            {
                MarkKeyCapPressed(TeachKeyCap, TeachKeyText);
                if (s.Inputs.Length > 1) MarkKeyCapPressed(TeachKeyCap2, TeachKeyText2);
            }

            RefreshProgressHint();

            await Task.Delay(TimeSpan.FromSeconds(1.5));
            if (!IsLoaded || _stepIndex != completedStep) return;

            if (!s.PreserveDemoForNextStep) CleanUpDemo();
            MoveToNextStep();
        }

        private static void ResetKeyCap(Border keyCap, TextBlock keyText)
        {
            keyCap.ClearValue(Border.BackgroundProperty);
            keyCap.ClearValue(Border.BorderBrushProperty);
            keyText.ClearValue(TextBlock.ForegroundProperty);
        }

        private static void MarkKeyCapPressed(Border keyCap, TextBlock keyText)
        {
            keyCap.Background = GetBrush("#FFB455FF");
            keyCap.BorderBrush = GetBrush("#FFD6A0FF");
            keyText.Foreground = GetBrush("#FF151515");
        }

        private static Brush GetBrush(string hex)
        {
            Brush b = (Brush)(new BrushConverter().ConvertFromString(hex)
                ?? throw new InvalidOperationException($"Invalid brush colour: {hex}"));
            b.Freeze();
            return b;
        }

        private void InputTimer_Tick(object sender, EventArgs e)
        {
            if (StepTeach.Visibility != Visibility.Visible || _stepIndex < 0) return;

            bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            TeachStep s = Steps[_stepIndex];

            if (_stepSatisfied) return;

            if (_awaitingFollowUp && s.FollowUp.HasValue)
            {
                var followUp = s.FollowUp.Value;
                bool down = MainWindow.KeyHeld(followUp.Key(_main), shiftDown);
                if (down && !_followUpKeyLast)
                {
                    followUp.Act(_main);
                    _followUpKeyLast = down;
                    MarkStepSatisfied();
                    return;
                }

                _followUpKeyLast = down;
                return;
            }

            for (int i = 0; i < s.Inputs.Length; i++)
            {
                // KeyHeld, not raw GetAsyncKeyState: a binding can carry ShiftFlag in its high bits.
                bool down = MainWindow.KeyHeld(s.Inputs[i].Key(_main), shiftDown);

                if (down && !_keyLast[i] && _pressCounts[i] < s.PressesEach)
                {
                    _pressCounts[i]++;
                    if (_pressCounts[i] == s.PressesEach)
                    {
                        MarkKeyCapPressed(
                            i == 0 ? TeachKeyCap : TeachKeyCap2,
                            i == 0 ? TeachKeyText : TeachKeyText2);
                    }

                    if (s.StartsEvidenceTutorial)
                    {
                        _keyLast[i] = down;
                        StartEvidenceTutorial();
                        return;
                    }

                    s.Inputs[i].Act(_main);
                    RefreshProgressHint();
                }
                _keyLast[i] = down;
            }

            foreach (int c in _pressCounts)
            {
                if (c < s.PressesEach) return;
            }

            if (s.FollowUp.HasValue)
            {
                BeginFollowUp(s, shiftDown);
                return;
            }

            MarkStepSatisfied();
        }

        private void StartEvidenceTutorial()
        {
            if (_evidenceTutorialRunning) return;

            _evidenceTutorialRunning = true;
            _inputTimer.Stop();
            Opacity = 0;
            MoveToNextStep();
            UpdateLayout();
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            Hide();

            _main.StartEvidenceTutorial(completed =>
            {
                Dispatcher.Invoke(() =>
                {
                    _evidenceTutorialRunning = false;

                    if (!completed)
                    {
                        Step3.Visibility = Visibility.Collapsed;
                        BtnFinish.Visibility = Visibility.Collapsed;
                        StepTeach.Visibility = Visibility.Visible;
                        ShowStep(_stepIndex);
                    }

                    Show();
                    UpdateLayout();
                    Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
                    Opacity = 1;
                    Activate();
                    _inputTimer.Start();
                });
            });
        }

        private bool EnsureTutorialFootstep()
        {
            if (_tutorialFootstepLoaded) return true;

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "footstep.mp3");
            if (!File.Exists(filePath))
                filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "footstep.mp3");
            if (!File.Exists(filePath)) return false;

            long result = mciSendString($"open \"{filePath}\" type mpegvideo alias {TutorialFootstepAlias}", null, 0, IntPtr.Zero);
            _tutorialFootstepLoaded = result == 0;
            return _tutorialFootstepLoaded;
        }

        private void StartTutorialFootsteps()
        {
            StopTutorialFootsteps();
            if (!EnsureTutorialFootstep()) return;

            _tutorialFootstepCancel = new CancellationTokenSource();
            CancellationToken token = _tutorialFootstepCancel.Token;
            TimeSpan interval = TimeSpan.FromMilliseconds((1000.0 / 1.7) - 75.0);

            FireTutorialFootstep();

            Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(interval);
                    while (await timer.WaitForNextTickAsync(token))
                    {
                        _ = Dispatcher.InvokeAsync(() =>
                        {
                            if (!token.IsCancellationRequested) FireTutorialFootstep();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        private static void FireTutorialFootstep()
        {
            mciSendString($"play {TutorialFootstepAlias} from 0", null, 0, IntPtr.Zero);
        }

        private void StopTutorialFootsteps()
        {
            if (_tutorialFootstepCancel == null) return;

            _tutorialFootstepCancel.Cancel();
            _tutorialFootstepCancel.Dispose();
            _tutorialFootstepCancel = null;
        }

        private void CloseTutorialFootsteps()
        {
            StopTutorialFootsteps();
            if (!_tutorialFootstepLoaded) return;

            mciSendString($"close {TutorialFootstepAlias}", null, 0, IntPtr.Zero);
            _tutorialFootstepLoaded = false;
        }

        /// <summary>Clears anything the walkthrough started so the real session begins clean.</summary>
        private void CleanUpDemo()
        {
            StopTutorialFootsteps();

            double originalVolume = _main.MasterVolume;
            _main.MasterVolume = 0;
            try
            {
                if (_main.IsEvidenceWindowOpen) _main.ToggleEvidenceWindow();
                _main.ClearAll();
            }
            catch { }
            _main.MasterVolume = originalVolume;
        }

        // ------------------------------------------------------------------
        //  Preferences (drive the initial settings)
        // ------------------------------------------------------------------
        private void Difficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ColDifficulty == null) return;

            if (CmbDifficulty.SelectedIndex == MainWindow.DiffCustom)
            {
                ColDifficulty.Width = new GridLength(1, GridUnitType.Star);
                ColCustomGap.Width = new GridLength(10);
                ColCustomDur.Width = new GridLength(1, GridUnitType.Star);
                PanelCustomDuration.Visibility = Visibility.Visible;
                if (CmbCustomDuration.SelectedIndex == -1) CmbCustomDuration.SelectedIndex = 1;
            }
            else
            {
                ColDifficulty.Width = new GridLength(1, GridUnitType.Star);
                ColCustomGap.Width = new GridLength(0);
                ColCustomDur.Width = new GridLength(0);
                if (PanelCustomDuration != null) PanelCustomDuration.Visibility = Visibility.Collapsed;
            }

            if (CmbDifficulty.SelectedIndex == MainWindow.DiffWeekly)
            {
                var w = WeeklyDataService.GetWeekly();
                if (w != null) _main.ActiveWeekly = w;
                else { CmbDifficulty.SelectedIndex = 4; return; }
            }
            else
            {
                _main.ActiveWeekly = null;
            }

            ApplyDifficultyLocks();
            Setup_SelectionChanged(null, null);
        }

        /// <summary>Speed is editable only on Custom; map is locked only on Weekly.</summary>
        private void ApplyDifficultyLocks()
        {
            if (CmbSpeed == null || CmbMap == null) return;

            bool custom = CmbDifficulty.SelectedIndex == MainWindow.DiffCustom;
            bool weekly = CmbDifficulty.SelectedIndex == MainWindow.DiffWeekly;

            CmbSpeed.IsEnabled = custom;
            CmbSpeed.Opacity = custom ? 1.0 : 0.5;

            CmbMap.IsEnabled = !weekly;
            CmbMap.Opacity = weekly ? 0.5 : 1.0;

            if (weekly && _main.ActiveWeekly != null)
            {
                CmbMap.SelectedIndex = Math.Clamp(_main.ActiveWeekly.MapSizeIndex, 0, 2);
                int si = WeeklyDataService.SpeedToIndex(_main.ActiveWeekly.GhostSpeed);
                CmbSpeed.SelectedIndex = si >= 0 ? si : 2;
            }
            else if (!custom && CmbSpeed.SelectedIndex != 2)
            {
                CmbSpeed.SelectedIndex = 2;
            }
        }

        private int GetResolvedDurationIndex()
        {
            int diffIdx = CmbDifficulty.SelectedIndex;
            if (diffIdx == 0) return 0;
            if (diffIdx == 1) return 1;
            if (diffIdx >= 2 && diffIdx <= 4) return 2;
            if (diffIdx == MainWindow.DiffWeekly) return _main.ActiveWeekly?.HuntTier ?? 2;
            if (diffIdx == MainWindow.DiffCustom) return CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1;
            return 1;
        }

        private void Setup_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _main == null) return;

            if (CmbDifficulty.SelectedIndex == MainWindow.DiffWeekly && _main.ActiveWeekly != null)
            {
                _main.ApplyWeekly(_main.ActiveWeekly);
                return;
            }

            double[,] huntTimes = new double[,] {
                { 15.0, 30.0, 40.0 },
                { 20.0, 40.0, 50.0 },
                { 30.0, 50.0, 60.0 }
            };

            int mapIdx = CmbMap.SelectedIndex >= 0 ? CmbMap.SelectedIndex : 0;
            int durIdx = GetResolvedDurationIndex();

            _main.BaseHuntDuration = huntTimes[durIdx, mapIdx];
            _main.MapSizeIndex = mapIdx;
            _main.DifficultyIndex = CmbDifficulty.SelectedIndex >= 0 ? CmbDifficulty.SelectedIndex : 1;
            _main.CustomDurationIndex = CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1;

            if (CmbSpeed.SelectedIndex == 0) _main.SpeedMultiplierSetting = 0.5;
            else if (CmbSpeed.SelectedIndex == 1) _main.SpeedMultiplierSetting = 0.75;
            else if (CmbSpeed.SelectedIndex == 2) _main.SpeedMultiplierSetting = 1.0;
            else if (CmbSpeed.SelectedIndex == 3) _main.SpeedMultiplierSetting = 1.25;
            else if (CmbSpeed.SelectedIndex == 4) _main.SpeedMultiplierSetting = 1.5;
        }

        private void SldScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _main == null) return;
            _main.OverlayScale.ScaleX = SldScale.Value;
            _main.OverlayScale.ScaleY = SldScale.Value;

            UpdateSliderLabels();
            _main.LastSettingsPreviewTime = DateTime.Now;
            _main.RefreshCompactModeVisuals(true);
        }

        private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _main == null) return;
            _main.BgBrush.Opacity = SldOpacity.Value;

            UpdateSliderLabels();
            _main.LastSettingsPreviewTime = DateTime.Now;
            _main.RefreshCompactModeVisuals(true);
        }

        private void SldPosition_ValueChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _main == null) return;

            if (CmbPosition.SelectedIndex >= 0)
            {
                _main.OverlayPosition = CmbPosition.SelectedIndex;
                _main.UpdateWindowPosition();
            }

            _main.LastSettingsPreviewTime = DateTime.Now;
            _main.RefreshCompactModeVisuals(true);
        }

        private void PersistentMode_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _main == null) return;
            _main.IsCompactMode = ChkPersistentOverlay.IsChecked == false;

            _main.LastSettingsPreviewTime = DateTime.Now;
            _main.RefreshCompactModeVisuals(true);
        }

        // ------------------------------------------------------------------
        //  Persistence
        // ------------------------------------------------------------------
        private void SaveInitialSettings()
        {
            try
            {
                string statesStr = "";
                for (int i = 0; i < 7; i++) statesStr += _main.ModStates[i] ? "1" : "0";

                int finalDurIdx = GetResolvedDurationIndex();
                int diffIdx = CmbDifficulty.SelectedIndex >= 0 ? CmbDifficulty.SelectedIndex : 1;
                int customDurIdx = CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1;
                int posIdx = CmbPosition.SelectedIndex >= 0 ? CmbPosition.SelectedIndex : 1;

                string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay");
                Directory.CreateDirectory(appDataFolder);
                string configPath = Path.Combine(appDataFolder, "settings.txt");

                string[] lines = {
                    "[Game Settings]",
                    "SettingsVersion=2",
                    $"MapSize={CmbMap.SelectedIndex}",
                    $"Difficulty={diffIdx}",
                    $"CustomDuration={customDurIdx}",
                    $"HuntDuration={finalDurIdx}",
                    $"GhostSpeed={CmbSpeed.SelectedIndex}",
                    $"EvidenceLimit={_main.EvidenceLimit}",
                    "",
                    "[Overlay Display]",
                    $"Position={posIdx}",
                    $"Opacity={SldOpacity.Value}",
                    $"Scale={SldScale.Value}",
                    $"CompactMode={(_main.IsCompactMode ? 1 : 0)}",
                    $"ModulesActive={statesStr}",
                    "",
                    "[Audio]",
                    $"Volume={_main.MasterVolume}",

                    "",
                    "[Keybinds]",
                    $"KeySmudge={_main.KeySmudge}",
                    $"KeyCooldown={_main.KeyCooldown}",
                    $"KeyHunt={_main.KeyHunt}",
                    $"KeyObambo={_main.KeyObambo}",
                    $"KeySpeedReset={_main.KeySpeedReset}",
                    $"KeyBloodMoon={_main.KeyBloodMoon}",
                    $"KeyCursedHunt={_main.KeyCursedHunt}",
                    $"KeySpeedTap={_main.KeySpeedTap}",
                    $"KeySettings={_main.KeySettings}",
                    $"KeyEvidence={_main.KeyEvidence}",
                    $"KeyClear={_main.KeyClear}"
                };

                File.WriteAllLines(configPath, lines);

                // Legacy positional format; the retired KeyMap slot stays as a literal 0 to keep
                // field indices (posIdx at settings[21]) stable for old configs on this format.
                string saveString = $"{CmbMap.SelectedIndex}|{CmbSpeed.SelectedIndex}|{SldOpacity.Value}|{SldScale.Value}|{finalDurIdx}|{statesStr}|{_main.MasterVolume}|{_main.KeySmudge}|{_main.KeyCooldown}|{_main.KeyHunt}|{_main.KeyObambo}|{_main.KeySpeedReset}|{_main.KeyBloodMoon}|{_main.KeyCursedHunt}|{_main.KeySpeedTap}|{_main.KeySettings}|{diffIdx}|{customDurIdx}|{_main.KeyEvidence}|{_main.KeyClear}|0|{posIdx}";
                string fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
                try { File.WriteAllText(fallbackPath, saveString); } catch { }
            }
            catch { }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            CleanUpDemo();
            SaveInitialSettings();
            _inputTimer.Stop();
            this.Close();
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            CleanUpDemo();
            SaveInitialSettings();
            _inputTimer.Stop();
            this.Close();
        }
    }
}
