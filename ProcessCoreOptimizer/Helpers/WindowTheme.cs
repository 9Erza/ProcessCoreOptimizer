using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ProcessCoreOptimizer.WPF.Helpers
{
    /// <summary>
    /// Native window theming through the documented DWM attributes:
    /// dark title bar / frame (Windows 10 2004+), rounded corners and a border
    /// color that matches the app palette (Windows 11). Every call is best-effort:
    /// older systems simply ignore unknown attributes and nothing throws.
    /// </summary>
    public static class WindowTheme
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// Applies the dark frame once the HWND exists. Safe to call from a window
        /// constructor (hooks SourceInitialized) or after the handle is created.
        /// </summary>
        public static void Attach(Window window)
        {
            if (window == null) return;
            var helper = new WindowInteropHelper(window);
            if (helper.Handle != IntPtr.Zero)
            {
                Apply(helper.Handle);
                return;
            }

            window.SourceInitialized += (s, e) => Apply(new WindowInteropHelper(window).Handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        /// <summary>
        /// How far (in DIPs) a maximized chrome-less window extends past the monitor
        /// work area on each side. Used as content inset so nothing is clipped and the
        /// taskbar stays uncovered. Falls back to the system resize frame size.
        /// </summary>
        public static Thickness GetMaximizedOverflow(Window window)
        {
            var fallback = SystemParameters.WindowResizeBorderThickness;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT wr)) return fallback;
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return fallback;

                double scaleX = 1, scaleY = 1;
                var source = PresentationSource.FromVisual(window);
                if (source?.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                double left = Math.Max(0, info.rcWork.Left - wr.Left) / scaleX;
                double top = Math.Max(0, info.rcWork.Top - wr.Top) / scaleY;
                double right = Math.Max(0, wr.Right - info.rcWork.Right) / scaleX;
                double bottom = Math.Max(0, wr.Bottom - info.rcWork.Bottom) / scaleY;
                return new Thickness(left, top, right, bottom);
            }
            catch
            {
                return fallback;
            }
        }

        private static void Apply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
                }

                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

                // COLORREF is 0x00BBGGRR. Border = PcoBorder (#272A31), caption = PcoBg0 (#0F1013).
                int border = 0x00312A27;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
                int caption = 0x0013100F;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            }
            catch
            {
                // dwmapi unavailable or attribute unsupported: keep the default frame.
            }
        }
    }
}
