using System;
using System.Runtime.InteropServices;

namespace ProcessCoreOptimizer.WPF.Helpers
{
    /// <summary>
    /// Provides low-level Windows API (Kernel32) functions for process and CPU Set management.
    /// </summary>
    public static class NativeMethods
    {
        #region Constants

        /// <summary>
        /// Required access right to set process information without requiring full process access.
        /// Useful for interacting with protected processes or avoiding anti-cheat triggers.
        /// </summary>
        public const uint PROCESS_SET_LIMITED_INFORMATION = 0x2000;

        #endregion

        #region Process Management API

        /// <summary>
        /// Opens an existing local process object.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        /// <summary>
        /// Closes an open object handle.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        #endregion

        #region CPU Sets API

        /// <summary>
        /// Retrieves the CPU Set information for the system or a specific process.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemCpuSetInformation(
            IntPtr information,
            uint bufferLength,
            out uint returnedLength,
            IntPtr hProcess,
            uint flags);

        /// <summary>
        /// Sets the default CPU Sets for the specified process.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessDefaultCpuSets(
            IntPtr hProcess,
            uint[] cpuSetIds,
            uint cpuSetIdCount);

        #endregion
    }
}