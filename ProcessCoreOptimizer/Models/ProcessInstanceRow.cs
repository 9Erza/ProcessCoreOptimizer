using ProcessCoreOptimizer.WPF.Helpers;
using System;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// A single running process instance (one PID) for Instance view and drill-down.
    /// Display strings stay human-readable; raw numeric twins exist for correct
    /// numeric sorting (never sort the "12.3%" strings lexicographically).
    /// Unreadable values are null and render as Unknown/Unavailable, never invented.
    /// </summary>
    public class ProcessInstanceRow : ViewModelBase
    {
        private string _cpuUsage = "…";
        private string _ramUsageMB = "…";
        private string _priority = "Unknown";
        private string _affinitySummary = "Unknown";
        private string _profileName = string.Empty;
        private string _lastStatus = string.Empty;

        public ProcessInstanceKey Key { get; set; }

        public int ProcessId => Key.ProcessId;

        public DateTime StartTimeUtc => Key.StartTimeUtc;

        public string Name { get; set; } = string.Empty;

        public string GroupName { get; set; } = string.Empty;

        public string? ExecutablePath { get; set; }

        public double? CpuUsagePercent { get; set; }

        public string CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        public long? MemoryBytes { get; set; }

        public string RamUsageMB
        {
            get => _ramUsageMB;
            set => SetProperty(ref _ramUsageMB, value);
        }

        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        /// <summary>Semantic priority rank for column sorting (see <see cref="Services.PriorityService.Rank"/>).</summary>
        public int PriorityRank { get; set; } = 2;

        public string AffinitySummary
        {
            get => _affinitySummary;
            set => SetProperty(ref _affinitySummary, value);
        }

        public string ProfileName
        {
            get => _profileName;
            set => SetProperty(ref _profileName, value);
        }

        public string LastStatus
        {
            get => _lastStatus;
            set => SetProperty(ref _lastStatus, value);
        }

        public TimeSpan? UptimeUtcNow
        {
            get
            {
                if (StartTimeUtc == default) return null;
                try { return DateTime.UtcNow - StartTimeUtc; }
                catch { return null; }
            }
        }
    }
}
