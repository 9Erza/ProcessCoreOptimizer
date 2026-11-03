using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Pure builders from scan data to UI rows. No OS access, no IO — fully testable.
    /// </summary>
    public static class ProcessRowBuilder
    {
        public static string FormatCpu(double? percent)
        {
            if (!percent.HasValue || double.IsNaN(percent.Value) || double.IsInfinity(percent.Value))
                return "…";
            return $"{percent.Value:F1}%";
        }

        public static string FormatRam(long? bytes)
        {
            if (!bytes.HasValue) return "…";
            return $"{bytes.Value / 1024 / 1024} MB";
        }

        /// <summary>
        /// Builds flat per-instance rows from drill-down data.
        /// Profile names are attached when a profile matches (name or name+path).
        /// </summary>
        public static List<ProcessInstanceRow> BuildInstanceRows(
            IEnumerable<ProcessInstanceData> data,
            IEnumerable<ProcessProfile> profiles,
            Func<string, string>? translatePriority = null)
        {
            var list = data.ToList();
            var result = new List<ProcessInstanceRow>(list.Count);
            foreach (var d in list)
            {
                var row = new ProcessInstanceRow
                {
                    Key = d.Key,
                    Name = d.ProcessName,
                    GroupName = d.ProcessName,
                    ExecutablePath = d.ExecutablePath,
                    CpuUsagePercent = d.CpuUsagePercent,
                    MemoryBytes = d.WorkingSetBytes,
                    Priority = d.Priority?.ToString() ?? "Unknown"
                };
                row.CpuUsage = FormatCpu(d.CpuUsagePercent);
                row.RamUsageMB = FormatRam(d.WorkingSetBytes);
                if (translatePriority != null && d.Priority.HasValue)
                    row.Priority = translatePriority(d.Priority.Value.ToString());

                var match = profiles
                    .Where(p => p.IsEnabled)
                    .FirstOrDefault(p => ProfileService.MatchesIdentity(p, d.ProcessName, d.ExecutablePath));
                row.ProfileName = match != null ? match.DisplayName : string.Empty;
                result.Add(row);
            }

            return result;
        }

        /// <summary>
        /// P0 scope guard: partitioning a key set never widens it. Applying to an
        /// empty selection is a no-op (never falls back to the whole group).
        /// </summary>
        public static bool IsScopedSelectionValid(IReadOnlyCollection<ProcessInstanceKey>? keys)
        {
            return keys != null && keys.Count > 0;
        }
    }
}
