using ProcessCoreOptimizer.WPF.Helpers;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents a saved optimization profile for a specific process, 
    /// storing its desired CPU affinity and priority levels.
    /// </summary>
    public class ProcessProfile : ViewModelBase
    {
        #region Private Fields
        private string _processName = string.Empty;
        private long _affinityMask;
        private string _priority = "Normal";
        private bool _isEnabled = true;
        #endregion

        #region Identification Properties
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
        /// Gets or sets the bitmask representing the CPU cores assigned to this process.
        /// </summary>
        public long AffinityMask
        {
            get => _affinityMask;
            set => SetProperty(ref _affinityMask, value);
        }

        /// <summary>
        /// Gets or sets the desired process priority class (e.g., "High", "AboveNormal").
        /// </summary>
        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this profile should be automatically applied.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
        #endregion
    }
}