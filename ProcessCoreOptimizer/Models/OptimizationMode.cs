namespace ProcessCoreOptimizer.WPF.Models
{
    #region Optimization Modes

    /// <summary>
    /// Defines the level and method of CPU core optimization applied to a specific process.
    /// </summary>
    public enum OptimizationMode
    {
        /// <summary>
        /// Hard binding to specific CPU cores. 
        /// This represents the classic, strict thread affinity method.
        /// </summary>
        Affinity,

        /// <summary>
        /// Soft binding via CPU Sets. 
        /// The Windows OS scheduler prioritizes selected cores for the target application, 
        /// but does not strictly restrict other threads during heavy system load.
        /// </summary>
        CpuSets,

        /// <summary>
        /// Strict core isolation. 
        /// The target application receives exclusive access to the selected cores, 
        /// actively evicting background system processes and other applications from them.
        /// </summary>
        Exclusive
    }

    #endregion
}