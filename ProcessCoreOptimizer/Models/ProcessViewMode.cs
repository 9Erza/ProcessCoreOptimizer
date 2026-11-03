namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Process list presentation: grouped rows (one per process name) or flat
    /// per-instance rows (one per PID). Switching is a local view op, no rescan.
    /// </summary>
    public enum ProcessViewMode
    {
        Grouped,
        Instances
    }
}
