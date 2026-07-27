using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace PhasOverlay
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _info;

        public UpdateWindow(UpdateInfo info, int monitorIndex)
        {
            InitializeComponent();
            _info = info;

            this.Loaded += (s, e) => DisplayService.CenterOn(this, monitorIndex);

            var current = UpdateService.CurrentVersion;
            VersionLine.Text = $"PhasOverlay {info.VersionLabel} is available. "
                             + $"You're on v{current.Major}.{current.Minor}.{current.Build}.";

            if (!string.IsNullOrWhiteSpace(info.Notes))
            {
                NotesText.Text = info.Notes;
                NotesCard.Visibility = Visibility.Visible;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(_info.Url) { UseShellExecute = true }); }
            catch { }
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            UpdateService.SkipVersion(_info.Version);
            Close();
        }

        private void Later_Click(object sender, RoutedEventArgs e) => Close();
    }
}
