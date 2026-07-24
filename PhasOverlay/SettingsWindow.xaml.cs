using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhasOverlay
{
    public partial class SettingsWindow : Window
    {
        private MainWindow _overlay;
        private string _configPath;
        private bool _isFirstRun;
        private bool _isLoaded = false;

        private Button _activeBindButton = null;

        public SettingsWindow(MainWindow overlay, bool isFirstRun)
        {
            InitializeComponent();
            this.Topmost = true;

            _overlay = overlay;
            _isFirstRun = isFirstRun;

            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            VersionLabel.Text = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "";

            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay");
            _configPath = Path.Combine(appDataFolder, "settings.txt");

            if (_overlay.SpeedMultiplierSetting == 0.5) SpeedCombo.SelectedIndex = 0;
            else if (_overlay.SpeedMultiplierSetting == 0.75) SpeedCombo.SelectedIndex = 1;
            else if (_overlay.SpeedMultiplierSetting == 1.0) SpeedCombo.SelectedIndex = 2;
            else if (_overlay.SpeedMultiplierSetting == 1.25) SpeedCombo.SelectedIndex = 3;
            else if (_overlay.SpeedMultiplierSetting == 1.5) SpeedCombo.SelectedIndex = 4;
            else SpeedCombo.SelectedIndex = 2;

            CmbPosition.SelectedIndex = _overlay.OverlayPosition;
            OpacitySlider.Value = _overlay.BgBrush.Opacity;
            ScaleSlider.Value = _overlay.OverlayScale.ScaleX;

            VolumeSlider.Value = _overlay.MasterVolume;

            ChkPersistentOverlay.IsChecked = !_overlay.IsCompactMode;
            ChkAlwaysShowEvidence.IsChecked = _overlay.AlwaysShowEvidence;
            ChkAlwaysShowEvidence.IsEnabled = _overlay.IsCompactMode;

            UpdateVolumeLabel();
            UpdateSliderLabels();

            TglSmudge.IsChecked = _overlay.ModStates[0];
            TglCooldown.IsChecked = _overlay.ModStates[1];
            TglHunt.IsChecked = _overlay.ModStates[2];
            TglObambo.IsChecked = _overlay.ModStates[3];
            TglSpeed.IsChecked = _overlay.ModStates[4];
            TglBloodMoon.IsChecked = _overlay.ModStates[5];
            TglCursed.IsChecked = _overlay.ModStates[6];
            TglEvidence.IsChecked = _overlay.ModStates[7];
            TglGhosts.IsChecked = _overlay.ModStates[8];

            RefreshBindVisuals();

            // Seed the match combos from the overlay's already-loaded (and migrated) values.
            RefreshWeeklyComboItem();
            MapCombo.SelectedIndex = Math.Clamp(_overlay.MapSizeIndex, 0, 2);
            CmbCustomDuration.SelectedIndex = Math.Clamp(_overlay.CustomDurationIndex, 0, 2);
            CmbDifficulty.SelectedIndex = Math.Clamp(_overlay.DifficultyIndex, 0, MainWindow.DiffCustom);

            _isLoaded = true;
            Difficulty_SelectionChanged(null, null);

            _ = RefreshWeeklyDataAsync();
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void UpdateVolumeLabel()
        {
            if (VolumeLabel != null)
            {
                VolumeLabel.Text = $"Volume ({Math.Round(VolumeSlider.Value * 100)}%)";
            }
        }

        private void UpdateSliderLabels()
        {
            if (ScaleValueLabel != null) ScaleValueLabel.Text = $"{Math.Round(ScaleSlider.Value * 100)}%";
            if (OpacityValueLabel != null) OpacityValueLabel.Text = $"{Math.Round(OpacitySlider.Value * 100)}%";
        }

        private static readonly string[] HuntTierNames = { "Low", "Med", "High" };

        /// <summary>Shows the resolved hunt-length tier for a preset; Custom uses its own combo.</summary>
        private void UpdateHuntTierLabel()
        {
            if (HuntTierLabel == null) return;

            if (CmbDifficulty.SelectedIndex == MainWindow.DiffCustom) // Custom — the HUNT DURATION combo shows it
            {
                HuntTierLabel.Visibility = Visibility.Collapsed;
                return;
            }

            HuntTierLabel.Visibility = Visibility.Visible;
            HuntTierLabel.Text = $"Hunt duration: {HuntTierNames[Math.Clamp(GetResolvedDurationIndex(), 0, 2)]}";
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (MainHotkeysGrid == null || EvidenceHotkeysGrid == null) return;

            if (TabMain.IsChecked == true)
            {
                MainHotkeysGrid.Visibility = Visibility.Visible;
                EvidenceHotkeysGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                MainHotkeysGrid.Visibility = Visibility.Collapsed;
                EvidenceHotkeysGrid.Visibility = Visibility.Visible;
            }
        }

        private void OpenHotkeys_Click(object sender, RoutedEventArgs e)
        {
            HotkeysModalOverlay.Visibility = Visibility.Visible;
        }

        private void CloseHotkeys_Click(object sender, RoutedEventArgs e)
        {
            if (_activeBindButton != null)
            {
                _activeBindButton.Foreground = (Brush)new BrushConverter().ConvertFromString("#CCCCCC");
                _activeBindButton = null;
                RefreshBindVisuals();
            }
            HotkeysModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void HotkeysModalOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseHotkeys_Click(sender, e);
        }

        private void HotkeysModalContent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void RefreshBindVisuals()
        {
            BtnBindSettings.Content = $"[ {FormatKeyName(_overlay.KeySettings)} ]  Settings";
            BtnBindEvidence.Content = $"[ {FormatKeyName(_overlay.KeyEvidence)} ]  Evidence Window";

            BtnBindClear.Content = $"[ {FormatKeyName(_overlay.KeyClear)} ]  Reset UI";

            BtnBindSmudge.Content = $"[ {FormatKeyName(_overlay.KeySmudge)} ]  Smudge";
            BtnBindCooldown.Content = $"[ {FormatKeyName(_overlay.KeyCooldown)} ]  Cooldown";

            BtnBindHunt.Content = $"[ {FormatKeyName(_overlay.KeyHunt)} ]  Hunt";
            BtnBindObambo.Content = $"[ {FormatKeyName(_overlay.KeyObambo)} ]  Obambo";

            BtnBindSpeedReset.Content = $"[ {FormatKeyName(_overlay.KeySpeedReset)} ]  Reset Speed";
            BtnBindBloodMoon.Content = $"[ {FormatKeyName(_overlay.KeyBloodMoon)} ]  Blood Moon";

            BtnBindCursedHunt.Content = $"[ {FormatKeyName(_overlay.KeyCursedHunt)} ]  Cursed Hunt";
            BtnBindSpeedTap.Content = $"[ {FormatKeyName(_overlay.KeySpeedTap)} ]  Tap Speed";

            BtnBindToggleEv.Content = $"[ {FormatKeyName(_overlay.KeyToggleEv)} ]  Toggle Ev Overlay";

            BtnBindEv1.Content = $"[ {FormatKeyName(_overlay.KeyEv1)} ]  EMF Level 5";
            BtnBindEv2.Content = $"[ {FormatKeyName(_overlay.KeyEv2)} ]  D.O.T.S Projector";
            BtnBindEv3.Content = $"[ {FormatKeyName(_overlay.KeyEv3)} ]  Ultraviolet";
            BtnBindEv4.Content = $"[ {FormatKeyName(_overlay.KeyEv4)} ]  Freezing Temps";
            BtnBindEv5.Content = $"[ {FormatKeyName(_overlay.KeyEv5)} ]  Ghost Orb";
            BtnBindEv6.Content = $"[ {FormatKeyName(_overlay.KeyEv6)} ]  Ghost Writing";
            BtnBindEv7.Content = $"[ {FormatKeyName(_overlay.KeyEv7)} ]  Spirit Box";
        }

        private string FormatKeyName(int vKeyRaw)
        {
            string prefix = (vKeyRaw & MainWindow.ShiftFlag) != 0 ? "SHIFT + " : "";
            int vKey = vKeyRaw & 0xFFFF;

            Key wpfKey = KeyInterop.KeyFromVirtualKey(vKey);
            string name = wpfKey.ToString();

            string core;
            if (name.StartsWith("D") && name.Length == 2 && char.IsDigit(name[1])) core = name[1].ToString();
            else if (name.StartsWith("NumPad")) core = "NUM " + name.Substring(6);
            else if (name == "Space") core = "SPC";
            else if (name == "Return") core = "ENTER";
            else if (name == "Next") core = "PGDN";
            else if (name == "Prior") core = "PGUP";
            else if (name == "Capital") core = "CAPS";
            else if (name == "Oem3") core = "`"; // Explicitly mapped to show the backtick
            else core = name.ToUpper();

            return prefix + core;
        }
        private void Bind_Click(object sender, RoutedEventArgs e)
        {
            if (_activeBindButton != null) RefreshBindVisuals();

            _activeBindButton = sender as Button;
            _activeBindButton.Content = "[ PRESS ANY KEY ]";
            _activeBindButton.Foreground = (Brush)new BrushConverter().ConvertFromString("#FF5555");
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_activeBindButton == null) return;

            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                _activeBindButton.Foreground = (Brush)new BrushConverter().ConvertFromString("#CCCCCC");
                _activeBindButton = null;
                RefreshBindVisuals();
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Wait for a real key — ignore a lone modifier press so the user can hold Shift.
            if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt || key == Key.System)
                return;

            int newVkCode = KeyInterop.VirtualKeyFromKey(key);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) newVkCode |= MainWindow.ShiftFlag;

            string bindTarget = _activeBindButton.Tag.ToString();

            if (_overlay != null)
            {
                _overlay.SyncKeybind(bindTarget, newVkCode);
            }

            _activeBindButton.Foreground = (Brush)new BrushConverter().ConvertFromString("#CCCCCC");
            _activeBindButton = null;
            RefreshBindVisuals();
        }

        private void ResetBinds_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay != null)
            {
                if (_activeBindButton != null)
                {
                    _activeBindButton.Foreground = (Brush)new BrushConverter().ConvertFromString("#CCCCCC");
                    _activeBindButton = null;
                }

                _overlay.ResetKeybinds();
                RefreshBindVisuals();
            }
        }

        private void Difficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ColDifficulty == null) return;

            // Only Custom exposes the HUNT DURATION combo.
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
                if (w != null) _overlay.ActiveWeekly = w;
                else { CmbDifficulty.SelectedIndex = 4; return; }
            }
            else
            {
                _overlay.ActiveWeekly = null;
            }

            ApplyDifficultyLocks();
            UpdateHuntTierLabel();
            SilentUpdate_Trigger(null, null);
        }

        /// <summary>Speed is editable only on Custom (presets/Weekly are fixed at their values);
        /// map is locked only on Weekly.</summary>
        private void ApplyDifficultyLocks()
        {
            if (SpeedCombo == null) return;

            bool custom = CmbDifficulty.SelectedIndex == MainWindow.DiffCustom;
            bool weekly = CmbDifficulty.SelectedIndex == MainWindow.DiffWeekly;

            SpeedCombo.IsEnabled = custom;
            SpeedCombo.Opacity = custom ? 1.0 : 0.5;

            MapCombo.IsEnabled = !weekly;
            MapCombo.Opacity = weekly ? 0.5 : 1.0;

            if (weekly && _overlay.ActiveWeekly != null)
            {
                MapCombo.SelectedIndex = Math.Clamp(_overlay.ActiveWeekly.MapSizeIndex, 0, 2);
                int si = WeeklyDataService.SpeedToIndex(_overlay.ActiveWeekly.GhostSpeed);
                SpeedCombo.SelectedIndex = si >= 0 ? si : 2;
            }
            else if (!custom && SpeedCombo.SelectedIndex != 2)
            {
                SpeedCombo.SelectedIndex = 2;
            }
        }

        private int GetResolvedDurationIndex()
        {
            int diffIdx = CmbDifficulty.SelectedIndex;
            if (diffIdx == 0) return 0;
            if (diffIdx == 1) return 1;
            if (diffIdx >= 2 && diffIdx <= 4) return 2;
            if (diffIdx == MainWindow.DiffWeekly) return _overlay.ActiveWeekly?.HuntTier ?? 2;
            if (diffIdx == MainWindow.DiffCustom) return CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1;
            return 1;
        }

        private void SilentUpdate_Trigger(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _overlay == null) return;

            if (CmbDifficulty.SelectedIndex == MainWindow.DiffWeekly && _overlay.ActiveWeekly != null)
            {
                _overlay.ApplyWeekly(_overlay.ActiveWeekly);
                _overlay.NotifyMatchSettingsChanged();
                return;
            }

            double[,] huntTimes = new double[,] {
                { 15.0, 30.0, 40.0 },
                { 20.0, 40.0, 50.0 },
                { 30.0, 50.0, 60.0 }
            };

            int durIdx = GetResolvedDurationIndex();
            int mapIdx = MapCombo.SelectedIndex >= 0 ? MapCombo.SelectedIndex : 0;

            _overlay.BaseHuntDuration = huntTimes[durIdx, mapIdx];
            _overlay.MapSizeIndex = mapIdx;
            _overlay.DifficultyIndex = CmbDifficulty.SelectedIndex >= 0 ? CmbDifficulty.SelectedIndex : 1;
            _overlay.CustomDurationIndex = CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1;

            if (SpeedCombo.SelectedIndex == 0) _overlay.SpeedMultiplierSetting = 0.5;
            else if (SpeedCombo.SelectedIndex == 1) _overlay.SpeedMultiplierSetting = 0.75;
            else if (SpeedCombo.SelectedIndex == 2) _overlay.SpeedMultiplierSetting = 1.0;
            else if (SpeedCombo.SelectedIndex == 3) _overlay.SpeedMultiplierSetting = 1.25;
            else if (SpeedCombo.SelectedIndex == 4) _overlay.SpeedMultiplierSetting = 1.5;

            _overlay.NotifyMatchSettingsChanged();
        }

        private void PreviewUpdate_Trigger(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _overlay == null) return;

            _overlay.LastSettingsPreviewTime = DateTime.Now;

            if (CmbPosition.SelectedIndex >= 0)
            {
                _overlay.OverlayPosition = CmbPosition.SelectedIndex;
                _overlay.RefreshCompactModeVisuals(true);
            }

            _overlay.BgBrush.Opacity = OpacitySlider.Value;
            _overlay.OverlayScale.ScaleX = ScaleSlider.Value;
            _overlay.OverlayScale.ScaleY = ScaleSlider.Value;

            UpdateSliderLabels();
            _overlay.RefreshCompactModeVisuals(true);
        }

        private void Module_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _overlay == null) return;

            _overlay.LastSettingsPreviewTime = DateTime.Now;

            bool[] newStates = new bool[] {
                TglSmudge.IsChecked == true,
                TglCooldown.IsChecked == true,
                TglHunt.IsChecked == true,
                TglObambo.IsChecked == true,
                TglSpeed.IsChecked == true,
                TglBloodMoon.IsChecked == true,
                TglCursed.IsChecked == true,
                TglEvidence.IsChecked == true,
                TglGhosts.IsChecked == true
            };

            _overlay.ApplyModuleVisibility(newStates);
        }

        private void ModuleToggle_Click(object sender, RoutedEventArgs e)
        {
            Module_Click(sender, e);
        }

        private void PersistentMode_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay != null)
            {
                _overlay.IsCompactMode = ChkPersistentOverlay.IsChecked == false;
                ChkAlwaysShowEvidence.IsEnabled = _overlay.IsCompactMode;
                _overlay.RefreshCompactModeVisuals(true);
            }
        }

        private void AlwaysShowEvidence_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay != null)
            {
                _overlay.AlwaysShowEvidence = ChkAlwaysShowEvidence.IsChecked == true;
                _overlay.RefreshCompactModeVisuals(true);
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _overlay == null) return;
            _overlay.MasterVolume = VolumeSlider.Value;
            UpdateVolumeLabel();
        }

        private void TestAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay != null)
            {
                _overlay.TestAudio();
            }
        }

        private void SaveSettingsData()
        {
            string statesStr = "";
            statesStr += TglSmudge.IsChecked == true ? "1" : "0";
            statesStr += TglCooldown.IsChecked == true ? "1" : "0";
            statesStr += TglHunt.IsChecked == true ? "1" : "0";
            statesStr += TglObambo.IsChecked == true ? "1" : "0";
            statesStr += TglSpeed.IsChecked == true ? "1" : "0";
            statesStr += TglBloodMoon.IsChecked == true ? "1" : "0";
            statesStr += TglCursed.IsChecked == true ? "1" : "0";
            statesStr += TglEvidence.IsChecked == true ? "1" : "0";
            statesStr += TglGhosts.IsChecked == true ? "1" : "0";

            int finalDurIdx = GetResolvedDurationIndex();
            int diffIdx = CmbDifficulty.SelectedIndex >= 0 ? CmbDifficulty.SelectedIndex : 1;
            int customDurIdx = CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1;
            int posIdx = CmbPosition.SelectedIndex >= 0 ? CmbPosition.SelectedIndex : 1; // Default to Center

            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay");
            Directory.CreateDirectory(appDataFolder);

            string[] lines = {
                "[Game Settings]",
                "SettingsVersion=2",
                $"MapSize={MapCombo.SelectedIndex}",
                $"Difficulty={diffIdx}",
                $"CustomDuration={customDurIdx}",
                $"HuntDuration={finalDurIdx}",
                $"GhostSpeed={SpeedCombo.SelectedIndex}",
                $"EvidenceLimit={_overlay.EvidenceLimit}",
                "",
                "[Overlay Display]",
                $"Position={posIdx}",
                $"Opacity={OpacitySlider.Value}",
                $"Scale={ScaleSlider.Value}",
                $"CompactMode={(ChkPersistentOverlay.IsChecked == false ? 1 : 0)}",
                $"AlwaysShowEvidence={(ChkAlwaysShowEvidence.IsChecked == true ? 1 : 0)}",
                $"ModulesActive={statesStr}",
                "",
                "[Audio]",
                $"Volume={VolumeSlider.Value}",
                "",
                "[Keybinds]",
                $"KeySmudge={_overlay.KeySmudge}",
                $"KeyCooldown={_overlay.KeyCooldown}",
                $"KeyHunt={_overlay.KeyHunt}",
                $"KeyObambo={_overlay.KeyObambo}",
                $"KeySpeedReset={_overlay.KeySpeedReset}",
                $"KeyBloodMoon={_overlay.KeyBloodMoon}",
                $"KeyCursedHunt={_overlay.KeyCursedHunt}",
                $"KeySpeedTap={_overlay.KeySpeedTap}",
                $"KeySettings={_overlay.KeySettings}",
                $"KeyEvidence={_overlay.KeyEvidence}",
                $"KeyClear={_overlay.KeyClear}",
                $"KeyToggleEv={_overlay.KeyToggleEv}",
                $"KeyEv1={_overlay.KeyEv1}",
                $"KeyEv2={_overlay.KeyEv2}",
                $"KeyEv3={_overlay.KeyEv3}",
                $"KeyEv4={_overlay.KeyEv4}",
                $"KeyEv5={_overlay.KeyEv5}",
                $"KeyEv6={_overlay.KeyEv6}",
                $"KeyEv7={_overlay.KeyEv7}"
            };

            File.WriteAllLines(_configPath, lines);

            // Legacy positional format. The slot that once held KeyMap is kept as a literal 0 so
            // the field indices (notably posIdx, read back at settings[21]) stay put for any old
            // configs still on this format. The map viewer itself is gone.
            string saveString = $"{MapCombo.SelectedIndex}|{SpeedCombo.SelectedIndex}|{OpacitySlider.Value}|{ScaleSlider.Value}|{finalDurIdx}|{statesStr}|{VolumeSlider.Value}|{_overlay.KeySmudge}|{_overlay.KeyCooldown}|{_overlay.KeyHunt}|{_overlay.KeyObambo}|{_overlay.KeySpeedReset}|{_overlay.KeyBloodMoon}|{_overlay.KeyCursedHunt}|{_overlay.KeySpeedTap}|{_overlay.KeySettings}|{diffIdx}|{customDurIdx}|{_overlay.KeyEvidence}|{_overlay.KeyClear}|0|{posIdx}";
            string fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            try { File.WriteAllText(fallbackPath, saveString); } catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { SaveSettingsData(); } catch { }

            if (_isFirstRun)
            {
                string currentKey = FormatKeyName(_overlay.KeySettings);
                MessageBox.Show($"Settings saved!\n\nPress [ {currentKey} ] at any time to reopen this menu.", "Setup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            this.Close();
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            try { SaveSettingsData(); } catch { }
            Application.Current.Shutdown();
        }
    }
}