using ProcessCoreOptimizer.WPF.Helpers;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents a saved optimization profile for a specific process, 
    /// storing its desired CPU affinity, priority levels, and optimization mode.
    /// </summary>
    public class ProcessProfile : ViewModelBase
    {
        #region Private Backing Fields

        private OptimizationMode _optimizationMode = OptimizationMode.Affinity;
        private string _processName = string.Empty;
        private long _affinityMask;
        private string _priority = "Normal";
        private bool _isEnabled = true;

        #endregion

        #region Process Identification

        /// <summary>
        /// Gets or sets the target process name (without the .exe extension).
        /// </summary>
        public string ProcessName
        {
            get => _processName;
            set => SetProperty(ref _processName, value);
        }

        #endregion

        #region Optimization Settings

        /// <summary>
        /// Gets or sets the optimization method applied to this profile (e.g., Affinity, CpuSets, Exclusive).
        /// </summary>
        public OptimizationMode OptimizationMode
        {
            get => _optimizationMode;
            set => SetProperty(ref _optimizationMode, value);
        }

        /// <summary>
        /// Gets or sets the bitmask representing the CPU cores explicitly assigned to this process.
        /// </summary>
        public long AffinityMask
        {
            get => _affinityMask;
            set => SetProperty(ref _affinityMask, value);
        }

        /// <summary>
        /// Gets or sets the desired process priority class (e.g., "High", "RealTime", "AboveNormal").
        /// </summary>
        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this profile should be automatically applied in the background.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        #endregion
    }
}