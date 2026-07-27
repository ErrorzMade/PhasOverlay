using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace PhasOverlay
{
    public class DisplayInfo
    {
        public int Index;
        public bool IsPrimary;
        public Rect WorkArea;      // DIPs, ready to assign to Window.Left/Top/Width/Height
        public int PixelWidth;
        public int PixelHeight;

        public string Label => $"Display {Index + 1} ({PixelWidth}x{PixelHeight})" + (IsPrimary ? " • Primary" : "");
    }

    /// <summary>
    /// Display enumeration for the overlay picker. SystemParameters.WorkArea only ever
    /// describes the primary monitor, so the overlay can't be placed elsewhere without this.
    /// </summary>
    public static class DisplayService
    {
        private const int MonitorInfoPrimary = 0x00000001;
        private const int MdtEffectiveDpi = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RectL { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public RectL rcMonitor;
            public RectL rcWork;
            public int dwFlags;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RectL rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MonitorInfo info);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>All monitors in enumeration order. Never empty: falls back to the primary work area.</summary>
        public static List<DisplayInfo> GetDisplays()
        {
            var found = new List<DisplayInfo>();

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RectL r, IntPtr d) =>
                {
                    var mi = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
                    if (!GetMonitorInfoW(hMon, ref mi)) return true;

                    // Per-monitor DPI: physical pixels have to become DIPs or the overlay lands in
                    // the wrong place whenever two monitors run different scaling.
                    double scale = 1.0;
                    try
                    {
                        if (GetDpiForMonitor(hMon, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0)
                            scale = dpiX / 96.0;
                    }
                    catch { }

                    found.Add(new DisplayInfo
                    {
                        Index = found.Count,
                        IsPrimary = (mi.dwFlags & MonitorInfoPrimary) != 0,
                        WorkArea = new Rect(
                            mi.rcWork.Left / scale,
                            mi.rcWork.Top / scale,
                            Math.Max(1, (mi.rcWork.Right - mi.rcWork.Left) / scale),
                            Math.Max(1, (mi.rcWork.Bottom - mi.rcWork.Top) / scale)),
                        PixelWidth = mi.rcMonitor.Right - mi.rcMonitor.Left,
                        PixelHeight = mi.rcMonitor.Bottom - mi.rcMonitor.Top
                    });
                    return true;
                }, IntPtr.Zero);
            }
            catch { }

            if (found.Count == 0)
            {
                var wa = SystemParameters.WorkArea;
                found.Add(new DisplayInfo
                {
                    Index = 0,
                    IsPrimary = true,
                    WorkArea = wa,
                    PixelWidth = (int)SystemParameters.PrimaryScreenWidth,
                    PixelHeight = (int)SystemParameters.PrimaryScreenHeight
                });
            }

            return found;
        }

        public static int PrimaryIndex()
        {
            var all = GetDisplays();
            for (int i = 0; i < all.Count; i++) if (all[i].IsPrimary) return i;
            return 0;
        }

        /// <summary>
        /// Work area of the chosen monitor, falling back to the primary one when the index no
        /// longer exists (monitor unplugged, or the layout changed since the setting was saved).
        /// </summary>
        public static Rect WorkAreaFor(int index)
        {
            var all = GetDisplays();
            if (index >= 0 && index < all.Count) return all[index].WorkArea;

            foreach (var d in all) if (d.IsPrimary) return d.WorkArea;
            return all[0].WorkArea;
        }

        /// <summary>Centres a window on the chosen monitor. Call once the window has a size.</summary>
        public static void CenterOn(Window window, int index)
        {
            try
            {
                Rect wa = WorkAreaFor(index);
                double w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
                double h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
                if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;

                window.Left = wa.Left + (wa.Width - w) / 2;
                window.Top = wa.Top + (wa.Height - h) / 2;
            }
            catch { }
        }
    }
}
