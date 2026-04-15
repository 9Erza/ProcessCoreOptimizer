using System;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for identifying, filtering, and managing 
    /// system process attributes such as CPU affinity and priority classes.
    /// </summary>
    public class ProcessService
    {
        #region Process Filtering
        /// <summary>
        /// Determines if a process is a high-level user application rather than a core system service.
        /// This helps declutter the UI from critical Windows components.
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
                // If we can't access process info (Access Denied), treat it as a system process
                return false;
            }
        }
        #endregion

        #region Affinity and Priority Management
        /// <summary>
        /// Sets the CPU affinity mask for a specific process.
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
    }
}