using ProcessCoreOptimizer.WPF.Helpers;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents a system process entry within the UI, 
    /// containing its current performance metrics and optimization status.
    /// </summary>
    public class ProcessItem : ViewModelBase
    {
        #region Private Backing Fields

        private string _cpuUsage = "0%";
        private string _ramUsageMB = "0 MB";
        private bool _isOptimized;
        private string _priority = "Normal";
        private string _modeTag = string.Empty;

        #endregion

        #region Process Identification

        /// <summary>
        /// Gets or sets the unique Process Identifier (PID).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the base name of the executable file.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets the formatted display name used in the UI, automatically appending the optimization mode tag if present.
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(ModeTag) ? Name : $"{Name} [{ModeTag}]";

        #endregion

        #region UI Binding & Telemetry

        /// <summary>
        /// Gets or sets the optimization mode tag (e.g., "CPU Sets", "Exclusive") to display next to the process name.
        /// </summary>
        public string ModeTag
        {
            get => _modeTag;
            set
            {
                if (SetProperty(ref _modeTag, value))
                {
                    // Automatically notify the UI that the DisplayName has also changed
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// Gets or sets the formatted CPU usage string (e.g., "5.2%").
        /// </summary>
        public string CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        /// <summary>
        /// Gets or sets the formatted RAM usage string (e.g., "256 MB").
        /// </summary>
        public string RamUsageMB
        {
            get => _ramUsageMB;
            set => SetProperty(ref _ramUsageMB, value);
        }

        /// <summary>
        /// Gets or sets the current process priority class as a string.
        /// </summary>
        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the process 
        /// is currently managed by a custom optimization profile.
        /// </summary>
        public bool IsOptimized
        {
            get => _isOptimized;
            set => SetProperty(ref _isOptimized, value);
        }

        #endregion
    }
}