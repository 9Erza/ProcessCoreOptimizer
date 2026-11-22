using System;
using System.Globalization;
using System.Windows.Data;

namespace ProcessCoreOptimizer.WPF.Converters
{
    /// <summary>
    /// True when every value has the same string representation (ordinal).
    /// Used by the sidebar so one nav style can mark the active tab by comparing
    /// ActiveTab with the button's own CommandParameter.
    /// </summary>
    public sealed class StringEqualsMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return false;
            string? first = values[0]?.ToString();
            if (first == null) return false;
            for (int i = 1; i < values.Length; i++)
            {
                if (!string.Equals(first, values[i]?.ToString(), StringComparison.Ordinal)) return false;
            }
            return true;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
