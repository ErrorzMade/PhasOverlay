using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PhasOverlay
{
    internal sealed class TrayIconService : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly Icon _trayImage;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _overlayItem;
        private readonly NotifyIcon _notifyIcon;
        private bool _disposed;

        internal TrayIconService(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _trayImage = LoadIcon();
            _mainWindow.IsVisibleChanged += MainWindow_IsVisibleChanged;

            _settingsItem = new ToolStripMenuItem("Open Settings");
            _settingsItem.Click += (_, _) => Dispatch(_mainWindow.OpenSettingsFromTray);

            _overlayItem = new ToolStripMenuItem();
            _overlayItem.Click += (_, _) => Dispatch(_mainWindow.ToggleOverlayFromTray);
            RefreshMenuState();

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit PhasOverlay");
            exitItem.Click += (_, _) => Dispatch(_mainWindow.ExitFromTray);

            _menu = new ContextMenuStrip();
            _menu.Items.Add(_settingsItem);
            _menu.Items.Add(_overlayItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);
            _menu.Opening += (_, _) => RefreshMenuState();

            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = _menu,
                Icon = _trayImage,
                Text = "PhasOverlay",
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => Dispatch(_mainWindow.OpenSettingsFromTray);
        }

        internal void ShowIntroduction()
        {
            _notifyIcon.BalloonTipTitle = "PhasOverlay is ready";
            _notifyIcon.BalloonTipText = "PhasOverlay stays in your system tray. Open the tray and right-click the PhasOverlay icon to reopen Settings.";
            _notifyIcon.ShowBalloonTip(5000);
        }

        private void RefreshMenuState()
        {
            bool tutorialActive = _mainWindow.IsTutorialActive;
            _settingsItem.Enabled = !tutorialActive;
            _overlayItem.Enabled = !tutorialActive;
            _overlayItem.Text = _mainWindow.IsVisible ? "Hide Overlay" : "Show Overlay";
        }

        private void MainWindow_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            RefreshMenuState();
        }

        private void Dispatch(Action action)
        {
            if (_disposed) return;
            _mainWindow.Dispatcher.BeginInvoke(action);
        }

        private static Icon LoadIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "phasicon.ico");
            if (File.Exists(iconPath)) return new Icon(iconPath);

            Icon? executableIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "");
            return executableIcon ?? (Icon)SystemIcons.Application.Clone();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _mainWindow.IsVisibleChanged -= MainWindow_IsVisibleChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _trayImage.Dispose();
        }
    }
}
