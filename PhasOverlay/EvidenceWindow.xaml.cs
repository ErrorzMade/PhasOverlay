using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace PhasOverlay
{
    public class SpeedOption
    {
        public string Display { get; set; } = string.Empty;
        public double Value { get; set; } = 0;
        public bool IsClickable { get; set; } = false;
    }

    public class EvidenceIcon
    {
        /// <summary>Short tag shown on the ghost card (e.g. "DOTS"). Space is tight there.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Full evidence name, surfaced as the tooltip so the tag is never ambiguous.</summary>
        public string FullName { get; set; } = string.Empty;

        public bool IsForcedVisible { get; set; } = false;

        /// <summary>0 unset, 1 confirmed by the user, 2 ruled out by the user.</summary>
        public int State { get; set; } = 0;
    }

    public class BehaviorItem
    {
        public string Prefix { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public string TargetGhost { get; set; } = string.Empty;
        public int ActionType { get; set; } = 0; // 1 for Mark, 2 for Rule Out
    }

    public class GhostData : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string ShortFact { get; set; } = string.Empty;
        public string Sanity { get; set; } = string.Empty;
        public string Tell { get; set; } = string.Empty;
        public string LosSpeedup { get; set; } = "Yes";
        public string LosTooltip { get; set; } = "LOS speedup is when a ghost gradually speeds up as it has continuous line of sight of you.";
        public List<string> EvidencePool { get; set; } = new List<string>();
        public string ForcedEvidence { get; set; } = string.Empty;

        private bool _showForcedUnderline = false;
        public bool ShowForcedUnderline
        {
            get { return _showForcedUnderline; }
            set
            {
                if (_showForcedUnderline != value)
                {
                    _showForcedUnderline = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EvidenceIcons));
                }
            }
        }

        private bool _isSpeedHighlighted = false;
        public bool IsSpeedHighlighted
        {
            get { return _isSpeedHighlighted; }
            set { _isSpeedHighlighted = value; OnPropertyChanged(); }
        }

        private List<string> _confirmedEvidence = new List<string>();
        private List<string> _ruledOutEvidence = new List<string>();

        /// <summary>
        /// Tints the card's evidence tags to match the user's own toggles. Below 3 evidence a
        /// ghost can hide evidence, so a surviving ghost may legitimately carry a ruled-out tag.
        /// That is the point: it shows the ghost is still possible despite the rule-out.
        /// </summary>
        public void SetEvidenceStates(List<string> confirmed, List<string> ruledOut)
        {
            _confirmedEvidence = confirmed;
            _ruledOutEvidence = ruledOut;
            OnPropertyChanged(nameof(EvidenceIcons));
        }

        public List<EvidenceIcon> EvidenceIcons
        {
            get
            {
                var icons = new List<EvidenceIcon>();
                foreach (var ev in EvidencePool)
                {
                    if (Name == "The Mimic" && ev == "Ghost Orb")
                        continue;

                    bool isForced = (ev == ForcedEvidence && ShowForcedUnderline);

                    int state = 0;
                    if (_confirmedEvidence.Contains(ev)) state = 1;
                    else if (_ruledOutEvidence.Contains(ev)) state = 2;

                    icons.Add(new EvidenceIcon
                    {
                        Label = ShortEvidenceLabel(ev),
                        FullName = ev,
                        IsForcedVisible = isForced,
                        State = state
                    });
                }
                return icons;
            }
        }

        /// <summary>
        /// Evidence tags shown on the ghost cards. Only the genuinely unambiguous abbreviations
        /// are shortened (EMF/DOTS/UV). The rest stay close to the in-game wording, because
        /// terse forms like "BOX"/"BOOK" were not clear enough. The full name is on each tag's
        /// tooltip, and the detail modal always spells evidence out in full. An unrecognised
        /// evidence type falls back to its own name rather than rendering blank.
        /// </summary>
        private static string ShortEvidenceLabel(string evidence)
        {
            switch (evidence)
            {
                case "EMF Level 5": return "EMF";
                case "D.O.T.S Projector": return "DOTS";
                case "Ultraviolet": return "UV";
                case "Freezing Temperatures": return "FREEZING";
                case "Ghost Orb": return "ORBS";
                case "Ghost Writing": return "WRITING";
                case "Spirit Box": return "SPIRIT BOX";
                default: return evidence.ToUpperInvariant();
            }
        }

        public List<SpeedOption> Speeds { get; set; } = new List<SpeedOption>();

        public double GraphBase { get; set; } = 1.7;
        public double GraphMax { get; set; } = 2.805;
        public double GraphTimeToMax { get; set; } = 13.0;
        public List<BehaviorDto> Behaviors { get; set; } = new List<BehaviorDto>();
        public bool HideBehaviorScroll { get; set; } = false;

        public bool CanBeSlow { get; set; } = false;
        public bool CanBeNormal { get; set; } = false;
        public bool CanBeFast { get; set; } = false;

        public bool CanHuntVeryEarly { get; set; } = false;
        public bool CanHuntEarly { get; set; } = false;
        public bool CanHuntNormal { get; set; } = false;
        public bool CanHuntLate { get; set; } = false;

        private int _visibleLineLimit = 2;
        public int VisibleLineLimit
        {
            get { return _visibleLineLimit; }
            set
            {
                if (_visibleLineLimit != value)
                {
                    _visibleLineLimit = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TruncatedTell));
                }
            }
        }

        public string TruncatedTell
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Tell)) return string.Empty;
                var lines = Tell.Split('\n');
                var result = new List<string>();

                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("•"))
                    {
                        result.Add(line.Trim());
                        if (result.Count == VisibleLineLimit) break;
                    }
                }

                return result.Count > 0 ? string.Join("\n\n", result) : (lines.Length > 0 ? lines[0] : Tell);
            }
        }

        private int _cardState = 0;
        public int CardState
        {
            get { return _cardState; }
            set { _cardState = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public partial class EvidenceWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        private static extern long mciSendString(string command, string returnValue, int returnLength, IntPtr winHandle);

        private MainWindow _main;
        private List<GhostData> _masterGhostList = new List<GhostData>();
        private List<GhostData> _visibleGhosts = new List<GhostData>();
        private int _evidenceGivenLimit = 3;

        private System.Windows.Threading.DispatcherTimer _playbackDurationTimer;
        private double _currentPlayingSpeed = 0;
        private DateTime _lastClickTime = DateTime.MinValue;

        private CancellationTokenSource? _footstepCancelToken;
        private const int AUDIO_POOL_SIZE = 16;
        private string[] _mciAliases = new string[AUDIO_POOL_SIZE];
        private int _currentAliasIndex = 0;
        private bool _aliasesLoaded = false;

        private enum EvidenceTutorialStep
        {
            ConfirmEvidence,
            RuleOutEvidence,
            ExpandGhost,
            CloseGhost,
            OpenSettings,
            ChangeMap,
            ResetTracker
        }

        private bool _tutorialMode = false;
        private EvidenceTutorialStep _tutorialStep;
        private Action<bool>? _tutorialFinished;
        private FrameworkElement? _tutorialTarget;
        private bool _tutorialTargetEmphasised;
        private object _tutorialTargetEnabledValue = DependencyProperty.UnsetValue;
        private object _tutorialTargetOpacityValue = DependencyProperty.UnsetValue;
        private object _tutorialTargetEffectValue = DependencyProperty.UnsetValue;
        private object _tutorialTargetStyleValue = DependencyProperty.UnsetValue;
        private object _tutorialCloseEnabledValue = DependencyProperty.UnsetValue;
        private object _tutorialCloseOpacityValue = DependencyProperty.UnsetValue;
        private object _tutorialCloseCursorValue = DependencyProperty.UnsetValue;
        private int _tutorialRun = 0;
        private int _tutorialOriginalMapSize;
        private int _tutorialMapBeforeChange;
        private bool _tutorialMatchSetupWasOpen;
        private bool?[] _tutorialEvidenceSnapshot = Array.Empty<bool?>();
        private bool[] _tutorialHuntSnapshot = Array.Empty<bool>();
        private bool[] _tutorialSpeedSnapshot = Array.Empty<bool>();
        private Dictionary<string, int> _tutorialGhostSnapshot = new Dictionary<string, int>();
        private readonly Dictionary<FrameworkElement, (object IsEnabled, object Opacity)> _tutorialControlSnapshot =
            new Dictionary<FrameworkElement, (object, object)>();
        private readonly Dictionary<ContentPresenter, (object Opacity, object IsHitTestVisible)> _tutorialGhostVisualSnapshot =
            new Dictionary<ContentPresenter, (object, object)>();

        public EvidenceWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            _playbackDurationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _playbackDurationTimer.Tick += (s, e) => StopFootstepPlayback();
            _evidenceGivenLimit = _main.EvidenceLimit;
            LoadGhostData();
            ApplyFilteringEngine();

            // Re-synced on every reshow in case Settings changed while the tracker was hidden.
            RefreshWeeklyComboItem();
            SyncMatchControls();
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    RefreshWeeklyComboItem();
                    SyncMatchControls();
                }
                else
                {
                    // Hiding via the evidence hotkey bypasses Close_Click/OnClosing, so without
                    // this the footsteps keep playing (and the ramp keeps running its per-frame
                    // dot animation) from a window you can no longer see.
                    StopGraphPlayback();
                    StopFootstepPlayback();
                }
            };

            _ = RefreshGhostDataAsync();
            _ = RefreshWeeklyDataAsync();

            this.Loaded += (s, e) => DisplayService.CenterOn(this, _main.DisplayIndex);
        }

        public void StartTutorial(Action<bool> finished)
        {
            if (_tutorialMode) return;

            _tutorialMode = true;
            _tutorialFinished = finished;
            _tutorialRun++;
            _tutorialOriginalMapSize = _main.MapSizeIndex;
            _tutorialMatchSetupWasOpen = BtnMatchSetup.IsChecked == true;
            _tutorialEvidenceSnapshot = Enumerable.Range(1, 7).Select(GetEvidenceState).ToArray();
            _tutorialHuntSnapshot = new[]
            {
                TglHuntVeryEarly.IsChecked == true,
                TglHuntEarly.IsChecked == true,
                TglHuntNormal.IsChecked == true,
                TglHuntLate.IsChecked == true
            };
            _tutorialSpeedSnapshot = new[]
            {
                TglSpeedSlow.IsChecked == true,
                TglSpeedNormal.IsChecked == true,
                TglSpeedFast.IsChecked == true
            };
            _tutorialGhostSnapshot = _masterGhostList.ToDictionary(g => g.Name, g => g.CardState);
            CaptureTutorialControlState();
            _tutorialCloseEnabledValue = BtnCloseWindow.ReadLocalValue(UIElement.IsEnabledProperty);
            _tutorialCloseOpacityValue = BtnCloseWindow.ReadLocalValue(UIElement.OpacityProperty);
            _tutorialCloseCursorValue = BtnCloseWindow.ReadLocalValue(FrameworkElement.CursorProperty);

            ResetTracker();
            BtnMatchSetup.IsChecked = false;
            BtnCloseWindow.IsEnabled = false;
            BtnCloseWindow.Opacity = 0.28;
            BtnCloseWindow.Cursor = Cursors.Arrow;
            GhostModalOverlay.Visibility = Visibility.Collapsed;
            TutorialCoach.Visibility = Visibility.Visible;
            _tutorialStep = EvidenceTutorialStep.ConfirmEvidence;
            ShowTutorialStep();

            if (!IsVisible) Show();
            Activate();
        }

        private void ShowTutorialStep()
        {
            ClearTutorialTarget();
            RestoreGhostTutorialVisuals();
            DimTutorialControls();

            int number = (int)_tutorialStep + 1;
            TutorialStepText.Text = $"{number} OF 7";
            TutorialCoach.VerticalAlignment = VerticalAlignment.Top;
            TutorialCoach.Margin = new Thickness(0, 18, 0, 0);

            switch (_tutorialStep)
            {
                case EvidenceTutorialStep.ConfirmEvidence:
                    TutorialTitle.Text = "Confirm Evidence";
                    TutorialBody.Text = "Click EMF Level 5 once to confirm it. Watch the possible ghost list narrow immediately.";
                    AllowTutorialTarget(ChkEmf);
                    break;

                case EvidenceTutorialStep.RuleOutEvidence:
                    TutorialTitle.Text = "Rule Out Evidence";
                    TutorialBody.Text = ChkDots.IsChecked == true
                        ? "D.O.T.S is selected. Click it once more to rule it out."
                        : "Click D.O.T.S Projector twice. The first click confirms it and the second rules it out.";
                    AllowTutorialTarget(ChkDots);
                    break;

                case EvidenceTutorialStep.ExpandGhost:
                    TutorialTitle.Text = "Inspect A Ghost";
                    TutorialBody.Text = "Use the plus button on Dayan to open its full information.";
                    GhostScrollViewer.IsEnabled = true;
                    GhostScrollViewer.Opacity = 1.0;
                    DimGhostsExcept("Dayan");

                    Button? dayanExpand = FindGhostExpandButton("Dayan");
                    if (dayanExpand != null)
                    {
                        AllowTutorialTarget(dayanExpand, true);
                    }
                    else
                    {
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!_tutorialMode || _tutorialStep != EvidenceTutorialStep.ExpandGhost) return;
                            Button? delayedTarget = FindGhostExpandButton("Dayan");
                            if (delayedTarget != null) AllowTutorialTarget(delayedTarget, true);
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                    break;

                case EvidenceTutorialStep.CloseGhost:
                    TutorialTitle.Text = "Read The Details";
                    TutorialBody.Text = "This view shows the ghost's evidence, speed, hunt sanity and identifying behaviours. Close it when you are ready.";
                    AllowTutorialTarget(BtnCloseGhostModal);
                    break;

                case EvidenceTutorialStep.OpenSettings:
                    TutorialTitle.Text = "Open Game Settings";
                    TutorialBody.Text = "Open Game Settings to keep the tracker matched to the current contract.";
                    AllowTutorialTarget(BtnMatchSetup);
                    break;

                case EvidenceTutorialStep.ChangeMap:
                    TutorialTitle.Text = "Change The Map Size";
                    TutorialBody.Text = "Change the map size. Match settings keep hunt timing and ghost information accurate.";
                    _tutorialMapBeforeChange = CtxMap.SelectedIndex;
                    TutorialCoach.VerticalAlignment = VerticalAlignment.Bottom;
                    TutorialCoach.Margin = new Thickness(0, 0, 0, 18);
                    AllowTutorialTarget(CtxMap);
                    break;

                case EvidenceTutorialStep.ResetTracker:
                    TutorialTitle.Text = "Reset The Tracker";
                    TutorialBody.Text = "Reset the tracker when a new investigation begins. This clears the tutorial choices too.";
                    AllowTutorialTarget(BtnResetTracker);
                    break;
            }
        }

        private FrameworkElement[] GetTutorialControls()
        {
            return new FrameworkElement[]
            {
                ChkEmf, ChkDots, ChkUv, ChkFreezing, ChkOrb, ChkWriting, ChkBox,
                TglHuntVeryEarly, TglHuntEarly, TglHuntNormal, TglHuntLate,
                TglSpeedSlow, TglSpeedNormal, TglSpeedFast,
                TglEv0, TglEv1, TglEv2, TglEv3,
                BtnResetTracker, BtnMatchSetup, GhostScrollViewer,
                CtxDifficulty, CtxMap, CtxHunt, CtxSpeed
            };
        }

        private void CaptureTutorialControlState()
        {
            _tutorialControlSnapshot.Clear();
            foreach (FrameworkElement control in GetTutorialControls())
            {
                _tutorialControlSnapshot[control] =
                    (control.ReadLocalValue(UIElement.IsEnabledProperty), control.ReadLocalValue(UIElement.OpacityProperty));
            }
        }

        private void DimTutorialControls()
        {
            foreach (FrameworkElement control in GetTutorialControls())
            {
                control.IsEnabled = false;
                control.Opacity = 0.42;
            }
        }

        private void RestoreTutorialControlState()
        {
            foreach (KeyValuePair<FrameworkElement, (object IsEnabled, object Opacity)> entry in _tutorialControlSnapshot)
            {
                RestoreLocalValue(entry.Key, UIElement.IsEnabledProperty, entry.Value.IsEnabled);
                RestoreLocalValue(entry.Key, UIElement.OpacityProperty, entry.Value.Opacity);
            }
            _tutorialControlSnapshot.Clear();
        }

        private void AllowTutorialTarget(FrameworkElement target, bool emphasise = false)
        {
            _tutorialTargetEnabledValue = target.ReadLocalValue(UIElement.IsEnabledProperty);
            _tutorialTargetOpacityValue = target.ReadLocalValue(UIElement.OpacityProperty);
            _tutorialTargetEffectValue = target.ReadLocalValue(UIElement.EffectProperty);
            target.IsEnabled = true;
            target.Opacity = 1.0;
            if (emphasise && target is Button button)
            {
                _tutorialTargetStyleValue = button.ReadLocalValue(FrameworkElement.StyleProperty);
                button.Style = (Style)FindResource("TutorialExpandButtonStyle");
            }
            target.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString(emphasise ? "#FFC66BFF" : "#FFB455FF"),
                BlurRadius = emphasise ? 36 : 18,
                ShadowDepth = 0,
                Opacity = emphasise ? 1.0 : 0.9
            };
            _tutorialTarget = target;
            _tutorialTargetEmphasised = emphasise;
        }

        private Button? FindGhostExpandButton(string ghostName)
        {
            GhostData? ghost = _visibleGhosts.FirstOrDefault(g => g.Name == ghostName);
            if (ghost == null) return null;

            GhostItemsControl.UpdateLayout();
            if (GhostItemsControl.ItemContainerGenerator.ContainerFromItem(ghost) is not ContentPresenter presenter)
            {
                return null;
            }

            DataTemplate? template = presenter.ContentTemplate ?? GhostItemsControl.ItemTemplate;
            return template?.FindName("ExpandGhostButton", presenter) as Button;
        }

        private void DimGhostsExcept(string ghostName)
        {
            GhostItemsControl.UpdateLayout();
            _tutorialGhostVisualSnapshot.Clear();
            foreach (object item in GhostItemsControl.Items)
            {
                if (item is not GhostData ghost
                    || GhostItemsControl.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter presenter)
                {
                    continue;
                }

                _tutorialGhostVisualSnapshot[presenter] =
                    (presenter.ReadLocalValue(UIElement.OpacityProperty),
                     presenter.ReadLocalValue(UIElement.IsHitTestVisibleProperty));
                bool isTarget = ghost.Name == ghostName;
                presenter.Opacity = isTarget ? 1.0 : 0.25;
                presenter.IsHitTestVisible = isTarget;
            }
        }

        private void RestoreGhostTutorialVisuals()
        {
            foreach (KeyValuePair<ContentPresenter, (object Opacity, object IsHitTestVisible)> entry
                     in _tutorialGhostVisualSnapshot)
            {
                RestoreLocalValue(entry.Key, UIElement.OpacityProperty, entry.Value.Opacity);
                RestoreLocalValue(entry.Key, UIElement.IsHitTestVisibleProperty, entry.Value.IsHitTestVisible);
            }
            _tutorialGhostVisualSnapshot.Clear();
        }

        private void ClearTutorialTarget()
        {
            if (_tutorialTarget != null)
            {
                RestoreLocalValue(_tutorialTarget, UIElement.IsEnabledProperty, _tutorialTargetEnabledValue);
                RestoreLocalValue(_tutorialTarget, UIElement.OpacityProperty, _tutorialTargetOpacityValue);
                RestoreLocalValue(_tutorialTarget, UIElement.EffectProperty, _tutorialTargetEffectValue);
                if (_tutorialTargetEmphasised && _tutorialTarget is Button button)
                {
                    RestoreLocalValue(button, FrameworkElement.StyleProperty, _tutorialTargetStyleValue);
                }
            }
            _tutorialTarget = null;
            _tutorialTargetEmphasised = false;
            _tutorialTargetEnabledValue = DependencyProperty.UnsetValue;
            _tutorialTargetOpacityValue = DependencyProperty.UnsetValue;
            _tutorialTargetEffectValue = DependencyProperty.UnsetValue;
            _tutorialTargetStyleValue = DependencyProperty.UnsetValue;
        }

        private static void RestoreLocalValue(DependencyObject target, DependencyProperty property, object value)
        {
            if (value == DependencyProperty.UnsetValue) target.ClearValue(property);
            else target.SetValue(property, value);
        }

        private void AdvanceTutorial()
        {
            if (!_tutorialMode || _tutorialStep == EvidenceTutorialStep.ResetTracker) return;
            _tutorialStep++;
            ShowTutorialStep();
        }

        private async void CompleteTutorial()
        {
            if (!_tutorialMode) return;

            int run = _tutorialRun;
            ClearTutorialTarget();
            DimTutorialControls();
            TutorialStepText.Text = "DONE";
            TutorialTitle.Text = "Evidence Basics Complete";
            TutorialBody.Text = "You can now narrow the ghost list, inspect candidates and keep each investigation configured correctly.";

            await Task.Delay(TimeSpan.FromSeconds(1.5));
            if (_tutorialMode && _tutorialRun == run) EndTutorial(true);
        }

        private void EndTutorial(bool completed)
        {
            if (!_tutorialMode) return;

            Action<bool>? finished = _tutorialFinished;
            _tutorialMode = false;
            _tutorialFinished = null;
            _tutorialRun++;

            ClearTutorialTarget();
            RestoreGhostTutorialVisuals();
            RestoreLocalValue(BtnCloseWindow, UIElement.IsEnabledProperty, _tutorialCloseEnabledValue);
            RestoreLocalValue(BtnCloseWindow, UIElement.OpacityProperty, _tutorialCloseOpacityValue);
            RestoreLocalValue(BtnCloseWindow, FrameworkElement.CursorProperty, _tutorialCloseCursorValue);
            _tutorialCloseEnabledValue = DependencyProperty.UnsetValue;
            _tutorialCloseOpacityValue = DependencyProperty.UnsetValue;
            _tutorialCloseCursorValue = DependencyProperty.UnsetValue;
            TutorialCoach.Visibility = Visibility.Collapsed;
            GhostModalOverlay.Visibility = Visibility.Collapsed;
            BtnMatchSetup.IsChecked = _tutorialMatchSetupWasOpen;
            RestoreTutorialControlState();

            if (_tutorialEvidenceSnapshot.Length == 7)
            {
                ChkEmf.IsChecked = _tutorialEvidenceSnapshot[0];
                ChkDots.IsChecked = _tutorialEvidenceSnapshot[1];
                ChkUv.IsChecked = _tutorialEvidenceSnapshot[2];
                ChkFreezing.IsChecked = _tutorialEvidenceSnapshot[3];
                ChkOrb.IsChecked = _tutorialEvidenceSnapshot[4];
                ChkWriting.IsChecked = _tutorialEvidenceSnapshot[5];
                ChkBox.IsChecked = _tutorialEvidenceSnapshot[6];
            }
            if (_tutorialHuntSnapshot.Length == 4)
            {
                TglHuntVeryEarly.IsChecked = _tutorialHuntSnapshot[0];
                TglHuntEarly.IsChecked = _tutorialHuntSnapshot[1];
                TglHuntNormal.IsChecked = _tutorialHuntSnapshot[2];
                TglHuntLate.IsChecked = _tutorialHuntSnapshot[3];
            }
            if (_tutorialSpeedSnapshot.Length == 3)
            {
                TglSpeedSlow.IsChecked = _tutorialSpeedSnapshot[0];
                TglSpeedNormal.IsChecked = _tutorialSpeedSnapshot[1];
                TglSpeedFast.IsChecked = _tutorialSpeedSnapshot[2];
            }
            foreach (GhostData ghost in _masterGhostList)
            {
                if (_tutorialGhostSnapshot.TryGetValue(ghost.Name, out int state)) ghost.CardState = state;
            }

            _main.MapSizeIndex = _tutorialOriginalMapSize;
            _main.RecomputeHuntDuration();
            SyncMatchControls();
            ApplyFilteringEngine();
            Hide();
            finished?.Invoke(completed);
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

            Dispatcher.Invoke(() =>
            {
                RefreshStaleDataNotice();
                if (!changed) return;

                RefreshWeeklyComboItem();
                if (_main.DifficultyIndex == MainWindow.DiffWeekly)
                {
                    var w = WeeklyDataService.GetWeekly();
                    if (w != null) _main.ApplyWeekly(w);
                    SyncMatchControls();
                }
            });
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        public bool? GetEvidenceState(int index)
        {
            switch (index)
            {
                case 1: return ChkEmf.IsChecked;
                case 2: return ChkDots.IsChecked;
                case 3: return ChkUv.IsChecked;
                case 4: return ChkFreezing.IsChecked;
                case 5: return ChkOrb.IsChecked;
                case 6: return ChkWriting.IsChecked;
                case 7: return ChkBox.IsChecked;
                default: return null;
            }
        }

        public string CycleEvidence(int index)
        {
            CheckBox cb = null;
            switch (index)
            {
                case 1: cb = ChkEmf; break;
                case 2: cb = ChkDots; break;
                case 3: cb = ChkUv; break;
                case 4: cb = ChkFreezing; break;
                case 5: cb = ChkOrb; break;
                case 6: cb = ChkWriting; break;
                case 7: cb = ChkBox; break;
            }

            if (cb == null || !cb.IsEnabled) return null;

            if (cb.IsChecked == null) cb.IsChecked = true;
            else if (cb.IsChecked == true) cb.IsChecked = false;
            else cb.IsChecked = null;

            ApplyFilteringEngine();

            string status = cb.IsChecked == true ? "Selected" : cb.IsChecked == false ? "Ruled Out" : "Deselected";
            return $"{cb.Content}: {status}";
        }

        private List<SpeedOption> ParseSpeeds(string raw)
        {
            var list = new List<SpeedOption>();
            if (raw == "Varies")
            {
                list.Add(new SpeedOption { Display = "Varies", IsClickable = false });
                return list;
            }

            string separator = "";
            if (raw.Contains(" - "))
            {
                separator = " - ";
            }
            else if (raw.Contains(" / "))
            {
                separator = " / ";
            }

            if (!string.IsNullOrEmpty(separator))
            {
                var parts = raw.Split(new string[] { separator }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++)
                {
                    list.Add(new SpeedOption { Display = parts[i], Value = double.Parse(parts[i]), IsClickable = true });

                    if (i < parts.Length - 1)
                    {
                        list.Add(new SpeedOption { Display = separator, IsClickable = false });
                    }
                }
            }
            else
            {
                list.Add(new SpeedOption { Display = raw, Value = double.Parse(raw), IsClickable = true });
            }

            return list;
        }

        private void LoadGhostData()
        {
            _masterGhostList = new List<GhostData>();

            try
            {
                var file = JsonSerializer.Deserialize<GhostFileDto>(GhostDataService.GetGhostJson(), GhostDataService.Json);
                if (file?.Ghosts != null)
                {
                    foreach (var dto in file.Ghosts)
                        _masterGhostList.Add(MapGhost(dto));
                }
            }
            catch { }

            GhostItemsControl.ItemsSource = _masterGhostList;
        }

        private GhostData MapGhost(GhostDto dto)
        {
            var canBe = dto.CanBe ?? new List<string>();
            var canHunt = dto.CanHunt ?? new List<string>();

            var g = new GhostData
            {
                Name = dto.Name,
                ShortFact = dto.ShortFact,
                Speeds = ParseSpeeds(dto.Speed),
                Sanity = dto.Sanity,
                Tell = dto.Tell,
                LosSpeedup = string.IsNullOrEmpty(dto.LosSpeedup) ? "Yes" : dto.LosSpeedup,
                EvidencePool = dto.Evidence ?? new List<string>(),
                ForcedEvidence = dto.ForcedEvidence ?? string.Empty,
                CanBeSlow = canBe.Contains("slow"),
                CanBeNormal = canBe.Contains("normal"),
                CanBeFast = canBe.Contains("fast"),
                CanHuntVeryEarly = canHunt.Contains("veryearly"),
                CanHuntEarly = canHunt.Contains("early"),
                CanHuntNormal = canHunt.Contains("normal"),
                CanHuntLate = canHunt.Contains("late")
            };
            if (!string.IsNullOrEmpty(dto.LosTooltip)) g.LosTooltip = dto.LosTooltip;

            g.Behaviors = dto.Behaviors ?? new List<BehaviorDto>();
            g.HideBehaviorScroll = dto.HideBehaviorScroll == true;
            if (dto.SpeedGraph != null)
            {
                g.GraphBase = dto.SpeedGraph.Base;
                g.GraphMax = dto.SpeedGraph.Max;
                g.GraphTimeToMax = dto.SpeedGraph.TimeToMax;
            }
            return g;
        }

        private async Task RefreshGhostDataAsync()
        {
            bool changed = await GhostDataService.CheckForUpdatesAsync();

            Dispatcher.Invoke(() =>
            {
                // Runs even when nothing changed: a refused too-new file also reports "not changed".
                RefreshStaleDataNotice();
                if (!changed) return;

                LoadGhostData();
                ApplyFilteringEngine();
            });
        }

        /// <summary>
        /// Shows the banner when remote data declares a schema this build can't read. The data
        /// itself still works (last good copy), so this explains the staleness rather than an
        /// apparently broken tracker.
        /// </summary>
        private void RefreshStaleDataNotice()
        {
            bool ghosts = GhostDataService.DataTooNew;
            bool weekly = WeeklyDataService.DataTooNew;
            if (!ghosts && !weekly)
            {
                StaleDataBar.Visibility = Visibility.Collapsed;
                return;
            }

            string what = ghosts && weekly ? "Ghost and weekly challenge data"
                        : ghosts ? "Ghost data"
                        : "Weekly challenge data";

            StaleDataText.Text = $"{what} needs a newer version of PhasOverlay. "
                               + "You're still seeing the last version this build can read.";
            StaleDataBar.Visibility = Visibility.Visible;
        }

        private void StaleData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(UpdateService.DefaultReleasesUrl) { UseShellExecute = true });
            }
            catch { }
        }

        public void SetHighlightedSpeed(double tappedSpeed)
        {
            foreach (var ghost in _masterGhostList)
            {
                bool matches = false;

                foreach (var spd in ghost.Speeds)
                {
                    if (spd.Display == "Varies")
                    {
                        matches = true;
                        break;
                    }

                    if (spd.IsClickable)
                    {
                        if (Math.Abs(spd.Value - tappedSpeed) <= 0.15)
                        {
                            matches = true;
                            break;
                        }
                    }
                }

                ghost.IsSpeedHighlighted = matches;
            }
            PushPossibleGhostsToOverlay();
        }

        public void ClearSpeedHighlight()
        {
            foreach (var ghost in _masterGhostList)
            {
                ghost.IsSpeedHighlighted = false;
            }
            PushPossibleGhostsToOverlay();
        }

        private void Evidence_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is CheckBox cb)
            {
                e.Handled = true;
                if (!cb.IsEnabled) return;
                if (_tutorialMode
                    && !(_tutorialStep == EvidenceTutorialStep.ConfirmEvidence && cb == ChkEmf)
                    && !(_tutorialStep == EvidenceTutorialStep.RuleOutEvidence && cb == ChkDots))
                {
                    return;
                }

                if (cb.IsChecked == null) cb.IsChecked = true;
                else if (cb.IsChecked == true) cb.IsChecked = false;
                else cb.IsChecked = null;

                ApplyFilteringEngine();

                if (_tutorialMode && _tutorialStep == EvidenceTutorialStep.ConfirmEvidence
                    && cb == ChkEmf && cb.IsChecked == true)
                {
                    AdvanceTutorial();
                }
                else if (_tutorialMode && _tutorialStep == EvidenceTutorialStep.RuleOutEvidence && cb == ChkDots)
                {
                    if (cb.IsChecked == false) AdvanceTutorial();
                    else ShowTutorialStep();
                }
            }
        }

        private void EvCount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton clicked && clicked.IsChecked == true)
            {
                if (clicked != TglEv0) TglEv0.IsChecked = false;
                if (clicked != TglEv1) TglEv1.IsChecked = false;
                if (clicked != TglEv2) TglEv2.IsChecked = false;
                if (clicked != TglEv3) TglEv3.IsChecked = false;

                _evidenceGivenLimit = clicked == TglEv0 ? 0 : clicked == TglEv1 ? 1 : clicked == TglEv2 ? 2 : 3;

                _main.EvidenceLimit = _evidenceGivenLimit;
                SaveEvidenceLimitToSettings();

                ApplyFilteringEngine();
            }
        }


        private void SaveEvidenceLimitToSettings()
        {
            try
            {
                string configPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "settings.txt");
                if (System.IO.File.Exists(configPath))
                {
                    var lines = new List<string>(System.IO.File.ReadAllLines(configPath));
                    bool found = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith("EvidenceLimit="))
                        {
                            lines[i] = $"EvidenceLimit={_evidenceGivenLimit}";
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        int insertIdx = lines.IndexOf("[Game Settings]") + 1;
                        if (insertIdx > 0) lines.Insert(insertIdx, $"EvidenceLimit={_evidenceGivenLimit}");
                    }

                    System.IO.File.WriteAllLines(configPath, lines);
                }
            }
            catch { }
        }

        private struct FilterCriteria
        {
            public bool Speed, Slow, Normal, Fast;
            public bool Hunt, HuntVeryEarly, HuntEarly, HuntNormal, HuntLate;
        }

        /// <summary>
        /// Whether one ghost survives the current evidence, speed and hunt filters. Extracted so
        /// the "could this evidence still happen" test runs the exact same rules as the real
        /// filter, including the Mimic's fake orb and the forced-evidence handling below 3.
        /// </summary>
        private bool GhostPassesFilters(GhostData ghost, List<string> confirmed, List<string> ruledOut, FilterCriteria f)
        {
            foreach (var ev in confirmed)
            {
                if (!ghost.EvidencePool.Contains(ev)) return false;
            }

            foreach (var ev in ruledOut)
            {
                if (_evidenceGivenLimit == 3)
                {
                    if (ghost.EvidencePool.Contains(ev)) return false;
                }
                else
                {
                    if (ghost.ForcedEvidence == ev) return false;
                }
            }

            int actualCountForGhost = confirmed.Count;
            if (ghost.Name == "The Mimic" && confirmed.Contains("Ghost Orb"))
            {
                actualCountForGhost--;
            }
            if (actualCountForGhost > _evidenceGivenLimit) return false;

            if (_evidenceGivenLimit > 0 && _evidenceGivenLimit < 3 && !string.IsNullOrEmpty(ghost.ForcedEvidence) && ghost.Name != "The Mimic")
            {
                if (actualCountForGhost >= _evidenceGivenLimit && !confirmed.Contains(ghost.ForcedEvidence)) return false;
            }

            if (f.Speed)
            {
                bool speedMatch = false;
                if (f.Slow && ghost.CanBeSlow) speedMatch = true;
                if (f.Normal && ghost.CanBeNormal) speedMatch = true;
                if (f.Fast && ghost.CanBeFast) speedMatch = true;

                if (!speedMatch) return false;
            }

            if (f.Hunt)
            {
                bool huntMatch = false;
                if (f.HuntVeryEarly && ghost.CanHuntVeryEarly) huntMatch = true;
                if (f.HuntEarly && ghost.CanHuntEarly) huntMatch = true;
                if (f.HuntNormal && ghost.CanHuntNormal) huntMatch = true;
                if (f.HuntLate && ghost.CanHuntLate) huntMatch = true;

                if (!huntMatch) return false;
            }

            return true;
        }

        /// <summary>Would confirming this evidence still leave at least one ghost alive?</summary>
        private bool AnyGhostPossibleWith(string evidence, List<string> confirmed, List<string> ruledOut, FilterCriteria f)
        {
            var test = new List<string>(confirmed) { evidence };

            foreach (var ghost in _masterGhostList)
            {
                if (GhostPassesFilters(ghost, test, ruledOut, f)) return true;
            }
            return false;
        }

        // Evidence the engine ruled out on the user's behalf. Tracked so it can be undone the
        // moment the combination that made it impossible changes, without clearing a real
        // user-set rule-out.
        private readonly HashSet<CheckBox> _autoRuledOut = new();

        private void ApplyFilteringEngine()
        {
            // Recomputed from scratch every pass, so releasing a constraint unlocks them again.
            foreach (var cb in _autoRuledOut) cb.IsChecked = null;
            _autoRuledOut.Clear();

            var confirmed = new List<string>();
            var ruledOut = new List<string>();
            var confirmedBoxes = new List<CheckBox>();

            CheckBox[] allBoxes = { ChkEmf, ChkDots, ChkUv, ChkFreezing, ChkOrb, ChkWriting, ChkBox };

            foreach (var cb in allBoxes)
            {
                if (cb.IsChecked == true)
                {
                    confirmed.Add(cb.Content.ToString());
                    confirmedBoxes.Add(cb);
                }
                else if (cb.IsChecked == false)
                {
                    ruledOut.Add(cb.Content.ToString());
                }
            }

            bool filterSpeed = TglSpeedSlow.IsChecked == true || TglSpeedNormal.IsChecked == true || TglSpeedFast.IsChecked == true;
            bool filterSlow = TglSpeedSlow.IsChecked == true;
            bool filterNormal = TglSpeedNormal.IsChecked == true;
            bool filterFast = TglSpeedFast.IsChecked == true;

            bool filterHunt = TglHuntVeryEarly.IsChecked == true || TglHuntEarly.IsChecked == true || TglHuntNormal.IsChecked == true || TglHuntLate.IsChecked == true;
            bool filterHuntVeryEarly = TglHuntVeryEarly.IsChecked == true;
            bool filterHuntEarly = TglHuntEarly.IsChecked == true;
            bool filterHuntNormal = TglHuntNormal.IsChecked == true;
            bool filterHuntLate = TglHuntLate.IsChecked == true;

            var criteria = new FilterCriteria
            {
                Speed = filterSpeed,
                Slow = filterSlow,
                Normal = filterNormal,
                Fast = filterFast,
                Hunt = filterHunt,
                HuntVeryEarly = filterHuntVeryEarly,
                HuntEarly = filterHuntEarly,
                HuntNormal = filterHuntNormal,
                HuntLate = filterHuntLate
            };

            var visibleGhosts = new List<GhostData>();
            bool mimicIsValid = false;

            foreach (var ghost in _masterGhostList)
            {
                ghost.ShowForcedUnderline = _evidenceGivenLimit <= 2;
                ghost.SetEvidenceStates(confirmed, ruledOut);

                if (!GhostPassesFilters(ghost, confirmed, ruledOut, criteria)) continue;

                if (ghost.Name == "The Mimic") mimicIsValid = true;
                visibleGhosts.Add(ghost);
            }

            int dynamicLineLimit = 2;
            if (visibleGhosts.Count <= 3) dynamicLineLimit = 10;
            else if (visibleGhosts.Count <= 6) dynamicLineLimit = 4;
            else dynamicLineLimit = 2;

            foreach (var ghost in visibleGhosts)
            {
                ghost.VisibleLineLimit = dynamicLineLimit;
            }

            GhostItemsControl.ItemsSource = visibleGhosts;

            int limit = _evidenceGivenLimit;
            int currentTotal = confirmed.Count;

            List<string> mimicEvidence = new List<string> { "Spirit Box", "Ultraviolet", "Freezing Temperatures", "Ghost Orb" };

            foreach (var cb in allBoxes)
            {
                if (cb.IsChecked != null)
                {
                    cb.IsEnabled = true;
                    cb.Opacity = 1.0;
                }
                else
                {
                    bool canEnable = false;
                    string evName = cb.Content?.ToString() ?? "";

                    if (currentTotal < limit)
                    {
                        canEnable = true;
                    }
                    else if (currentTotal == limit)
                    {
                        bool isBoxMimicEv = mimicEvidence.Contains(evName);
                        bool willContainOrb = confirmed.Contains("Ghost Orb") || evName == "Ghost Orb";

                        if (mimicIsValid && isBoxMimicEv && willContainOrb)
                        {
                            canEnable = true;
                        }
                    }

                    // Only at 3 evidence. Below that a ghost can hide evidence, so "no ghost has
                    // this" is not the same as "it cannot be this" and locking it would be wrong.
                    if (canEnable && _evidenceGivenLimit == 3
                        && !AnyGhostPossibleWith(evName, confirmed, ruledOut, criteria))
                    {
                        cb.IsChecked = false;
                        _autoRuledOut.Add(cb);
                        canEnable = false;
                    }

                    cb.IsEnabled = canEnable;
                    cb.Opacity = canEnable ? 1.0 : 0.3;
                    GhostScrollViewer.ScrollToTop();
                }
            }

            _visibleGhosts = visibleGhosts;
            _main?.SyncEvidenceUI();
            PushPossibleGhostsToOverlay();
        }

        // Header-badge colours: muted for "still narrowing", accent when down to one ghost
        // (effectively identified), danger when the evidence combination matches nothing.
        private static readonly Brush CountMutedBrush = FreezeBrush("#FFAAAAAA");
        private static readonly Brush CountAccentBrush = FreezeBrush("#FFB455FF");
        private static readonly Brush CountDangerBrush = FreezeBrush("#FFFF5555");
        private static Brush FreezeBrush(string hex)
        {
            Brush b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Feeds the overlay's POSSIBLE GHOSTS panel and refreshes the header count. Ghosts the
        /// user has manually ruled out (CardState 2) are dropped here rather than in the filtering
        /// engine, so the tracker's own list keeps showing them struck through. That card is how
        /// you un-rule them out.
        /// </summary>
        private void PushPossibleGhostsToOverlay()
        {
            var possible = _visibleGhosts.Where(g => g.CardState != 2).ToList();
            UpdateGhostCount(possible.Count);
            _main?.UpdatePossibleGhostsUI(possible);
        }

        private static readonly double[] SpeedMults = { 0.5, 0.75, 1.0, 1.25, 1.5 };
        private static int SpeedMultToIndex(double m)
        {
            for (int i = 0; i < SpeedMults.Length; i++) if (Math.Abs(SpeedMults[i] - m) < 0.001) return i;
            return 2; // 100%
        }

        // Suppresses the change handler while we programmatically load the match combos.
        private bool _loadingMatch = false;

        /// <summary>Mirrors the match-setup combos + evidence pills from MainWindow's values.</summary>
        public void SyncMatchControls()
        {
            if (CtxDifficulty == null || _main == null) return;

            _loadingMatch = true;
            CtxDifficulty.SelectedIndex = Math.Clamp(_main.DifficultyIndex, 0, MainWindow.DiffCustom);
            CtxMap.SelectedIndex = Math.Clamp(_main.MapSizeIndex, 0, 2);
            CtxSpeed.SelectedIndex = SpeedMultToIndex(_main.SpeedMultiplierSetting);

            bool custom = _main.DifficultyIndex == MainWindow.DiffCustom;
            bool weekly = _main.DifficultyIndex == MainWindow.DiffWeekly;

            // Hunt tier: editable only on Custom; otherwise it shows the derived/weekly tier.
            CtxHunt.IsEnabled = custom;
            CtxHunt.SelectedIndex = _main.ResolveHuntTier();

            // Ghost speed: editable only on Custom (presets + weekly are fixed).
            CtxSpeed.IsEnabled = custom;
            CtxSpeed.Opacity = custom ? 1.0 : 0.5;
            if (!custom && !weekly) CtxSpeed.SelectedIndex = 2; // presets -> 100%

            // Map size: locked only on Weekly (a fixed challenge map).
            CtxMap.IsEnabled = !weekly;
            CtxMap.Opacity = weekly ? 0.5 : 1.0;
            _loadingMatch = false;

            int prevLimit = _evidenceGivenLimit;
            _evidenceGivenLimit = _main.EvidenceLimit;
            SyncEvidencePills();
            if (_evidenceGivenLimit != prevLimit) ApplyFilteringEngine();
            if (_tutorialMode) ShowTutorialStep();
        }

        /// <summary>Reflects the evidence-given limit into the pills, and locks them on Weekly.</summary>
        private void SyncEvidencePills()
        {
            TglEv0.IsChecked = _evidenceGivenLimit == 0;
            TglEv1.IsChecked = _evidenceGivenLimit == 1;
            TglEv2.IsChecked = _evidenceGivenLimit == 2;
            TglEv3.IsChecked = _evidenceGivenLimit == 3;

            bool weekly = _main.DifficultyIndex == MainWindow.DiffWeekly;
            TglEv0.IsEnabled = !weekly;
            TglEv1.IsEnabled = !weekly;
            TglEv2.IsEnabled = !weekly;
            TglEv3.IsEnabled = !weekly;
        }

        /// <summary>Shows/hides the match-setup bar. Collapsed by default so it costs no space.</summary>
        private void MatchToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (MatchBar == null) return;
            MatchBar.Visibility = BtnMatchSetup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (_tutorialMode && _tutorialStep == EvidenceTutorialStep.OpenSettings
                && BtnMatchSetup.IsChecked == true)
            {
                AdvanceTutorial();
            }
        }

        /// <summary>Applies an edit from the match-setup combos live and persists it.</summary>
        private void CtxMatch_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingMatch || _main == null) return;

            if (CtxDifficulty.SelectedIndex == MainWindow.DiffWeekly)
            {
                var w = WeeklyDataService.GetWeekly();
                if (w != null)
                {
                    _main.ApplyWeekly(w);
                    SyncMatchControls();
                    if (!_tutorialMode) SaveMatchSettings();
                    return;
                }
                _loadingMatch = true;
                CtxDifficulty.SelectedIndex = 4;
                _loadingMatch = false;
            }

            _main.ActiveWeekly = null;
            _main.DifficultyIndex = CtxDifficulty.SelectedIndex >= 0 ? CtxDifficulty.SelectedIndex : 1;
            _main.MapSizeIndex = CtxMap.SelectedIndex >= 0 ? CtxMap.SelectedIndex : 0;

            // Reaching here means the selection is not Weekly, so release the Weekly-only locks.
            CtxMap.IsEnabled = true;
            CtxMap.Opacity = 1.0;
            SyncEvidencePills();

            bool custom = _main.DifficultyIndex == MainWindow.DiffCustom;

            // Presets are locked to 100% ghost speed; snap back if we just left Custom.
            CtxSpeed.IsEnabled = custom;
            CtxSpeed.Opacity = custom ? 1.0 : 0.5;
            if (!custom && CtxSpeed.SelectedIndex != 2)
            {
                _loadingMatch = true;
                CtxSpeed.SelectedIndex = 2;
                _loadingMatch = false;
            }
            _main.SpeedMultiplierSetting = SpeedMults[Math.Clamp(CtxSpeed.SelectedIndex, 0, 4)];

            CtxHunt.IsEnabled = custom;
            if (custom && CtxHunt.SelectedIndex >= 0) _main.CustomDurationIndex = CtxHunt.SelectedIndex;

            _main.RecomputeHuntDuration();

            // For preset difficulties the Hunt tier is derived, so reflect it back into the combo.
            if (!custom)
            {
                _loadingMatch = true;
                CtxHunt.SelectedIndex = _main.ResolveHuntTier();
                _loadingMatch = false;
            }

            if (_tutorialMode && _tutorialStep == EvidenceTutorialStep.ChangeMap
                && sender == CtxMap && CtxMap.SelectedIndex != _tutorialMapBeforeChange)
            {
                BtnMatchSetup.IsChecked = false;
                AdvanceTutorial();
            }
            else if (!_tutorialMode)
            {
                SaveMatchSettings();
            }
        }

        /// <summary>Writes the match-setup keys back to settings.txt so edits round-trip.</summary>
        private void SaveMatchSettings()
        {
            try
            {
                string configPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhasOverlay", "settings.txt");
                if (!System.IO.File.Exists(configPath)) return;

                var updates = new Dictionary<string, string>
                {
                    // Required: without it a tracker-selected Weekly (Difficulty=5) mis-migrates to Custom.
                    ["SettingsVersion"] = "2",
                    ["Difficulty"] = _main.DifficultyIndex.ToString(),
                    ["MapSize"] = _main.MapSizeIndex.ToString(),
                    ["CustomDuration"] = _main.CustomDurationIndex.ToString(),
                    ["HuntDuration"] = _main.ResolveHuntTier().ToString(),
                    ["GhostSpeed"] = CtxSpeed.SelectedIndex.ToString(),
                    ["EvidenceLimit"] = _main.EvidenceLimit.ToString(),
                };

                var lines = new List<string>(System.IO.File.ReadAllLines(configPath));
                foreach (var kv in updates)
                {
                    bool found = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith(kv.Key + "="))
                        {
                            lines[i] = $"{kv.Key}={kv.Value}";
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        int insertIdx = lines.IndexOf("[Game Settings]") + 1;
                        if (insertIdx > 0) lines.Insert(insertIdx, $"{kv.Key}={kv.Value}");
                    }
                }
                System.IO.File.WriteAllLines(configPath, lines);
            }
            catch { }
        }

        /// <summary>Updates the "N possible" badge next to the Ghosts header.</summary>
        private void UpdateGhostCount(int count)
        {
            if (GhostCountText == null) return;

            if (count == 0)
            {
                GhostCountText.Text = "no matches";
                GhostCountText.Foreground = CountDangerBrush;
            }
            else if (count == 1)
            {
                GhostCountText.Text = "1 possible";
                GhostCountText.Foreground = CountAccentBrush;
            }
            else
            {
                GhostCountText.Text = $"{count} possible";
                GhostCountText.Foreground = CountMutedBrush;
            }
        }

        private void GhostCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_tutorialMode)
            {
                e.Handled = true;
                return;
            }

            if (sender is FrameworkElement element && element.DataContext is GhostData ghost)
            {
                int nextState = (ghost.CardState + 1) % 3;

                if (nextState == 1)
                {
                    foreach (var g in _masterGhostList)
                    {
                        if (g != ghost && g.CardState == 1)
                        {
                            g.CardState = 0;
                        }
                    }
                }

                ghost.CardState = nextState;

                // Ruling a ghost out (or back in) changes what the overlay should list.
                PushPossibleGhostsToOverlay();
            }
        }

        private void ExpandGhost_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is FrameworkElement element && element.DataContext is GhostData ghost)
            {
                if (_tutorialMode
                    && (_tutorialStep != EvidenceTutorialStep.ExpandGhost || ghost.Name != "Dayan"))
                {
                    return;
                }
                OpenModalForGhost(ghost);
                if (_tutorialMode) AdvanceTutorial();
            }
        }

        private void OpenModalForGhost(GhostData ghost)
        {
            ModalGhostName.Text = ghost.Name;
            ModalGhostSpeedControl.ItemsSource = ghost.Speeds;
            ModalGhostSanity.Text = ghost.Sanity;
            ModalGhostTell.Text = ghost.Tell;

            ModalGhostEvidence.Inlines.Clear();
            for (int i = 0; i < ghost.EvidencePool.Count; i++)
            {
                var ev = ghost.EvidencePool[i];

                string displayEv = ev;
                if (displayEv == "Freezing Temperatures") displayEv = "Freezing";
                else if (displayEv == "D.O.T.S Projector") displayEv = "D.O.T.S";

                var run = new Run(displayEv);

                if (ev == ghost.ForcedEvidence && ghost.ShowForcedUnderline)
                {
                    run.TextDecorations = TextDecorations.Underline;
                }

                ModalGhostEvidence.Inlines.Add(run);

                if (i < ghost.EvidencePool.Count - 1)
                {
                    ModalGhostEvidence.Inlines.Add(new Run(", "));
                }
            }

            var visibleGhosts = GhostItemsControl.ItemsSource as List<GhostData>;

            // Build the modal's mark/rule-out tells from the ghost's JSON data. A tell can
            // depend on whether another ghost is still a live possibility (e.g. Gallu's salt
            // tell reads differently once Wraith is ruled out) via ConditionGhost.
            var behaviors = new List<BehaviorItem>();
            foreach (var b in ghost.Behaviors)
            {
                string text = b.Text;
                if (!string.IsNullOrEmpty(b.ConditionGhost) && !string.IsNullOrEmpty(b.TextIfConditionValid)
                    && visibleGhosts != null && visibleGhosts.Exists(g => g.Name == b.ConditionGhost))
                {
                    text = b.TextIfConditionValid;
                }

                behaviors.Add(new BehaviorItem
                {
                    Prefix = b.Prefix,
                    Description = text,
                    ActionText = b.Type == "ruleout" ? "✕ Rule Out" : "✓ Mark Ghost",
                    TargetGhost = ghost.Name,
                    ActionType = b.Type == "ruleout" ? 2 : 1
                });
            }

            ModalBehaviorControl.ItemsSource = behaviors;

            if (behaviors.Count > 0)
            {
                BehaviorsTitle.Visibility = Visibility.Visible;
                BehaviorsScrollViewer.Visibility = Visibility.Visible;
                GraphDivider.Visibility = Visibility.Visible;

                BehaviorsScrollViewer.VerticalScrollBarVisibility =
                    ghost.HideBehaviorScroll ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            }
            else
            {
                BehaviorsTitle.Visibility = Visibility.Collapsed;
                BehaviorsScrollViewer.Visibility = Visibility.Collapsed;
                GraphDivider.Visibility = Visibility.Collapsed;
            }

            RightPanelBorder.Visibility = Visibility.Visible;

            DrawSpeedGraph(ghost.GraphBase, ghost.GraphMax, ghost.GraphTimeToMax);

            GhostModalOverlay.Visibility = Visibility.Visible;
        }



        public void ExpandGhostView(string ghostName)
        {
            var ghost = _masterGhostList.Find(g => g.Name == ghostName);
            if (ghost == null) return;

            OpenModalForGhost(ghost);
        }

        private void BehaviorAction_Click(object sender, RoutedEventArgs e)
        {
            if (_tutorialMode) return;

            if (sender is Button btn && btn.CommandParameter is BehaviorItem item)
            {
                var targetGhost = _masterGhostList.Find(g => g.Name == item.TargetGhost);
                if (targetGhost != null)
                {
                    if (item.ActionType == 1) // Mark
                    {
                        foreach (var g in _masterGhostList)
                        {
                            if (g != targetGhost && g.CardState == 1)
                            {
                                g.CardState = 0;
                            }
                        }
                        targetGhost.CardState = 1;
                        CloseGhostModal();
                    }
                    else if (item.ActionType == 2) // Rule out
                    {
                        targetGhost.CardState = 2;
                        CloseGhostModal();
                    }

                    PushPossibleGhostsToOverlay();
                }
            }
        }

        private async void CloseGhostModal()
        {
            if (GhostModalOverlay.Visibility != Visibility.Visible) return;

            StopGraphPlayback();

            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.QuarticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            var slideOut = new System.Windows.Media.Animation.DoubleAnimation(0, 40, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.QuarticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            var overlayFade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));

            GhostModalContent.BeginAnimation(OpacityProperty, fadeOut);

            var transform = new System.Windows.Media.TranslateTransform(0, 0);
            GhostModalContent.RenderTransform = transform;
            transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideOut);

            GhostModalOverlay.BeginAnimation(OpacityProperty, overlayFade);

            await Task.Delay(200);

            GhostModalOverlay.Visibility = Visibility.Collapsed;

            GhostModalContent.BeginAnimation(OpacityProperty, null);
            GhostModalOverlay.BeginAnimation(OpacityProperty, null);
            GhostModalContent.RenderTransform = new System.Windows.Media.TranslateTransform(0, 40);

            if (_tutorialMode && _tutorialStep == EvidenceTutorialStep.CloseGhost)
            {
                AdvanceTutorial();
            }
        }

        private void GhostModalOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseGhostModal();
        }

        private void ModalContent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void CloseModal_Click(object sender, RoutedEventArgs e)
        {
            CloseGhostModal();
        }

        private void PlaySpeed_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_tutorialMode) return;

            if ((DateTime.Now - _lastClickTime).TotalMilliseconds < 250) return;
            _lastClickTime = DateTime.Now;

            if (sender is Button btn && btn.CommandParameter is double baseSpeed)
            {
                double effectiveMultiplier = _main.SpeedMultiplierSetting + (_main.IsBloodMoonActive ? 0.15 : 0.0);
                double trueSpeed = baseSpeed * effectiveMultiplier;

                if (_currentPlayingSpeed == trueSpeed)
                {
                    StopFootstepPlayback();
                    return;
                }

                StopFootstepPlayback();

                _currentPlayingSpeed = trueSpeed;
                _playbackDurationTimer.Start();

                StartHighPrecisionMetronome(trueSpeed);
            }
        }

        private void EnsureFootstepAliases()
        {
            if (_aliasesLoaded) return;

            string filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "footstep.mp3");
            if (!System.IO.File.Exists(filePath))
            {
                filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "footstep.mp3");
            }

            for (int i = 0; i < AUDIO_POOL_SIZE; i++)
            {
                _mciAliases[i] = "fs_alias_" + i;
                mciSendString($"open \"{filePath}\" type mpegvideo alias {_mciAliases[i]}", null, 0, IntPtr.Zero);
            }
            _aliasesLoaded = true;
        }

        /// <summary>
        /// Footstep interval (ms) for a given speed, matched to the tybayn phasmo cheat sheet's
        /// metronome: it uses BPM = 60 / (1/speed - 0.075), i.e. interval = 1000/speed - 75 ms.
        /// The -75 ms offset is what our old plain 850/speed lacked. Without it the cadence was
        /// slightly fast below 2.0 m/s and increasingly slow above it (noticeable at 3.0).
        /// </summary>
        private static double FootstepIntervalMs(double speed)
        {
            if (speed < 0.1) speed = 0.1;
            double ms = (1000.0 / speed) - 75.0;
            return ms < 60.0 ? 60.0 : ms;   // floor guards against absurdly high speeds
        }

        private void StartHighPrecisionMetronome(double speed)
        {
            EnsureFootstepAliases();

            _footstepCancelToken = new CancellationTokenSource();
            var token = _footstepCancelToken.Token;

            double intervalMs = FootstepIntervalMs(speed);
            TimeSpan interval = TimeSpan.FromMilliseconds(intervalMs);

            FireSingleFootstep();

            Task.Run(async () =>
            {
                try
                {
                    // PeriodicTimer keeps a drift-free schedule; the actual play call must run on
                    // the UI thread that opened the MCI aliases. MCI devices are thread-affine, so
                    // playing from another thread is silently ignored.
                    using var timer = new PeriodicTimer(interval);
                    while (await timer.WaitForNextTickAsync(token))
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                FireSingleFootstep();
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        private void FireSingleFootstep()
        {
            string alias = _mciAliases[_currentAliasIndex];
            _currentAliasIndex = (_currentAliasIndex + 1) % AUDIO_POOL_SIZE;

            mciSendString($"play {alias} from 0", null, 0, IntPtr.Zero);
        }

        private void StopFootstepPlayback()
        {
            _playbackDurationTimer?.Stop();
            _currentPlayingSpeed = 0;

            if (_footstepCancelToken != null)
            {
                _footstepCancelToken.Cancel();
                _footstepCancelToken.Dispose();
                _footstepCancelToken = null;
            }
        }

        // Walks the endpoint dot along the curve while footsteps accelerate to match.

        private bool _rampPlaying = false;
        private CancellationTokenSource? _rampCancel;
        private System.Diagnostics.Stopwatch? _rampWatch;
        private EventHandler? _rampRenderHandler;

        private void PlayGraph_Click(object sender, RoutedEventArgs e)
        {
            if (_rampPlaying) { StopGraphPlayback(); return; }
            StartGraphPlayback();
        }

        private void StartGraphPlayback()
        {
            if (GraphCanvas.ActualWidth <= 0 || GraphCanvas.ActualHeight <= 0) return;

            StopFootstepPlayback();   // don't overlap with the constant-speed preview
            EnsureFootstepAliases();

            _rampPlaying = true;
            SetPlayIcon(true);

            _rampCancel = new CancellationTokenSource();
            _rampWatch = System.Diagnostics.Stopwatch.StartNew();

            _rampRenderHandler = (s, ev) => UpdateRampDot();
            CompositionTarget.Rendering += _rampRenderHandler;

            var token = _rampCancel.Token;
            var watch = _rampWatch;
            Task.Run(async () => await RampFootstepsAsync(watch, token), token);
        }

        private void UpdateRampDot()
        {
            if (!_rampPlaying || _rampWatch == null) return;

            double elapsed = _rampWatch.Elapsed.TotalSeconds;
            if (elapsed >= _graphMaxTime) { StopGraphPlayback(); return; }

            double width = GraphCanvas.ActualWidth;
            double height = GraphCanvas.ActualHeight;
            double speed = CalculateSpeedAtTime(elapsed);

            Canvas.SetLeft(EndPoint, (elapsed / _graphMaxTime) * width);
            Canvas.SetTop(EndPoint, height - (speed / _graphMaxSpeed) * height);
        }

        private async Task RampFootstepsAsync(System.Diagnostics.Stopwatch watch, CancellationToken token)
        {
            // Absolute schedule: accumulate each step's target time and sleep only the remainder,
            // so the ~10ms MCI cost and timer overshoot can't compound into a slow cadence (the
            // old "fire-then-sleep" pattern measured ~5% slow at 3.0 m/s; this measures ~0%). The
            // scheduling runs here on a background thread, but the play call is marshalled to the
            // UI thread that opened the MCI aliases (MCI is thread-affine).
            double nextAtMs = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    double elapsedSec = watch.Elapsed.TotalSeconds;
                    if (elapsedSec >= _graphMaxTime) break;

                    Dispatcher.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            FireSingleFootstep();
                        }
                    });

                    double speed = CalculateSpeedAtTime(elapsedSec);

                    nextAtMs += FootstepIntervalMs(speed);   // same cadence formula as the metronome
                    double remainMs = nextAtMs - watch.Elapsed.TotalMilliseconds;
                    if (remainMs > 1)
                    {
                        await Task.Delay((int)Math.Round(remainMs), token);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void StopGraphPlayback()
        {
            if (!_rampPlaying) return;
            _rampPlaying = false;

            if (_rampRenderHandler != null)
            {
                CompositionTarget.Rendering -= _rampRenderHandler;
                _rampRenderHandler = null;
            }
            if (_rampCancel != null)
            {
                _rampCancel.Cancel();
                _rampCancel.Dispose();
                _rampCancel = null;
            }
            _rampWatch = null;

            SetPlayIcon(false);
            RestoreEndPoint();
        }

        /// <summary>Returns the dot to the end of the curve after (or instead of) a ramp.</summary>
        private void RestoreEndPoint()
        {
            if (SpeedCurve.Points.Count == 0) return;
            Point last = SpeedCurve.Points[SpeedCurve.Points.Count - 1];
            Canvas.SetLeft(EndPoint, last.X);
            Canvas.SetTop(EndPoint, last.Y);
        }

        private void SetPlayIcon(bool playing)
        {
            if (PlayIcon == null) return;
            PlayIcon.Data = Geometry.Parse(playing ? "M6,6 H18 V18 H6 Z" : "M8,5 L19,12 L8,19 Z");
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void HuntPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton clicked && clicked.IsChecked == true)
            {
                if (clicked != TglHuntVeryEarly) TglHuntVeryEarly.IsChecked = false;
                if (clicked != TglHuntEarly) TglHuntEarly.IsChecked = false;
                if (clicked != TglHuntNormal) TglHuntNormal.IsChecked = false;
                if (clicked != TglHuntLate) TglHuntLate.IsChecked = false;
            }

            ApplyFilteringEngine();
        }

        private void SpeedPill_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilteringEngine();
        }

        public void ResetTracker()
        {
            ChkEmf.IsChecked = null; ChkDots.IsChecked = null; ChkUv.IsChecked = null;
            ChkFreezing.IsChecked = null; ChkOrb.IsChecked = null; ChkWriting.IsChecked = null;
            ChkBox.IsChecked = null;

            TglHuntVeryEarly.IsChecked = false; TglHuntEarly.IsChecked = false; TglHuntNormal.IsChecked = false; TglHuntLate.IsChecked = false;
            TglSpeedSlow.IsChecked = false; TglSpeedNormal.IsChecked = false; TglSpeedFast.IsChecked = false;

            foreach (var ghost in _masterGhostList) ghost.CardState = 0;
            ApplyFilteringEngine();
            StopFootstepPlayback();
            _main.ResetSpeedTap(false);
        }

        private void ResetTracker_Click(object sender, RoutedEventArgs e)
        {
            ResetTracker();
            if (_tutorialMode && _tutorialStep == EvidenceTutorialStep.ResetTracker)
            {
                CompleteTutorial();
            }
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_tutorialMode) return;

            StopGraphPlayback();
            StopFootstepPlayback();
            this.Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            if (_tutorialMode) return;

            StopGraphPlayback();
            StopFootstepPlayback();
            this.Hide();
        }

        // ==========================================
        //  LOS GRAPH LOGIC
        // ==========================================

        private double _graphMaxTime = 15.0; // Fixed X-axis
        private double _graphMaxSpeed = 4.0; // Fixed Y-axis
        private double _currentBaseSpeed = 1.7;
        private double _currentMaxSpeed = 2.805;
        private double _currentTimeToMax = 13.0;

        private void DrawSpeedGraph(double baseSpeed, double maxSpeed, double timeToMax)
        {
            _currentBaseSpeed = baseSpeed;
            _currentMaxSpeed = maxSpeed;
            _currentTimeToMax = timeToMax;

            double width = GraphCanvas.ActualWidth;
            double height = GraphCanvas.ActualHeight;

            if (width == 0 || height == 0) return;

            var curve = new PointCollection();
            for (double t = 0; t <= _graphMaxTime; t += 0.1)
            {
                double speed = CalculateSpeedAtTime(t);
                double xPixel = (t / _graphMaxTime) * width;
                double yPixel = height - ((speed / _graphMaxSpeed) * height);
                curve.Add(new Point(xPixel, yPixel));
            }
            SpeedCurve.Points = curve;

            var fill = new PointCollection(curve);
            fill.Add(new Point(width, height));
            fill.Add(new Point(0, height));
            SpeedFill.Points = fill;

            if (curve.Count > 0)
            {
                Point last = curve[curve.Count - 1];
                Canvas.SetLeft(EndPoint, last.X);
                Canvas.SetTop(EndPoint, last.Y);
            }
        }

        private double CalculateSpeedAtTime(double timeInSeconds)
        {
            if (timeInSeconds >= _currentTimeToMax) return _currentMaxSpeed;
            return _currentBaseSpeed + ((_currentMaxSpeed - _currentBaseSpeed) * (timeInSeconds / _currentTimeToMax));
        }

        private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawSpeedGraph(_currentBaseSpeed, _currentMaxSpeed, _currentTimeToMax);
        }

        private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(GraphCanvas);
            double width = GraphCanvas.ActualWidth;
            double height = GraphCanvas.ActualHeight;

            if (width == 0 || height == 0) return;

            double hoveredTime = (pos.X / width) * _graphMaxTime;

            if (hoveredTime < 0) hoveredTime = 0;
            if (hoveredTime > _graphMaxTime) hoveredTime = _graphMaxTime;

            double currentSpeed = CalculateSpeedAtTime(hoveredTime);
            double curveYPixel = height - ((currentSpeed / _graphMaxSpeed) * height);

            HoverLine.Visibility = Visibility.Visible;
            HoverLine.X1 = pos.X;
            HoverLine.X2 = pos.X;

            HoverPoint.Visibility = Visibility.Visible;
            Canvas.SetLeft(HoverPoint, pos.X);
            Canvas.SetTop(HoverPoint, curveYPixel);

            HoverInfoBox.Visibility = Visibility.Visible;
            HoverTimeText.Text = $"Time: {hoveredTime:F1}s";
            HoverSpeedText.Text = $"Speed: {currentSpeed:F2} m/s";

            double boxWidth = HoverInfoBox.ActualWidth > 0 ? HoverInfoBox.ActualWidth : 80;
            double leftPos = pos.X + 15;
            if (leftPos + boxWidth > width) leftPos = pos.X - boxWidth - 15;

            Canvas.SetLeft(HoverInfoBox, leftPos);

            double topPos = pos.Y - 15;
            if (topPos < 0) topPos = 0;

            Canvas.SetTop(HoverInfoBox, topPos);
        }

        private void GraphCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            HoverLine.Visibility = Visibility.Collapsed;
            HoverPoint.Visibility = Visibility.Collapsed;
            HoverInfoBox.Visibility = Visibility.Collapsed;
        }
    }
}
