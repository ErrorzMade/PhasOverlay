using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PhasOverlay
{
    /// <summary>
    /// Small, click-through, bottom-centre toast window shown by the overlay.
    /// </summary>
    public partial class NotificationWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IEasingFunction EaseOut = FreezeEase();
        private static IEasingFunction FreezeEase()
        {
            QuarticEase e = new QuarticEase { EasingMode = EasingMode.EaseOut };
            e.Freeze();
            return e;
        }

        private readonly DispatcherTimer _hideTimer;

        public NotificationWindow()
        {
            InitializeComponent();

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _hideTimer.Tick += (s, e) => { _hideTimer.Stop(); HideNotification(); };

            this.SizeChanged += (s, e) => Reposition();
            this.Loaded += (s, e) => Reposition();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Make the window fully click-through and keep it out of alt-tab / focus.
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private void Reposition()
        {
            var wa = SystemParameters.WorkArea;
            this.Left = wa.Left + (wa.Width - this.ActualWidth) / 2;
            this.Top = wa.Bottom - this.ActualHeight - 8;
        }

        public void ShowMessage(string message)
        {
            NotificationText.Text = message;

            if (NotificationPopup.Visibility == Visibility.Collapsed)
                NotificationPopup.Visibility = Visibility.Visible;

            // Ensure the new text is measured so we centre against the final width.
            this.UpdateLayout();
            Reposition();

            _hideTimer.Stop();
            _hideTimer.Start();

            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(100));
            DoubleAnimation slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = EaseOut };

            NotificationPopup.BeginAnimation(OpacityProperty, fadeIn);
            NotifTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }

        private void HideNotification()
        {
            DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) =>
            {
                if (!_hideTimer.IsEnabled)
                {
                    NotificationPopup.Visibility = Visibility.Collapsed;
                    NotificationPopup.BeginAnimation(OpacityProperty, null);
                }
            };
            NotificationPopup.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
