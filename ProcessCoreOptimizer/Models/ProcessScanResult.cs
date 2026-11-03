using System.Collections.Generic;

namespace ProcessCoreOptimizer.WPF.Models
{
    public sealed class ProcessScanResult
    {
        public List<ProcessGroupSnapshot> Groups { get; set; } = new();
        public HashSet<ProcessInstanceKey> ActiveInstances { get; set; } = new();

        /// <summary>
        /// Per-instance detail rows, populated only when the caller passes
        /// includeInstanceDetails (UI full scan). Watcher scans leave it empty.
        /// Reuses the same handles + CPU cache as the group pass: zero extra scans.
        /// </summary>
        public List<Services.ProcessInstanceData> InstanceDetails { get; set; } = new();
    }
}
