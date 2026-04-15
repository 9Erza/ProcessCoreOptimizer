using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Models;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for identifying, filtering, and managing 
    /// system process attributes such as CPU affinity, CPU Sets, and priority classes.
    /// </summary>
    public class ProcessService
    {
        #region Process Filtering

        /// <summary>
        /// Determines if a process is a high-level user application rather than a core system service.
        /// This helps declutter the UI from critical Windows OS components.
        /// </summary>
        /// <param name="p">The process to evaluate.</param>
        /// <returns>True if the process is likely a user application; otherwise, false.</returns>
        public bool IsUserProcess(Process p)
        {
            try
            {
                // Filter out critical kernel processes (Idle, System)
                if (p.Id <= 4) return false;

                // Hide background services running in Session 0 (non-interactive)
                if (p.SessionId == 0) return false;

                string name = p.ProcessName.ToLower();

                // Blacklist of common Windows system host processes and UI components
                // to prevent accidental optimization of OS-critical tasks.
                string[] systemBlacklist = {
                    "svchost", "taskhostw", "explorer", "sihost", "searchhost",
                    "startmenuexperiencehost", "runtimebroker", "applicationframehost",
                    "shellhost", "system", "idle", "conhost", "wmiprvse", "ctfmon",
                    "fontdrvhost", "dwm", "spoolsv", "lsass", "csrss", "smss", "winlogon",
                    "crossdeviceresume", "rtkauduservice64", "taskmgr", "ctfmon"
                };

                if (systemBlacklist.Contains(name)) return false;

                return true;
            }
            catch
            {
                // If we cannot access process info (Access Denied), treat it as a system process
                return false;
            }
        }

        #endregion

        #region Basic Process Management (Priority & Legacy Affinity)

        /// <summary>
        /// Sets the standard CPU affinity mask for a specific process.
        /// </summary>
        /// <param name="pid">The Process ID.</param>
        /// <param name="mask">The bitmask representing assigned logical cores.</param>
        /// <returns>True if the operation was successful.</returns>
        public bool SetProcessAffinity(int pid, long mask)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.ProcessorAffinity = (IntPtr)mask;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Adjusts the execution priority of a specific process.
        /// </summary>
        /// <param name="pid">The Process ID.</param>
        /// <param name="priority">The desired priority class (e.g., High, Idle).</param>
        /// <returns>True if the priority was successfully changed.</returns>
        public bool SetPriority(int pid, ProcessPriorityClass priority)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.PriorityClass = priority;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieves the current priority class of a process as a string.
        /// </summary>
        /// <param name="pid">The Process ID.</param>
        /// <returns>A string representation of the priority class (defaults to 'Normal' on failure).</returns>
        public string GetPriorityString(int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                return proc.PriorityClass.ToString();
            }
            catch
            {
                return "Normal";
            }
        }

        #endregion

        #region Advanced Core Optimization (Affinity, CPU Sets & Exclusive Mode)

        /// <summary>
        /// Applies the selected core optimization mode (Affinity, CPU Sets, or Exclusive) to the target process.
        /// </summary>
        public string ApplyCoreOptimization(int pid, long mask, OptimizationMode mode, Dictionary<int, uint> cpuSetMap)
        {
            try
            {
                switch (mode)
                {
                    case OptimizationMode.Affinity:
                        {
                            // Standard hard affinity method. May be blocked by modern Anti-Cheats, 
                            // but works flawlessly for standard applications and older games.
                            using var proc = Process.GetProcessById(pid);
                            proc.ProcessorAffinity = (IntPtr)mask;

                            // Clear any existing CPU Sets to prevent OS scheduling conflicts
                            IntPtr hProcClean = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_LIMITED_INFORMATION, false, pid);
                            if (hProcClean != IntPtr.Zero)
                            {
                                NativeMethods.SetProcessDefaultCpuSets(hProcClean, new uint[0], 0);
                                NativeMethods.CloseHandle(hProcClean);
                            }
                            return "OK_AFFINITY";
                        }

                    case OptimizationMode.CpuSets:
                        {
                            // Bypass Anti-Cheat restrictions by opening the process with limited rights 
                            // (PROCESS_SET_LIMITED_INFORMATION) instead of requesting full execution access.
                            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_LIMITED_INFORMATION, false, pid);
                            if (hProc == IntPtr.Zero)
                            {
                                return $"ERR_OPENPROCESS_{Marshal.GetLastWin32Error()}";
                            }

                            var selectedSetIds = GetCpuSetIdsFromMask(mask, cpuSetMap);
                            if (selectedSetIds.Length == 0)
                            {
                                NativeMethods.CloseHandle(hProc);
                                return "ERR_NO_CPUSETS_MAPPED";
                            }

                            // Apply the CPU Sets configuration via Windows Native API
                            bool success = NativeMethods.SetProcessDefaultCpuSets(hProc, selectedSetIds, (uint)selectedSetIds.Length);
                            int win32Error = Marshal.GetLastWin32Error();

                            // Always close the unmanaged handle to prevent memory leaks
                            NativeMethods.CloseHandle(hProc);

                            if (!success) return $"ERR_API_{win32Error}";
                            return "OK_CPUSETS";
                        }

                    case OptimizationMode.Exclusive:
                        {
                            using var proc = Process.GetProcessById(pid);
                            proc.ProcessorAffinity = (IntPtr)mask;

                            // Trigger the eviction protocol for all other processes
                            ApplyExclusiveIsolation(pid, mask);
                            return "OK_EXCLUSIVE";
                        }
                }
                return "ERR_UNKNOWN_MODE";
            }
            catch (Exception ex)
            {
                return $"ERR_EXC: {ex.Message}";
            }
        }

        /// <summary>
        /// Exclusive Mode Algorithm: Evicts other background applications and system processes 
        /// from the selected cores to create a 'Clean Core Environment' for the target game/app.
        /// </summary>
        private void ApplyExclusiveIsolation(int targetPid, long targetMask)
        {
            // Invert the target bitmask. Selected cores become 0 (off-limits), unselected become 1 (available).
            long allCoresMask = (1L << Environment.ProcessorCount) - 1;
            long inverseMask = (~targetMask) & allCoresMask;

            // Failsafe: If the user selected ALL cores for the game, we cannot evict the OS entirely.
            // Doing so would cause a system freeze or BSOD. We require leaving at least 1 core for the OS.
            if (inverseMask == 0) return;

            var allProcs = Process.GetProcesses();
            foreach (var p in allProcs)
            {
                // Ignore the target process and critical kernel-level processes (PID <= 4)
                if (p.Id == targetPid || p.Id <= 4) continue;

                try
                {
                    // Assign the inverted affinity mask to all other processes, effectively pushing them off
                    p.ProcessorAffinity = (IntPtr)inverseMask;
                }
                catch
                {
                    // The OS will naturally block access to critical protected services (e.g., lsass.exe, Anti-Viruses).
                    // This is expected behavior; we simply ignore the Access Denied exceptions and proceed to evict what we can.
                }
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Converts a traditional hexadecimal bitmask into an array of native CPU Set IDs.
        /// </summary>
        private uint[] GetCpuSetIdsFromMask(long mask, Dictionary<int, uint> cpuSetMap)
        {
            var ids = new List<uint>();
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                // Check if the bit for the specific logical core index is set to 1
                if ((mask & (1L << i)) != 0)
                {
                    if (cpuSetMap.TryGetValue(i, out uint cpuSetId))
                    {
                        ids.Add(cpuSetId);
                    }
                }
            }
            return ids.ToArray();
        }

        #endregion
    }
}