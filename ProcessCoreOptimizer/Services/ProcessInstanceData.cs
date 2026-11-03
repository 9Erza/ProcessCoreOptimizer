using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Diagnostics;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// One PID of per-instance detail produced by on-demand drill-down.
    /// Nulls mean the OS refused that read (Unknown), never an invented value.
    /// </summary>
    public sealed class ProcessInstanceData
    {
        public ProcessInstanceKey Key { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public double? CpuUsagePercent { get; set; }
        public long? WorkingSetBytes { get; set; }
        public ProcessPriorityClass? Priority { get; set; }
    }
}
