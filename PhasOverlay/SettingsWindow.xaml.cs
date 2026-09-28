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
            PopulateDisplays();
            OpacitySlider.Value = _overlay.BgOpacity;
            ScaleSlider.Value = _overlay.OverlayScale.ScaleX;

            VolumeSlider.Value = _overlay.MasterVolume;

            UpdateVolumeLabel();
            UpdateSliderLabels();

            SyncModuleRows();

            RefreshBindVisuals();

            // Seed the match combos from the overlay's already-loaded (and migrated) values.
            RefreshWeeklyComboItem();
            MapCombo.SelectedIndex = Math.Clamp(_overlay.MapSizeIndex, 0, 2);
            CmbCustomDuration.SelectedIndex = Math.Clamp(_overlay.CustomDurationIndex, 0, 2);
            CmbDifficulty.SelectedIndex = Math.Clamp(_overlay.DifficultyIndex, 0, MainWindow.DiffCustom);

            _isLoaded = true;
            Difficulty_SelectionChanged(null, null);
            InitializeLinkSync();

            this.Loaded += (s, e) => DisplayService.CenterOn(this, _overlay.DisplayIndex);
            _overlay.WeeklyDataStateChanged += OnWeeklyDataStateChanged;
            this.Closed += (s, e) => _overlay.WeeklyDataStateChanged -= OnWeeklyDataStateChanged;
        }

        /// <summary>Fills the display list. The picker hides on a single-display machine, but the
        /// section header stays since it also covers the mode toggles below it.</summary>
        private void PopulateDisplays()
        {
            var displays = DisplayService.GetDisplays();

            CmbDisplay.Items.Clear();
            foreach (var d in displays)
                CmbDisplay.Items.Add(new ComboBoxItem { Content = d.Label });

            int idx = _overlay.DisplayIndex;
            if (idx < 0 || idx >= displays.Count) idx = DisplayService.PrimaryIndex();
            CmbDisplay.SelectedIndex = idx;

            CmbDisplay.Visibility = displays.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Display_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _overlay == null || CmbDisplay.SelectedIndex < 0) return;

            _overlay.ApplyDisplayChange(CmbDisplay.SelectedIndex);
            _overlay.LastSettingsPreviewTime = DateTime.Now;
            _overlay.RefreshCompactModeVisuals(true);
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

        private void OnWeeklyDataStateChanged(WeeklyUpdateResult result)
        {
            if (result != WeeklyUpdateResult.Updated) return;

            RefreshWeeklyComboItem();
            if (CmbDifficulty.SelectedIndex == MainWindow.DiffWeekly && LinkRoom?.IsLinked != true)
                Difficulty_SelectionChanged(null, null);
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

            if (CmbDifficulty.SelectedIndex == MainWindow.DiffCustom) // Custom uses the HUNT DURATION combo instead
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

        private void OpenModules_Click(object sender, RoutedEventArgs e)
        {
            SyncModuleRows();
            ModulesModalOverlay.Visibility = Visibility.Visible;
        }

        private void CloseModules_Click(object sender, RoutedEventArgs e)
        {
            ModulesModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void ModulesModalOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => CloseModules_Click(sender, e);

        /// <summary>Both modals: a click on the card itself must not reach the scrim behind it.</summary>
        private void ModalContent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

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
            => CloseHotkeys_Click(sender, e);

        private void RefreshBindVisuals()
        {
            BtnBindSettings.Content = $"[ {FormatKeyName(_overlay.KeySettings)} ]  Settings";
            BtnBindEvidence.Content = $"[ {FormatKeyName(_overlay.KeyEvidence)} ]  Evidence Window";
            BtnBindMap.Content = $"[ {FormatKeyName(_overlay.KeyMap)} ]  Map Window";
            BtnBindMapHold.Content = $"[ {FormatKeyName(_overlay.KeyMapHold)} ]  Hold: Show Map";

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

        private string FormatKeyName(int vKeyRaw) => MainWindow.FormatKeyName(vKeyRaw);
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

            // Wait for a real key. A lone modifier press is ignored so the user can hold Shift.
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
            if (diffIdx == MainWindow.DiffWeekly) return _overlay.ResolveHuntTier();
            if (diffIdx == MainWindow.DiffCustom) return CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1;
            return 1;
        }

        private void SilentUpdate_Trigger(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _overlay == null) return;
            if (LinkOwnsMatchSettings()) return;

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

            _overlay.BgOpacity = OpacitySlider.Value;
            _overlay.OverlayScale.ScaleX = ScaleSlider.Value;
            _overlay.OverlayScale.ScaleY = ScaleSlider.Value;

            UpdateSliderLabels();
            _overlay.RefreshCompactModeVisuals(true);
        }

        // Rows answer Checked, not Click, since arrow keys change a radio group without clicking it.
        private bool _syncingRows;

        // A null segment is a mode the row does not offer.
        private record ModeRow(ModuleId Id, RadioButton? Off, RadioButton? Auto, RadioButton Always);

        private ModeRow[]? _rows;

        private ModeRow[] Rows => _rows ??= new[]
        {
            new ModeRow(ModuleId.Smudge, RbSmudgeOff, RbSmudgeAuto, RbSmudgeAlways),
            new ModeRow(ModuleId.Cooldown, RbCooldownOff, RbCooldownAuto, RbCooldownAlways),
            new ModeRow(ModuleId.Hunt, RbHuntOff, RbHuntAuto, RbHuntAlways),
            new ModeRow(ModuleId.Obambo, RbObamboOff, RbObamboAuto, RbObamboAlways),
            new ModeRow(ModuleId.SpeedTap, RbSpeedOff, RbSpeedAuto, RbSpeedAlways),
            new ModeRow(ModuleId.BloodMoon, null, RbBloodMoonAuto, RbBloodMoonAlways),
            new ModeRow(ModuleId.Cursed, null, RbCursedAuto, RbCursedAlways),
            new ModeRow(ModuleId.Evidence, RbEvidenceOff, null, RbEvidenceAlways),
            new ModeRow(ModuleId.Ghosts, RbGhostsOff, null, RbGhostsAlways)
        };

        private void SyncModuleRows()
        {
            _syncingRows = true;
            foreach (var row in Rows)
            {
                var mode = _overlay.ModeOf(row.Id);
                if (row.Off != null) row.Off.IsChecked = mode == ModuleMode.Off;
                if (row.Auto != null) row.Auto.IsChecked = mode == ModuleMode.Auto;

                // A mode this row cannot show lands on Always rather than leaving the row blank.
                row.Always.IsChecked = mode == ModuleMode.Always
                                       || (row.Off?.IsChecked != true && row.Auto?.IsChecked != true);
            }
            _syncingRows = false;
        }

        private ModuleMode[] ReadModuleRows()
        {
            var modes = new ModuleMode[Rows.Length];
            foreach (var row in Rows)
            {
                modes[(int)row.Id] = row.Off?.IsChecked == true ? ModuleMode.Off
                    : row.Auto?.IsChecked == true ? ModuleMode.Auto
                    : ModuleMode.Always;
            }
            return modes;
        }

        private void ModuleMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _syncingRows || _overlay == null) return;

            // Deliberately not the whole-overlay preview: only the row just changed flashes up.
            ModuleId? changed = null;
            foreach (var row in Rows)
            {
                if (ReferenceEquals(sender, row.Off) || ReferenceEquals(sender, row.Auto) || ReferenceEquals(sender, row.Always))
                {
                    changed = row.Id;
                    break;
                }
            }

            _overlay.ApplyModuleModes(ReadModuleRows(), changed);
        }

        private void ModulePreset_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _overlay == null) return;

            var mode = (sender as FrameworkElement)?.Tag as string == "Always" ? ModuleMode.Always : ModuleMode.Auto;

            _overlay.LastSettingsPreviewTime = DateTime.Now;
            _overlay.ApplyModulePreset(mode);
            SyncModuleRows();
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
            // Match settings come from the overlay, never this window's combos. While linked the
            // room owns them, and closing a stale window must not roll them back.
            int finalDurIdx = _overlay.ResolveHuntTier();
            int diffIdx = _overlay.DifficultyIndex;
            int customDurIdx = _overlay.CustomDurationIndex;
            int mapIdx = _overlay.MapSizeIndex;
            int speedIdx = MainWindow.SpeedMultiplierToIndex(_overlay.SpeedMultiplierSetting);
            int posIdx = CmbPosition.SelectedIndex >= 0 ? CmbPosition.SelectedIndex : 1; // Default to Center

            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay");
            Directory.CreateDirectory(appDataFolder);

            string[] lines = {
                "[Game Settings]",
                "SettingsVersion=2",
                $"MapSize={mapIdx}",
                $"Difficulty={diffIdx}",
                $"CustomDuration={customDurIdx}",
                $"HuntDuration={finalDurIdx}",
                $"GhostSpeed={speedIdx}",
                $"EvidenceLimit={_overlay.EvidenceLimit}",
                "",
                "[Overlay Display]",
                $"Position={posIdx}",
                $"Display={(CmbDisplay.SelectedIndex >= 0 ? CmbDisplay.SelectedIndex : DisplayService.PrimaryIndex())}",
                $"Opacity={OpacitySlider.Value}",
                $"Scale={ScaleSlider.Value}",
                $"ModuleModes={_overlay.ModuleModesString()}",
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
                $"KeyMap={_overlay.KeyMap}",
                $"KeyMapHold={_overlay.KeyMapHold}",
                $"KeyEv1={_overlay.KeyEv1}",
                $"KeyEv2={_overlay.KeyEv2}",
                $"KeyEv3={_overlay.KeyEv3}",
                $"KeyEv4={_overlay.KeyEv4}",
                $"KeyEv5={_overlay.KeyEv5}",
                $"KeyEv6={_overlay.KeyEv6}",
                $"KeyEv7={_overlay.KeyEv7}"
            };

            lock (MainWindow.SettingsFileGate)
            {
                File.WriteAllLines(_configPath, lines);
            }
        }

        // The Settings hotkey closes this window without either button, so saving also runs on close.
        private bool _saved;

        private void SaveOnce()
        {
            if (_saved) return;
            _saved = true;
            try { SaveSettingsData(); } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            SaveOnce();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveOnce();

            if (_isFirstRun)
            {
                string currentKey = FormatKeyName(_overlay.KeySettings);
                MessageBox.Show($"Settings saved!\n\nPress [ {currentKey} ] at any time to reopen this menu.", "Setup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            this.Close();
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            SaveOnce();
            Application.Current.Shutdown();
        }

        internal void SaveBeforeAppExit()
        {
            SaveOnce();
        }
    }
}
