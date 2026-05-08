using System.Collections.Generic;

namespace ProcessCoreOptimizer.WPF.Models
{
    public sealed class ProcessScanResult
    {
        public List<ProcessGroupSnapshot> Groups { get; set; } = new();
        public HashSet<ProcessInstanceKey> ActiveInstances { get; set; } = new();
    }
}
