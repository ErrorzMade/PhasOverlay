using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PhasOverlay.Link;

namespace PhasOverlay
{
    public partial class EvidenceWindow
    {
        /// <summary>Set while an authoritative change is being applied, so no local handler
        /// mistakes it for a user action and sends it back.</summary>
        private bool _applyingRemote;

        private CheckBox BoxFor(string evidence) => evidence switch
        {
            "EMF Level 5" => ChkEmf,
            "D.O.T.S Projector" => ChkDots,
            "Ultraviolet" => ChkUv,
            "Freezing Temperatures" => ChkFreezing,
            "Ghost Orb" => ChkOrb,
            "Ghost Writing" => ChkWriting,
            "Spirit Box" => ChkBox,
            _ => null!
        };

        private ToggleButton HuntPillFor(string key) => key switch
        {
            "veryearly" => TglHuntVeryEarly,
            "early" => TglHuntEarly,
            "normal" => TglHuntNormal,
            "late" => TglHuntLate,
            _ => null!
        };

        private string HuntKeyFor(ToggleButton pill) =>
            pill == TglHuntVeryEarly ? "veryearly"
            : pill == TglHuntEarly ? "early"
            : pill == TglHuntNormal ? "normal"
            : pill == TglHuntLate ? "late" : "";

        private string SpeedKeyFor(ToggleButton pill) =>
            pill == TglSpeedSlow ? "slow"
            : pill == TglSpeedNormal ? "normal"
            : pill == TglSpeedFast ? "fast" : "";

        private ToggleButton SpeedPillFor(string key) => key switch
        {
            "slow" => TglSpeedSlow,
            "normal" => TglSpeedNormal,
            "fast" => TglSpeedFast,
            _ => null!
        };

        /// <summary>
        /// Reads the shared half of the tracker. Auto rule-outs are the engine's own conclusion,
        /// not the user's, so they travel as unset.
        /// </summary>
        public SharedTrackerState BuildSharedState()
        {
            var state = new SharedTrackerState { Limit = _evidenceGivenLimit };

            foreach (string evidence in LinkProtocol.Evidence)
            {
                var box = BoxFor(evidence);
                bool auto = _autoRuledOut.Contains(box);
                state.Evidence[evidence] = auto ? 0 : box.IsChecked == true ? 1 : box.IsChecked == false ? 2 : 0;
            }

            foreach (string key in LinkProtocol.HuntKeys) state.Hunt[key] = HuntPillFor(key).IsChecked == true;
            foreach (string key in LinkProtocol.SpeedKeys) state.Speed[key] = SpeedPillFor(key).IsChecked == true;

            foreach (var ghost in _masterGhostList.Where(g => g.CardState != 0))
                state.Cards[ghost.Name] = ghost.CardState;

            state.Settings = BuildRoomSettings();
            return state;
        }

        private RoomSettings BuildRoomSettings() => new()
        {
            Difficulty = _main.DifficultyIndex,
            Map = _main.MapSizeIndex,
            CustomTier = _main.CustomDurationIndex,
            SpeedIndex = SpeedMultToIndex(_main.SpeedMultiplierSetting),
            HuntTier = _main.ResolveHuntTier()
        };

