using System.Configuration;
using System.Data;
using System.Windows;

namespace PhasOverlay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private TrayIconService? _trayIcon;
        private WeeklyRefreshCoordinator? _weeklyRefresh;

        internal const string LinkServiceUrl = "https://phasoverlay-link.phasoverlay-link-worker.workers.dev";

        /// <summary>Owned here, not by a window, since the room outlives every window's visibility.</summary>
        public Link.LinkCoordinator? Link { get; private set; }

        internal void InitializeTray(MainWindow mainWindow)
        {
            _trayIcon ??= new TrayIconService(mainWindow);
        }

        internal void InitializeLink(MainWindow mainWindow)
        {
            Link ??= new Link.LinkCoordinator(
                new System.Uri(LinkServiceUrl),
                new Link.LinkStorage(),
                action => mainWindow.Dispatcher.Invoke(action));
        }

        internal void InitializeWeeklyRefresh(MainWindow mainWindow)
        {
            if (_weeklyRefresh != null) return;

            _weeklyRefresh = new WeeklyRefreshCoordinator();
            _weeklyRefresh.StateChanged += result =>
            {
                if (mainWindow.Dispatcher.HasShutdownStarted) return;
                mainWindow.Dispatcher.BeginInvoke(() => mainWindow.ApplyWeeklyRefreshResult(result));
            };
            _weeklyRefresh.Start();
        }

        internal void ShowTrayIntroduction()
        {
            _trayIcon?.ShowIntroduction();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            Link?.Dispose();
            Link = null;
            _weeklyRefresh?.Dispose();
            _weeklyRefresh = null;
            base.OnExit(e);
        }
    }

}
