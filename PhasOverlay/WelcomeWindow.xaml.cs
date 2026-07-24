using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Controls;

namespace PhasOverlay
{
    public partial class WelcomeWindow : Window
    {
        private MainWindow _main;
        private DispatcherTimer _inputTimer;
        private bool _isLoaded = false;

        private bool _k3Last = false;
        private bool _demoDone = false;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

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
            RefreshWeeklyComboItem();

            Difficulty_SelectionChanged(null, null);

            _ = RefreshWeeklyDataAsync();
        }

        private void UpdateSliderLabels()
        {
            if (ScaleValueLabel != null) ScaleValueLabel.Text = $"{Math.Round(SldScale.Value * 100)}%";
            if (OpacityValueLabel != null) OpacityValueLabel.Text = $"{Math.Round(SldOpacity.Value * 100)}%";
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
            // Step 1 -> Step 2 (The Basics)
            Step1.Visibility = Visibility.Collapsed;
            Step2.Visibility = Visibility.Visible;
            StepSubtitle.Text = "THE BASICS";
            TxtProgress.Text = "STEP 2 OF 3";
            TxtProgress.Visibility = Visibility.Collapsed;
            BtnContinue.Visibility = Visibility.Visible;
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            // Tidy up the demo hunt timer before leaving the teaching step.
            if (_main.ModHunt.Visibility == Visibility.Visible)
            {
                double originalVolume = _main.MasterVolume;
                _main.MasterVolume = 0;
                _main.ToggleHunt();
                _main.MasterVolume = originalVolume;
            }

            // Step 2 -> Step 3 (Your Setup)
            Step2.Visibility = Visibility.Collapsed;
            Step3.Visibility = Visibility.Visible;
            StepSubtitle.Text = "YOUR SETUP";
            TxtProgress.Visibility = Visibility.Collapsed;
            BtnContinue.Visibility = Visibility.Collapsed;
            BtnFinish.Visibility = Visibility.Visible;
        }

        private void InputTimer_Tick(object sender, EventArgs e)
        {
            bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool k3 = MainWindow.KeyHeld(_main.KeyHunt, shiftDown);

            // The single hands-on moment: pressing the Hunt key on the Basics step.
            if (Step2.Visibility == Visibility.Visible && k3 && !_k3Last && !_demoDone)
            {
                _demoDone = true;
                _main.ToggleHunt();

                TryItBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20B455FF"));
                TryItBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB455FF"));
                TryItText.Text = "Nice — it's counting down at the top of your screen.";
                TryItKey.Visibility = Visibility.Collapsed;
                TryItText2.Visibility = Visibility.Collapsed;
            }

            _k3Last = k3;
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
                int posIdx = CmbPosition.SelectedIndex >= 0 ? CmbPosition.SelectedIndex : 0;

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
            SaveInitialSettings();
            _inputTimer.Stop();
            this.Close();
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            // Make sure the demo hunt isn't left running.
            if (_main.ModHunt.Visibility == Visibility.Visible)
            {
                double originalVolume = _main.MasterVolume;
                _main.MasterVolume = 0;
                _main.ToggleHunt();
                _main.MasterVolume = originalVolume;
            }

            SaveInitialSettings();
            _inputTimer.Stop();
            this.Close();
        }
    }
}
