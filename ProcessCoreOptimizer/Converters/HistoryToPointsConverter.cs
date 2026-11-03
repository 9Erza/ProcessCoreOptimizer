using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace ProcessCoreOptimizer.WPF.Converters
{
    /// <summary>
    /// Maps a numeric history to a 100×30 point box (XAML stretches it).
    /// Pure and unit-testable; the ViewModel precomputes the collections.
    /// </summary>
    public static class HistoryToPointsConverter
    {
        public static PointCollection ConvertToPoints(IEnumerable<double> history, double max)
        {
            var values = (history ?? Enumerable.Empty<double>())
                .Select(v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : v)
                .ToList();
            var points = new PointCollection();
            if (values.Count == 0) return points;
            if (max <= 0) max = 1;

            for (int i = 0; i < values.Count; i++)
            {
                double x = values.Count == 1 ? 100 : (i / (double)(values.Count - 1)) * 100;
                double y = 30 - (System.Math.Min(values[i], max) / max) * 30;
                points.Add(new System.Windows.Point(x, y));
            }

            return points;
        }
    }
}
