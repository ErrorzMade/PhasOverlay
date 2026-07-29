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

        internal void InitializeTray(MainWindow mainWindow)
        {
            _trayIcon ??= new TrayIconService(mainWindow);
        }

        internal void ShowTrayIntroduction()
        {
            _trayIcon?.ShowIntroduction();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            base.OnExit(e);
        }
    }

}
