using System;
using System.Runtime.InteropServices;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Best-effort foreground window PID. One cheap Win32 call per evaluation at
    /// most (caller decides frequency); never throws, null when unavailable.
    /// </summary>
    public static class ForegroundService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static int? GetForegroundPid()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                return pid == 0 ? (int?)null : (int)pid;
            }
            catch
            {
                return null;
            }
        }
    }
}
