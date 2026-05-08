using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Loads, validates, deduplicates and saves process optimization profiles.
    /// </summary>
    public class ProfileService
    {
        private static readonly ILogger _logger = LoggerService.Instance;
        private readonly string _filePath = AppPaths.GetUserDataFilePath("profiles.json");

        private static JsonSerializerOptions JsonOptions
        {
            get
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                options.Converters.Add(new JsonStringEnumConverter());
                return options;
            }
        }

        public List<ProcessProfile> LoadProfiles()
        {
            AppPaths.MigrateLegacyFileIfNeeded("profiles.json");

            if (!File.Exists(_filePath))
            {
                _logger.Debug("Profiles file not found - returning empty list");
                return new List<ProcessProfile>();
            }

            try
            {
                string jsonContent = File.ReadAllText(_filePath);
                var loadedProfiles = JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent, JsonOptions) ?? new List<ProcessProfile>();
                var sanitized = SanitizeProfiles(loadedProfiles).ToList();

                if (sanitized.Count != loadedProfiles.Count || !ProfilesEquivalent(sanitized, loadedProfiles))
                {
                    SaveProfiles(sanitized);
                }

                _logger.Info($"Profiles loaded: {sanitized.Count}");
                return sanitized;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load profiles - returning empty list", ex);
                return new List<ProcessProfile>();
            }
        }

        public void SaveProfiles(IEnumerable<ProcessProfile> profiles)
        {
            try
            {
                var sanitized = SanitizeProfiles(profiles).ToList();
                string jsonContent = JsonSerializer.Serialize(sanitized, JsonOptions);
                File.WriteAllText(_filePath, jsonContent);
                _logger.Info($"Profiles saved: {sanitized.Count}");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save profiles", ex);
            }
        }

        public static IEnumerable<ProcessProfile> SanitizeProfiles(IEnumerable<ProcessProfile> profiles)
        {
            return profiles
                .Where(p => p != null)
                .Select(SanitizeProfile)
                .Where(p => !string.IsNullOrWhiteSpace(p.ProcessName))
                .Where(p => p.AffinityMask != 0)
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase);
        }

        private static ProcessProfile SanitizeProfile(ProcessProfile profile)
        {
            profile.ProcessName = NormalizeProcessName(profile.ProcessName);
            profile.Priority = PriorityService.Normalize(profile.Priority, allowRealtime: true);

#pragma warning disable CS0618
            if (profile.OptimizationMode == OptimizationMode.Exclusive)
            {
                profile.OptimizationMode = OptimizationMode.Affinity;
            }
#pragma warning restore CS0618

            return profile;
        }

        private static string NormalizeProcessName(string? processName)
        {
            string value = processName?.Trim() ?? string.Empty;
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(value)
                : value;
        }

        private static bool ProfilesEquivalent(IReadOnlyList<ProcessProfile> left, IReadOnlyList<ProcessProfile> right)
        {
            if (left.Count != right.Count) return false;

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i].ProcessName, NormalizeProcessName(right[i].ProcessName), StringComparison.OrdinalIgnoreCase)) return false;
                if (left[i].AffinityMask != right[i].AffinityMask) return false;
                if (left[i].Priority != PriorityService.Normalize(right[i].Priority, allowRealtime: true)) return false;
                if (left[i].OptimizationMode != right[i].OptimizationMode) return false;
                if (left[i].IsEnabled != right[i].IsEnabled) return false;
            }

            return true;
        }
    }
}
