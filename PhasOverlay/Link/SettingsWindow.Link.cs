using System;
using System.Windows;
using PhasOverlay.Link;

namespace PhasOverlay
{
    public partial class SettingsWindow
    {
        private LinkCoordinator? LinkRoom => ((App)Application.Current)?.Link;

        private void InitializeLinkSync()
        {
            var link = LinkRoom;
            if (link == null) return;

            _overlay.LinkedSettingsApplied += OnLinkedSettingsApplied;
            link.Changed += OnLinkChanged;
            Closed += (s, e) =>
            {
                _overlay.LinkedSettingsApplied -= OnLinkedSettingsApplied;
                link.Changed -= OnLinkChanged;
            };
            ApplyLinkLocks();
        }

        private void OnLinkChanged(LinkStateChange change) => Dispatcher.Invoke(ApplyLinkLocks);

        /// <summary>Pulls the room's authoritative values back into this window's combos.</summary>
        private void OnLinkedSettingsApplied()
        {
            Dispatcher.Invoke(() =>
            {
                bool wasLoaded = _isLoaded;
                _isLoaded = false;
                RefreshWeeklyComboItem();
                CmbDifficulty.SelectedIndex = Math.Clamp(_overlay.DifficultyIndex, 0, MainWindow.DiffCustom);
                MapCombo.SelectedIndex = Math.Clamp(_overlay.MapSizeIndex, 0, 2);
                CmbCustomDuration.SelectedIndex = Math.Clamp(_overlay.CustomDurationIndex, 0, 2);
                SpeedCombo.SelectedIndex = MainWindow.SpeedMultiplierToIndex(_overlay.SpeedMultiplierSetting);
                _isLoaded = wasLoaded;
                ApplyLinkLocks();
            });
        }

        private void ApplyLinkLocks()
        {
            var link = LinkRoom;
            bool locked = link != null && link.SettingsLocked;

            if (locked)
            {
                CmbDifficulty.IsEnabled = false;
                MapCombo.IsEnabled = false;
                CmbCustomDuration.IsEnabled = false;
                SpeedCombo.IsEnabled = false;
                CmbDifficulty.Opacity = 0.5;
                MapCombo.Opacity = 0.5;
                CmbCustomDuration.Opacity = 0.5;
                SpeedCombo.Opacity = 0.5;
                return;
            }

            // Everything the lock touched is restored here. ApplyDifficultyLocks then re-applies
            // the rules it owns, which do not cover difficulty or the custom hunt tier.
            CmbDifficulty.IsEnabled = true;
            MapCombo.IsEnabled = true;
            CmbCustomDuration.IsEnabled = true;
            SpeedCombo.IsEnabled = true;
            CmbDifficulty.Opacity = 1.0;
            MapCombo.Opacity = 1.0;
            CmbCustomDuration.Opacity = 1.0;
            SpeedCombo.Opacity = 1.0;
            if (_isLoaded) ApplyDifficultyLocks();
        }

        /// <summary>True when the room owns this change, so the local handler must not apply it.</summary>
        private bool LinkOwnsMatchSettings()
        {
            var link = LinkRoom;
            if (link == null || !link.IsLinked) return false;

            int difficulty = CmbDifficulty.SelectedIndex >= 0 ? CmbDifficulty.SelectedIndex : 1;
            int limit = _overlay.EvidenceLimit;

            var settings = new RoomSettings
            {
                Difficulty = difficulty,
                Map = MapCombo.SelectedIndex >= 0 ? MapCombo.SelectedIndex : 0,
                CustomTier = CmbCustomDuration.SelectedIndex >= 0 ? CmbCustomDuration.SelectedIndex : 1,
                SpeedIndex = SpeedCombo.SelectedIndex >= 0 ? SpeedCombo.SelectedIndex : 2,
                HuntTier = GetResolvedDurationIndex()
            };

            // Weekly is a fixed challenge, so its own values win over whatever the combos show.
            if (difficulty == MainWindow.DiffWeekly)
            {
                var weekly = WeeklyDataService.GetWeekly();
                if (weekly == null) return false;
                settings.Map = weekly.MapSizeIndex;
                settings.SpeedIndex = WeeklyDataService.SpeedToIndex(weekly.GhostSpeed) is int index and >= 0 ? index : 2;
                settings.HuntTier = weekly.HuntTier;
                limit = weekly.EvidenceGiven;
            }

            bool consumed = link.TryConfigure(settings, limit);
            if (consumed) OnLinkedSettingsApplied();
            return consumed;
        }
    }
}
