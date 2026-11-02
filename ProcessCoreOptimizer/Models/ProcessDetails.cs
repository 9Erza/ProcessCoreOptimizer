using ProcessCoreOptimizer.WPF.Models;
using System;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Read-only details for the process panel. Best-effort reads; any field the
    /// OS denies stays null/"unreadable" instead of failing the whole panel.
    /// </summary>
    public sealed class ProcessDetails
    {
        public string Name { get; set; } = string.Empty;
        public int RepresentativePid { get; set; }
        public int InstanceCount { get; set; }
        public DateTime? StartTimeUtc { get; set; }
        public string? ExecutablePath { get; set; }
        public string Priority { get; set; } = "Unknown";
        public long? AffinityMask { get; set; }
        public string AffinitySource { get; set; } = "unreadable";
        public double CpuUsagePercent { get; set; }
        public long TotalMemoryBytes { get; set; }
        public string? AssignedProfileName { get; set; }
        public OperationStatus? LastStatus { get; set; }
        public string? LastMessage { get; set; }

        public string StartTimeLocalText => StartTimeUtc.HasValue
            ? StartTimeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "unreadable";
    }
}
