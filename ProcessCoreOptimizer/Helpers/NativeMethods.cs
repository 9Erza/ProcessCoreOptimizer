using System;
using System.Runtime.InteropServices;

namespace ProcessCoreOptimizer.WPF.Helpers
{
    /// <summary>
    /// Low-level Windows API declarations used by the app.
    /// Keep this file focused on documented Kernel32 APIs only.
    /// </summary>
    public static class NativeMethods
    {
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_SET_INFORMATION = 0x0200;
        public const uint PROCESS_SET_LIMITED_INFORMATION = 0x2000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetPriorityClass(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessTimes(
            IntPtr hProcess,
            out System.Runtime.InteropServices.ComTypes.FILETIME creationTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME exitTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemCpuSetInformation(
            IntPtr information,
            uint bufferLength,
            out uint returnedLength,
            IntPtr hProcess,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessDefaultCpuSets(
            IntPtr hProcess,
            uint[]? cpuSetIds,
            uint cpuSetIdCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessDefaultCpuSets(
            IntPtr hProcess,
            [Out] uint[]? cpuSetIds,
            uint cpuSetIdCount,
            out uint requiredIdCount);

        // EcoQoS / Efficiency mode (PROCESS_INFORMATION_CLASS.ProcessPowerThrottling = 4).
        public const int ProcessPowerThrottling = 4;
        public const uint POWER_THROTTLING_CURRENT_VERSION = 1;
        public const uint POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation,
            uint processInformationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation,
            uint processInformationSize);
    }
}