        /// <summary>Applies an authoritative board and runs the filtering engine once.</summary>
        public void ApplySharedState(SharedTrackerState state)
        {
            _applyingRemote = true;
            try
            {
                foreach (string evidence in LinkProtocol.Evidence)
                {
                    var box = BoxFor(evidence);
                    box.IsChecked = state.Evidence[evidence] switch { 1 => true, 2 => false, _ => null };
                }

                foreach (string key in LinkProtocol.HuntKeys) HuntPillFor(key).IsChecked = state.Hunt[key];
                foreach (string key in LinkProtocol.SpeedKeys) SpeedPillFor(key).IsChecked = state.Speed[key];

                // Assigned only on a real change: the setter notifies unconditionally, and a patch
                // that touched one evidence box would otherwise repaint all thirty cards.
                foreach (var ghost in _masterGhostList)
                {
                    int card = state.Cards.TryGetValue(ghost.Name, out int value) ? value : 0;
                    if (ghost.CardState != card) ghost.CardState = card;
                }

                _evidenceGivenLimit = state.Limit;
                _main.EvidenceLimit = state.Limit;
                SyncEvidencePills();
                ApplyLinkLocks();
                ApplyFilteringEngine();
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        /// <summary>
        /// Applies only the fields in a patch, the way the browser client does. A card change then
        /// costs exactly what a local card click costs: no filtering pass, no scroll reset, and no
        /// repaint of anything the change did not touch. Returns false when the patch needs the
        /// full path instead.
        /// </summary>
        public bool ApplyRemoteChanges(IReadOnlyList<RemoteChange> changes)
        {
            if (changes.Count == 0) return false;

            bool refilter = false;
            bool cardsOnly = false;

            _applyingRemote = true;
            try
            {
                foreach (var change in changes)
                {
                    switch (change.Field)
                    {
                        case "evidence":
                            var box = BoxFor(change.Key);
                            if (box == null) return false;
                            box.IsChecked = change.IntValue switch { 1 => true, 2 => false, _ => null };
                            refilter = true;
                            break;

                        case "hunt":
                            var huntPill = HuntPillFor(change.Key);
                            if (huntPill == null) return false;
                            huntPill.IsChecked = change.BoolValue;
                            refilter = true;
                            break;

                        case "speed":
                            var speedPill = SpeedPillFor(change.Key);
                            if (speedPill == null) return false;
                            speedPill.IsChecked = change.BoolValue;
                            refilter = true;
                            break;

                        case "card":
                            var ghost = _masterGhostList.Find(g => g.Name == change.Key);
                            if (ghost != null && ghost.CardState != change.IntValue)
                                ghost.CardState = change.IntValue;
                            cardsOnly = true;
                            break;

                        default:
                            return false;
                    }
                }

                if (refilter) ApplyFilteringEngine();
                else if (cardsOnly) PushPossibleGhostsToOverlay();
                return true;
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private LinkCoordinator? LinkRoom =>
            _tutorialMode || _applyingRemote ? null : ((App)System.Windows.Application.Current)?.Link;

        /// <summary>
        /// Every shared mutation passes through here. A true result means the room owns the change
        /// and the caller must not touch local state.
        /// </summary>
        private bool LinkOwnsEvidence(CheckBox box)
        {
            var link = LinkRoom;
            if (link == null || !link.IsLinked) return false;

            int current = box.IsChecked == true ? 1 : box.IsChecked == false ? 2 : 0;
            int next = (current + 1) % 3;
            return link.TrySetEvidence(box.Content.ToString()!, next);
        }

        private bool LinkOwnsFilter(string field, string key, bool value)
        {
            var link = LinkRoom;
            if (link == null || !link.IsLinked) return false;
            return link.TrySetFilter(field, key, value);
        }

        private bool LinkOwnsCard(string ghost, int value)
        {
            var link = LinkRoom;
            if (link == null || !link.IsLinked) return false;
            return link.TrySetCard(ghost, value);
        }

        private bool LinkOwnsReset()
        {
            var link = LinkRoom;
            if (link == null || !link.IsLinked) return false;
            return link.TryReset();
        }

        /// <summary>True when the room owns the match combos, so the local handler must not apply them.</summary>
        private bool LinkOwnsMatchSettings()
        {
            var link = LinkRoom;
            if (link == null || !link.IsLinked) return false;

            int difficulty = CtxDifficulty.SelectedIndex >= 0 ? CtxDifficulty.SelectedIndex : 1;
            int customTier = CtxHunt.SelectedIndex >= 0 ? CtxHunt.SelectedIndex : 1;
            int limit = _evidenceGivenLimit;

            var settings = new RoomSettings
            {
                Difficulty = difficulty,
                Map = CtxMap.SelectedIndex >= 0 ? CtxMap.SelectedIndex : 0,
                CustomTier = customTier,
                SpeedIndex = CtxSpeed.SelectedIndex >= 0 ? CtxSpeed.SelectedIndex : 2,
                // Derived from the selection being made, never from the difficulty still in
                // MainWindow, which has not been updated yet on this path.
                HuntTier = ResolveHuntTierFor(difficulty, customTier)
            };

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
            if (consumed) SyncMatchControls();
            return consumed;
        }

        private static int ResolveHuntTierFor(int difficulty, int customTier)
        {
            if (difficulty == 0) return 0;
            if (difficulty == 1) return 1;
            if (difficulty >= 2 && difficulty <= 4) return 2;
            if (difficulty == MainWindow.DiffWeekly) return WeeklyDataService.GetWeekly()?.HuntTier ?? 2;
            return System.Math.Clamp(customTier, 0, 2);
        }

        /// <summary>Evidence Given is host-owned, so it travels with the settings it gives meaning to.</summary>
        private bool LinkOwnsEvidenceGiven(int requested)
        {
            var link = LinkRoom;
            if (link == null || !link.IsLinked) return false;
            return link.TryConfigure(BuildRoomSettings(), requested);
        }

        /// <summary>Local half of a tracker reset. Runs whether the board reset is local or
        /// authoritative, since neither belongs to the room.</summary>
        private void ResetTrackerLocalEffects()
        {
            StopFootstepPlayback();
            _main.ResetSpeedTap(false);
        }

        /// <summary>Locks the shared controls a guest may not drive, and everything while
        /// reconnecting.</summary>
        public void ApplyLinkLocks()
        {
            if (_tutorialMode) return;

            var link = ((App)System.Windows.Application.Current)?.Link;
            bool sharedEditable = link == null || !link.IsLinked || link.CanEditShared;
            bool settingsLocked = link != null && link.SettingsLocked;

            // Evidence box enabled state and opacity belong to the filtering engine. Touching them
            // here overwrites its greying, so the shared lock is enforced at the command boundary.
            foreach (string key in LinkProtocol.HuntKeys) HuntPillFor(key).IsEnabled = sharedEditable;
            foreach (string key in LinkProtocol.SpeedKeys) SpeedPillFor(key).IsEnabled = sharedEditable;
            BtnResetTracker.IsEnabled = sharedEditable;

            bool weekly = _main.DifficultyIndex == MainWindow.DiffWeekly;
            bool givenEditable = !weekly && !settingsLocked;
            TglEv0.IsEnabled = givenEditable;
            TglEv1.IsEnabled = givenEditable;
            TglEv2.IsEnabled = givenEditable;
            TglEv3.IsEnabled = givenEditable;

            if (settingsLocked)
            {
                CtxDifficulty.IsEnabled = false;
                CtxMap.IsEnabled = false;
                CtxHunt.IsEnabled = false;
                CtxSpeed.IsEnabled = false;
                CtxDifficulty.Opacity = 0.5;
                CtxHunt.Opacity = 0.5;
                return;
            }

            // Difficulty and the hunt tier are not owned by SyncMatchControls, so they have to be
            // restored here or they stay dead once a lock has been applied.
            CtxDifficulty.IsEnabled = true;
            CtxDifficulty.Opacity = 1.0;
            CtxHunt.Opacity = 1.0;
            SyncMatchControls();
        }
    }
}
