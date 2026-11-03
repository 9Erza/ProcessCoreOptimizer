using System.Windows;
using System.Windows.Controls;

namespace ProcessCoreOptimizer.WPF.Views
{
    /// <summary>
    /// Compact, reusable CPU core tile (48-68 DIP). Two modes:
    /// editable (checkbox bound to a working selection, no OS writes on click)
    /// and read-only (status dot + load, optional preview highlight).
    /// Never stretches with the window.
    /// </summary>
    public partial class CoreTile : System.Windows.Controls.UserControl
    {
        public CoreTile()
        {
            InitializeComponent();
            UpdateMode();
        }

        public static readonly DependencyProperty TileLabelProperty =
            DependencyProperty.Register(nameof(TileLabel), typeof(string), typeof(CoreTile), new PropertyMetadata(string.Empty));

        public string TileLabel
        {
            get => (string)GetValue(TileLabelProperty);
            set => SetValue(TileLabelProperty, value);
        }

        public static readonly DependencyProperty IsTileCheckedProperty =
            DependencyProperty.Register(nameof(IsTileChecked), typeof(bool), typeof(CoreTile),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool IsTileChecked
        {
            get => (bool)GetValue(IsTileCheckedProperty);
            set => SetValue(IsTileCheckedProperty, value);
        }

        public static readonly DependencyProperty IsTileReadOnlyProperty =
            DependencyProperty.Register(nameof(IsTileReadOnly), typeof(bool), typeof(CoreTile),
                new PropertyMetadata(false, (d, e) => ((CoreTile)d).UpdateMode()));

        public bool IsTileReadOnly
        {
            get => (bool)GetValue(IsTileReadOnlyProperty);
            set => SetValue(IsTileReadOnlyProperty, value);
        }

        public static readonly DependencyProperty IsLoadAvailableProperty =
            DependencyProperty.Register(nameof(IsLoadAvailable), typeof(bool), typeof(CoreTile),
                new PropertyMetadata(true));

        public bool IsLoadAvailable
        {
            get => (bool)GetValue(IsLoadAvailableProperty);
            set => SetValue(IsLoadAvailableProperty, value);
        }

        public static readonly DependencyProperty UtilityTextProperty =
            DependencyProperty.Register(nameof(UtilityText), typeof(string), typeof(CoreTile),
                new PropertyMetadata("—"));

        public string UtilityText
        {
            get => (string)GetValue(UtilityTextProperty);
            set => SetValue(UtilityTextProperty, value);
        }

        public static readonly DependencyProperty LoadUsageProperty =
            DependencyProperty.Register(nameof(LoadUsage), typeof(double), typeof(CoreTile),
                new PropertyMetadata(0.0));

        public double LoadUsage
        {
            get => (double)GetValue(LoadUsageProperty);
            set => SetValue(LoadUsageProperty, value);
        }

        public static readonly DependencyProperty IsPreviewHighlightedProperty =
            DependencyProperty.Register(nameof(IsPreviewHighlighted), typeof(bool), typeof(CoreTile),
                new PropertyMetadata(false));

        public bool IsPreviewHighlighted
        {
            get => (bool)GetValue(IsPreviewHighlightedProperty);
            set => SetValue(IsPreviewHighlightedProperty, value);
        }

        private void UpdateMode()
        {
            if (TileCheck == null || ReadOnlyDot == null) return;
            TileCheck.Visibility = IsTileReadOnly ? Visibility.Collapsed : Visibility.Visible;
            ReadOnlyDot.Visibility = IsTileReadOnly ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
