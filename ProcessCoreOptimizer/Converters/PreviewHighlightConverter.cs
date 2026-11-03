using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace ProcessCoreOptimizer.WPF.Converters
{
    /// <summary>
    /// True when a logical processor index belongs to the previewed preset set.
    /// Values: [0] = core Index (int), [1] = preview set (IEnumerable&lt;int&gt; or null).
    /// </summary>
    public sealed class PreviewHighlightConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return false;
            if (values[0] is not int index) return false;
            if (values[1] is IEnumerable<int> set) return set.Contains(index);
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
