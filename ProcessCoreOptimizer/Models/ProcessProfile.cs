using ProcessCoreOptimizer.WPF.Helpers;
using System.Text.Json.Serialization;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents a saved optimization profile for a specific process.
    /// The persisted values are kept canonical and language-independent.
    /// UI-only display values are marked with JsonIgnore.
    /// </summary>
    public class ProcessProfile : ViewModelBase
    {
        #region Private Backing Fields

        private OptimizationMode _optimizationMode = OptimizationMode.Affinity;
        private string _processName = string.Empty;
        private long _affinityMask;
        private string _priority = "Normal";
        private bool _isEnabled = true;
        private string _displayPriority = "Normal";

        #endregion

        #region Process Identification

        /// <summary>
        /// Gets or sets the target process name without the .exe extension.
        /// </summary>
        public string ProcessName
        {
            get => _processName;
            set => SetProperty(ref _processName, value?.Trim() ?? string.Empty);
        }

        #endregion

        #region Optimization Settings

        /// <summary>
        /// Gets or sets the optimization method applied to this profile.
        /// </summary>
        public OptimizationMode OptimizationMode
        {
            get => _optimizationMode;
            set => SetProperty(ref _optimizationMode, value);
        }

        /// <summary>
        /// Gets or sets the bitmask representing the CPU cores assigned to this process.
        /// </summary>
        public long AffinityMask
        {
            get => _affinityMask;
            set => SetProperty(ref _affinityMask, value);
        }

        /// <summary>
        /// Gets or sets the canonical process priority class name, for example High or AboveNormal.
        /// This value should never be localized before saving.
        /// </summary>
        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value?.Trim() ?? "Normal");
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

        #region UI-only values

        /// <summary>
        /// Localized priority shown in the profiles grid. Not persisted to JSON.
        /// </summary>
        [JsonIgnore]
        public string DisplayPriority
        {
            get => _displayPriority;
            set => SetProperty(ref _displayPriority, value);
        }

        #endregion
    }
}
