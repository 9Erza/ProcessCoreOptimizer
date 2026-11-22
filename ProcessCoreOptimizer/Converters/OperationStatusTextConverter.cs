using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProcessCoreOptimizer.WPF.Converters
{
    /// <summary>
    /// Shows an <c>OperationStatus</c> as a localized label (resource key
    /// <c>StrOp{Status}</c>), falling back to the enum name. Log files keep
    /// the stable English codes; only the Diagnostics grid is translated.
    /// </summary>
    public sealed class OperationStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "—";
            string name = value.ToString() ?? string.Empty;
            return System.Windows.Application.Current?.TryFindResource("StrOp" + name) as string ?? name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
