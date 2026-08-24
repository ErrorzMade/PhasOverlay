using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PhasOverlay.Link;

namespace PhasOverlay
{
    public sealed class LinkRosterRow
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public Visibility HostBadge { get; init; } = Visibility.Collapsed;
        public Visibility ActionVisible { get; init; } = Visibility.Collapsed;
        public string ActionLabel { get; init; } = "";
        public bool IsSelf { get; init; }
    }

    public partial class EvidenceWindow
    {
        private void Link_Click(object sender, RoutedEventArgs e)
        {
            if (_tutorialMode) return;

            var link = ((App)Application.Current)?.Link;
            LinkUsernameBox.Text = link?.LoadStoredProfile()?.Username ?? "";
            CloseConfirm(false);
            LinkRenameRow.Visibility = Visibility.Collapsed;
            RefreshLinkUi();
            LinkOverlay.Visibility = Visibility.Visible;
        }

        private void LinkClose_Click(object sender, RoutedEventArgs e)
        {
            CloseConfirm(false);
            LinkOverlay.Visibility = Visibility.Collapsed;
        }

        private void LinkOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            LinkOverlay.Visibility = Visibility.Collapsed;

        private async void LinkCreate_Click(object sender, RoutedEventArgs e)
        {
            var link = ((App)Application.Current)?.Link;
            if (link == null || !RequireUsername(link)) return;

            await link.CreateRoomAsync(BuildSharedState(), _main.LinkContentHash());
            RefreshLinkUi();
        }

        private async void LinkJoin_Click(object sender, RoutedEventArgs e)
        {
            var link = ((App)Application.Current)?.Link;
            if (link == null || !RequireUsername(link)) return;

            string? code = LinkProtocol.RoomCodeFromInvite(LinkCodeBox.Text);
            if (code == null)
            {
                SetLinkStatus("Enter a valid 6-character room code or invite link.", true);
                return;
            }

            if (BoardIsNotEmpty() && !await ConfirmAsync("Join room",
                    "Joining replaces your current evidence and Game Settings with the room's.",
                    "JOIN", false)) return;

            await link.JoinAsync(code, _main.LinkContentHash());
            RefreshLinkUi();
        }

        private async void LinkLeave_Click(object sender, RoutedEventArgs e)
        {
            var link = ((App)Application.Current)?.Link;
            if (link == null) return;

            string question = link.IsHost
                ? "Are you sure you want to leave the room? It ends for everyone else too."
                : "Are you sure you want to leave the room?";
            if (!await ConfirmAsync("Leave room", question, "LEAVE", true)) return;

            await link.LeaveAsync();
            RefreshLinkUi();
        }

        private TaskCompletionSource<bool>? _confirm;

        /// <summary>In-window confirmation so Link never falls back to a system message box.</summary>
        private Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool danger)
        {
            _confirm?.TrySetResult(false);

            LinkConfirmTitle.Text = title;
            LinkConfirmText.Text = message;
            BtnLinkConfirmOk.Content = confirmLabel;
            BtnLinkConfirmOk.Style = (Style)FindResource(danger ? "DangerButton" : "ModernButton");
            LinkConfirmOverlay.Visibility = Visibility.Visible;

            _confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _confirm.Task;
        }

        private void LinkConfirmOk_Click(object sender, RoutedEventArgs e) => CloseConfirm(true);

        private void LinkConfirmCancel_Click(object sender, RoutedEventArgs e) => CloseConfirm(false);

        private void CloseConfirm(bool accepted)
        {
            LinkConfirmOverlay.Visibility = Visibility.Collapsed;
            var pending = _confirm;
            _confirm = null;
            pending?.TrySetResult(accepted);
        }

        private System.Windows.Threading.DispatcherTimer? _copyTick;

        private void LinkCopyCode_Click(object sender, RoutedEventArgs e)
        {
            var link = ((App)Application.Current)?.Link;
            if (link == null || !LinkProtocol.ValidRoomCode(link.RoomCode)) return;

            try { Clipboard.SetText(link.RoomCode); }
            catch { return; }

            ShowCopyTick();
        }

        /// <summary>The tick is the whole confirmation, so the status line keeps reporting the room.</summary>
        private void ShowCopyTick()
        {
            var clip = (Path?)BtnLinkCopyCode.Template?.FindName("ClipIcon", BtnLinkCopyCode);
            var tick = (Path?)BtnLinkCopyCode.Template?.FindName("TickIcon", BtnLinkCopyCode);
            if (clip == null || tick == null) return;

            clip.Visibility = Visibility.Collapsed;
            tick.Visibility = Visibility.Visible;

            _copyTick ??= new System.Windows.Threading.DispatcherTimer();
            _copyTick.Stop();
            _copyTick.Interval = TimeSpan.FromSeconds(1.2);
            _copyTick.Tick -= CopyTickElapsed;
            _copyTick.Tick += CopyTickElapsed;
            _copyTick.Start();
        }

        private void CopyTickElapsed(object? sender, EventArgs e)
        {
            _copyTick?.Stop();
            var clip = (Path?)BtnLinkCopyCode.Template?.FindName("ClipIcon", BtnLinkCopyCode);
            var tick = (Path?)BtnLinkCopyCode.Template?.FindName("TickIcon", BtnLinkCopyCode);
            if (clip == null || tick == null) return;
            tick.Visibility = Visibility.Collapsed;
            clip.Visibility = Visibility.Visible;
        }

        private void LinkRenameSave_Click(object sender, RoutedEventArgs e)
        {
            var link = ((App)Application.Current)?.Link;
            if (link == null) return;

            if (!link.SetUsername(LinkRenameBox.Text))
            {
                SetLinkStatus("Choose a name of 2 to 20 characters.", true);
                return;
            }
            LinkRenameRow.Visibility = Visibility.Collapsed;
            RefreshLinkUi();
        }

        private async void LinkRowAction_Click(object sender, RoutedEventArgs e)
        {
            var link = ((App)Application.Current)?.Link;
            if (link == null || sender is not System.Windows.Controls.Button button || button.Tag is not string id) return;

            if (id == link.ParticipantId)
            {
                LinkRenameBox.Text = link.Username;
                LinkRenameRow.Visibility = Visibility.Visible;
                LinkRenameBox.Focus();
                LinkRenameBox.SelectAll();
                return;
            }

            var target = link.Participants.FirstOrDefault(p => p.Id == id);
            if (target == null) return;
            if (!await ConfirmAsync("Transfer host",
                    $"Make {target.Username} the host? You will no longer control Game Settings.",
                    "MAKE HOST", false)) return;

            link.TryTransferHost(id);
        }

        private bool RequireUsername(LinkCoordinator link)
        {
            if (link.SetUsername(LinkUsernameBox.Text)) return true;
            SetLinkStatus("Choose a name of 2 to 20 characters.", true);
            return false;
        }

        private bool BoardIsNotEmpty()
        {
            var board = BuildSharedState();
            return board.Evidence.Values.Any(value => value != 0)
                || board.Hunt.Values.Any(value => value)
                || board.Speed.Values.Any(value => value)
                || board.Cards.Count > 0;
        }

        private void SetLinkStatus(string text, bool problem)
        {
            LinkStatusText.Text = text;
            LinkStatusText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            LinkStatusText.Foreground = problem
                ? (Brush)FindResource("DangerBrush")
                : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        }

        /// <summary>Repaints the chip, the panel and the roster from the coordinator.</summary>
        public void RefreshLinkUi()
        {
            var link = ((App)Application.Current)?.Link;
            if (link == null || LinkOverlay == null) return;

            bool inRoom = link.IsLinked;
            LinkIdlePanel.Visibility = inRoom ? Visibility.Collapsed : Visibility.Visible;
            LinkRoomPanel.Visibility = inRoom ? Visibility.Visible : Visibility.Collapsed;
            LinkRoomCodeText.Text = link.RoomCode.Length > 0 ? link.RoomCode : "------";

            // A connected room says everything through the code and the roster, so the line is
            // kept for the states that are not otherwise visible.
            string statusText = link.Status switch
            {
                LinkStatus.Creating => "Creating room.",
                LinkStatus.Connecting => "Connecting.",
                LinkStatus.Reconnecting => "Connection lost. Retrying automatically.",
                LinkStatus.Connected => "",
                _ => "Not connected."
            };
            string shown = _linkNotice.Length > 0 ? _linkNotice : statusText;
            LinkStatusText.Visibility = shown.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (shown.Length > 0)
                SetLinkStatus(shown, link.Status == LinkStatus.Error
                    || (link.Status == LinkStatus.Connected && _linkNotice.Length > 0));

            string ownId = link.ParticipantId;
            LinkRoster.ItemsSource = link.Participants.Select(participant =>
            {
                bool self = participant.Id == ownId;
                bool promotable = link.IsHost && !participant.IsHost && !self;
                return new LinkRosterRow
                {
                    Id = participant.Id,
                    Name = self ? participant.Username + " (you)" : participant.Username,
                    HostBadge = participant.IsHost ? Visibility.Visible : Visibility.Collapsed,
                    IsSelf = self,
                    ActionLabel = self ? "CHANGE NAME" : "MAKE HOST",
                    ActionVisible = self || promotable ? Visibility.Visible : Visibility.Collapsed
                };
            }).ToList();

            var dot = (Ellipse?)BtnLink.Template?.FindName("LinkDot", BtnLink);
            var label = (System.Windows.Controls.TextBlock?)BtnLink.Template?.FindName("LinkChipLabel", BtnLink);
            if (dot != null)
            {
                dot.Fill = link.Status switch
                {
                    LinkStatus.Connected => (Brush)FindResource("AccentBrush"),
                    LinkStatus.Connecting or LinkStatus.Creating or LinkStatus.Reconnecting =>
                        new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
                    LinkStatus.Error => (Brush)FindResource("DangerBrush"),
                    _ => new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
                };
            }
            if (label != null)
                label.Text = link.Status == LinkStatus.Connected ? link.RoomCode : "LINK";
        }

        private string _linkNotice = "";

        internal void OnLinkStateChanged(LinkStateChange change)
        {
            _linkNotice = change.Notice;
            ApplyLinkLocks();
            RefreshLinkUi();
        }
    }
}
