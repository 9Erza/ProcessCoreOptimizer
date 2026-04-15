using ProcessCoreOptimizer.WPF.Helpers;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents detailed information about a specific logical processor core,
    /// including its architectural type and current performance metrics.
    /// </summary>
    public class CoreInfo : ViewModelBase
    {
        #region Private Fields

        private bool _isChecked = true;
        private double _loadUsage;

        #endregion

        #region Core Identification

        /// <summary>
        /// Gets or sets the logical index of the CPU core as assigned by the OS.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the architectural tag (e.g., [P] for Performance, [E] for Efficiency, [T] for Thread).
        /// </summary>
        public string TypeTag { get; set; } = "[P]";

        /// <summary>
        /// Gets the formatted display name used in the UI (e.g., "Core 0 [P]").
        /// </summary>
        public string DisplayName => $"Core {Index} {TypeTag}";

        #endregion

        #region Hardware Architecture Flags

        /// <summary>
        /// Gets or sets a value indicating whether this logical processor is an SMT/Hyper-Threading logical thread.
        /// </summary>
        public bool IsThread { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this is an Efficiency core (e.g., in Intel Hybrid Architecture).
        /// </summary>
        public bool IsECore { get; set; }

        #endregion

        #region UI Binding & Telemetry

        /// <summary>
        /// Gets or sets a value indicating whether the core is selected by the user for process affinity.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        /// <summary>
        /// Gets or sets the current real-time CPU load percentage for this specific core.
        /// </summary>
        public double LoadUsage
        {
            get => _loadUsage;
            set => SetProperty(ref _loadUsage, value);
        }

        #endregion
    }
}