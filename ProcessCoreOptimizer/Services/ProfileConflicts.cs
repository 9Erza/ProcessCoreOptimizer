using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Deterministic profile conflict analysis. The engine applies at most one
    /// profile per process instance with a fixed precedence (path-bound first,
    /// then newest), but overlapping enabled profiles are a user-visible conflict
    /// that the UI must show instead of silently picking a winner.
    /// </summary>
    public sealed class ProfileConflict
    {
        public string ProcessName { get; set; } = string.Empty;
        public List<ProcessProfile> Profiles { get; set; } = new();
        public ProcessProfile? Winner { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public static class ProfileConflicts
    {
        public static List<ProfileConflict> FindConflicts(IEnumerable<ProcessProfile> profiles)
        {
            var conflicts = new List<ProfileConflict>();

            var groups = profiles
                .Where(p => p != null && p.IsEnabled && !string.IsNullOrWhiteSpace(p.ProcessName))
                .GroupBy(p => ProfileService.NormalizeProcessName(p.ProcessName), StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var members = group.ToList();
                if (members.Count < 2) continue;
                if (members.Select(EffectiveSettingsKey).Distinct(StringComparer.Ordinal).Count() < 2) continue;

                conflicts.Add(new ProfileConflict
                {
                    ProcessName = group.Key,
                    Profiles = OrderByPrecedence(members),
                    Winner = ResolveWinner(members),
                    Reason = BuildReason(members)
                });
            }

            return conflicts.OrderBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Mirrors the engine precedence in OptimizationService: path-bound first, then newest.
        /// </summary>
        public static ProcessProfile ResolveWinner(IEnumerable<ProcessProfile> profiles)
        {
            return OrderByPrecedence(profiles).First();
        }

        public static List<ProcessProfile> OrderByPrecedence(IEnumerable<ProcessProfile> profiles)
        {
            return profiles
                .OrderByDescending(p => !string.IsNullOrWhiteSpace(p.ExecutablePath))
                .ThenByDescending(p => p.UpdatedAt)
                .ThenBy(p => p.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static string EffectiveSettingsKey(ProcessProfile p)
        {
            return $"{ProfileService.NormalizeProcessName(p.ProcessName)}|{p.AffinityMask:X}|{p.Priority}|{p.OptimizationMode}|{p.ApplyPriority}|{p.ApplyCoreOptimization}|eco:{(p.EcoQos.HasValue ? p.EcoQos.Value ? "1" : "0" : "-")}|path:{ProfileService.NormalizeExecutablePath(p.ExecutablePath) ?? "-"}";
        }

        private static string BuildReason(List<ProcessProfile> members)
        {
            bool pathMix = members.Any(p => !string.IsNullOrWhiteSpace(p.ExecutablePath))
                && members.Any(p => string.IsNullOrWhiteSpace(p.ExecutablePath));
            if (pathMix)
                return "Mix of path-bound and name-only profiles: the path-bound profile wins for matching executables, the name-only profile covers everything else.";
            if (members.Select(p => ProfileService.NormalizeExecutablePath(p.ExecutablePath) ?? "-").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                return "Multiple path-bound profiles for the same process name with different executables.";
            return "Multiple enabled profiles for the same process name with different settings; newest wins.";
        }
    }
}
