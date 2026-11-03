using ProcessCoreOptimizer.WPF.Helpers;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents a logical processor shown in the core selection panel.
    /// </summary>
    public class CoreInfo : ViewModelBase
    {
        private bool _isChecked = true;

        public int Index { get; set; }
        public int Group { get; set; }
        public int CoreIndex { get; set; }
        public byte EfficiencyClass { get; set; }
        public string TypeTag { get; set; } = "[P]";

        /// <summary>
        /// Windows CPU Set ID backing this logical processor, when known.
        /// Null for fallback topologies built without CPU Set data.
        /// </summary>
        public uint? CpuSetId { get; set; }

        /// <summary>
        /// True when P/E/SMT classification is a guess (WMI fallback).
        /// The UI must render this as Unknown instead of a confident label.
        /// </summary>
        public bool IsClassificationUnknown { get; set; }

        public string DisplayName => Group == 0
            ? $"Core {Index} {TypeTag}"
            : $"G{Group}: Core {Index} {TypeTag}";

        public bool IsThread { get; set; }
        public bool IsECore { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        private double _loadUsage;

        public double LoadUsage
        {
            get => _loadUsage;
            set => SetProperty(ref _loadUsage, value);
        }

        private bool _loadAvailable = true;

        /// <summary>False while the reading is unknown (first sample, missing counter) — UI shows "—".</summary>
        public bool LoadAvailable
        {
            get => _loadAvailable;
            set => SetProperty(ref _loadAvailable, value);
        }

        private double _utilityPercent = double.NaN;

        /// <summary>CPU Utility incl. frequency scaling; may exceed 100%. NaN = unknown.</summary>
        public double UtilityPercent
        {
            get => _utilityPercent;
            set
            {
                if (SetProperty(ref _utilityPercent, value)) OnPropertyChanged(nameof(UtilityText));
            }
        }

        public string UtilityText =>
            double.IsNaN(UtilityPercent) || double.IsInfinity(UtilityPercent) ? "—" : $"{UtilityPercent:F0}%";
    }
}
