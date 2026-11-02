using System.Collections.Generic;
using System.Diagnostics;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Lightweight snapshot of grouped running processes used by the UI.
    /// Contains values only; no live Process handles are kept.
    /// </summary>
    public sealed class ProcessGroupSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public int FirstProcessId { get; set; }
        public int InstanceCount { get; set; }
        public long TotalMemoryBytes { get; set; }
        public double CpuUsagePercent { get; set; }
        public ProcessPriorityClass Priority { get; set; } = ProcessPriorityClass.Normal;
        public List<ProcessInstanceKey> Instances { get; set; } = new();

        /// <summary>
        /// EXE path when every observed instance reports the same one; null when
        /// instances disagree or the OS denies the read. Path-bound matching must
        /// re-read per instance anyway; this is display + pre-filter data only.
        /// </summary>
        public string? ExecutablePath { get; set; }
    }
}
