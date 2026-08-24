using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PhasOverlay.Link;

namespace PhasOverlay
{
    public partial class MainWindow
    {
        private LinkCoordinator? Link => ((App)Application.Current)?.Link;

        private void InitializeLink()
        {
            ((App)Application.Current).InitializeLink(this);
            var link = Link;
            if (link == null) return;

            link.SnapshotApplied += ApplyLinkSnapshot;
            link.PatchApplied += ApplyLinkPatch;
            link.Changed += OnLinkChanged;
        }

        private bool _linkApplyQueued;
        private bool _linkFullApply;
        private readonly List<RemoteChange> _linkPendingChanges = new();
        private int? _authoritativeWeeklyHuntTier;

        private void ApplyLinkSnapshot(SharedTrackerState state)
        {
            _linkFullApply = true;
            QueueLinkApply();
        }

        private bool _linkSettingsDirty;

        private void ApplyLinkPatch(IReadOnlyList<RemoteChange> changes, bool reset)
        {
            if (reset || changes.Count == 0) _linkFullApply = true;

            foreach (var change in changes)
            {
                if (change.Field is "settings" or "limit") _linkSettingsDirty = true;
                else _linkPendingChanges.Add(change);
            }

            QueueLinkApply();
        }

        /// <summary>
        /// Applying repaints a layered, always-on-top window, which the compositor has to push over
        /// whatever is running. Several changes arriving together are collapsed into one repaint by
        /// applying the latest state once the dispatcher is free, rather than once per message.
        /// </summary>
        private void QueueLinkApply()
        {
            if (_linkApplyQueued) return;
            _linkApplyQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, ApplyLatestLinkState);
        }

        private void ApplyLatestLinkState()
        {
            _linkApplyQueued = false;
            var state = Link?.State;
            if (state == null) { _linkPendingChanges.Clear(); _linkFullApply = false; return; }

            bool full = _linkFullApply;
            bool settingsDirty = _linkSettingsDirty;
            var pending = _linkPendingChanges.Count > 0 ? _linkPendingChanges.ToArray() : Array.Empty<RemoteChange>();
            _linkFullApply = false;
            _linkSettingsDirty = false;
            _linkPendingChanges.Clear();

            if (!full)
            {
                // Difficulty, map, hunt tier and ghost speed do not change which ghosts are
                // possible, so a settings change never touches the board. Evidence Given does, and
                // SyncMatchControls re-runs the engine for it.
                if (settingsDirty)
                {
                    ApplyLinkedMatchSettings(state.Settings, state.Limit);
                    _evidenceWin?.ApplyLinkLocks();
                }

                if (pending.Length == 0) return;
                if (_evidenceWin == null || _evidenceWin.ApplyRemoteChanges(pending)) return;
            }

            ApplyLinkedMatchSettings(state.Settings, state.Limit);
            _evidenceWin?.ApplySharedState(state);
        }

        private void OnLinkChanged(LinkStateChange change)
        {
            _evidenceWin?.OnLinkStateChanged(change);
        }

        /// <summary>
        /// The single authoritative path for host-owned match settings. Everything that mirrors
        /// them reads from here, so an open Settings window cannot drift from the room.
        /// </summary>
        public void ApplyLinkedMatchSettings(RoomSettings settings, int limit)
        {
            if (settings == null || !settings.IsValid()) return;

            // Most patches are evidence, which leaves settings untouched. Without this every
            // remote click would rewrite settings.txt on the UI thread.
            int clampedLimit = Math.Clamp(limit, 0, 3);
            bool unchanged = settings.Difficulty == DifficultyIndex
                && settings.Map == MapSizeIndex
                && settings.CustomTier == CustomDurationIndex
                && settings.SpeedIndex == SpeedMultiplierToIndex(SpeedMultiplierSetting)
                && settings.HuntTier == ResolveHuntTier()
                && clampedLimit == EvidenceLimit;
            _authoritativeWeeklyHuntTier = settings.Difficulty == DiffWeekly ? settings.HuntTier : null;
            if (unchanged) return;

            DifficultyIndex = settings.Difficulty;
            MapSizeIndex = settings.Map;
            CustomDurationIndex = settings.CustomTier;
            SpeedMultiplierSetting = SpeedIndexToMultiplier(settings.SpeedIndex);
            EvidenceLimit = clampedLimit;

            if (settings.Difficulty == DiffWeekly)
            {
                var weekly = WeeklyDataService.GetWeekly();
                ActiveWeekly = weekly;
            }
            else
            {
                ActiveWeekly = null;
            }

            RecomputeHuntDuration();
            NotifyMatchSettingsChanged();
            PersistMatchSettings();
            LinkedSettingsApplied?.Invoke();
        }

        /// <summary>Raised after the room's settings land, so an open Settings window can refresh.</summary>
        public event Action? LinkedSettingsApplied;

        /// <summary>
        /// Writes the match keys in place. SettingsVersion=2 is mandatory beside Difficulty, or a
        /// genuinely selected Weekly mis-migrates to Custom on the next load.
        /// </summary>
        internal static object SettingsFileGate { get; } = new();

        private void PersistMatchSettings()
        {
            var updates = new Dictionary<string, string>
            {
                ["SettingsVersion"] = "2",
                ["Difficulty"] = DifficultyIndex.ToString(),
                ["MapSize"] = MapSizeIndex.ToString(),
                ["CustomDuration"] = CustomDurationIndex.ToString(),
                ["HuntDuration"] = ResolveHuntTier().ToString(),
                ["GhostSpeed"] = SpeedMultiplierToIndex(SpeedMultiplierSetting).ToString(),
                ["EvidenceLimit"] = EvidenceLimit.ToString()
            };

            // Off the UI thread: a disk stall here would land as a frame hitch over the game.
            Task.Run(() => WriteMatchSettings(updates));
        }

        private static void WriteMatchSettings(Dictionary<string, string> updates)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "settings.txt");
                if (!System.IO.File.Exists(path)) return;

                lock (SettingsFileGate)
                {
                    var lines = new List<string>(System.IO.File.ReadAllLines(path));
                    foreach (var pair in updates)
                    {
                        bool found = false;
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (!lines[i].StartsWith(pair.Key + "=")) continue;
                            lines[i] = $"{pair.Key}={pair.Value}";
                            found = true;
                            break;
                        }
                        if (found) continue;
                        int insertAt = lines.IndexOf("[Game Settings]") + 1;
                        if (insertAt > 0) lines.Insert(insertAt, $"{pair.Key}={pair.Value}");
                    }
                    System.IO.File.WriteAllLines(path, lines);
                }
            }
            catch { }
        }

        public static double SpeedIndexToMultiplier(int index) => index switch
        {
            0 => 0.5,
            1 => 0.75,
            3 => 1.25,
            4 => 1.5,
            _ => 1.0
        };

        public static int SpeedMultiplierToIndex(double multiplier)
        {
            if (multiplier <= 0.5) return 0;
            if (multiplier <= 0.75) return 1;
            if (multiplier <= 1.0) return 2;
            if (multiplier <= 1.25) return 3;
            return 4;
        }

        /// <summary>Ghost data as the room identifies it. Both clients hash the same file.</summary>
        public string LinkContentHash() => LinkProtocol.ContentHash(GhostDataService.GetGhostJson());
    }
}
