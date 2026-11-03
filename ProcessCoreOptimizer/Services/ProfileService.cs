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
    /// Loads, validates, deduplicates, migrates and safely saves process optimization profiles.
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

            if (!File.Exists(_filePath) && !File.Exists(_filePath + ".bak"))
            {
                _logger.Debug("Profiles file not found - returning empty list");
                return new List<ProcessProfile>();
            }

            try
            {
                // Fall back to .bak when the live file is truncated/invalid JSON,
                // not only when the read itself throws.
                string? jsonContent = AtomicFileService.ReadValidatedWithBackup(_filePath, content =>
                {
                    if (string.IsNullOrWhiteSpace(content)) return true;
                    try
                    {
                        _ = JsonSerializer.Deserialize<List<ProcessProfile>>(content, JsonOptions);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (string.IsNullOrWhiteSpace(jsonContent)) return new List<ProcessProfile>();

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

                // Validate the serialized JSON before replacing the live file.
                _ = JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent, JsonOptions);

                AtomicFileService.WriteAllTextAtomic(_filePath, jsonContent);
                _logger.Info($"Profiles saved: {sanitized.Count}");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save profiles", ex);
            }
        }

        /// <summary>
        /// Creates an independent copy with a fresh identity for safe duplication.
        /// </summary>
        public static ProcessProfile DuplicateProfile(ProcessProfile source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new ProcessProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                SchemaVersion = 2,
                ProcessName = source.ProcessName,
                DisplayName = string.IsNullOrWhiteSpace(source.DisplayName)
                    ? $"{source.ProcessName} (copy)"
                    : $"{source.DisplayName} (copy)",
                ExecutablePath = source.ExecutablePath,
                AffinityMask = source.AffinityMask,
                Priority = source.Priority,
                OptimizationMode = source.OptimizationMode,
                ApplyPriority = source.ApplyPriority,
                ApplyCoreOptimization = source.ApplyCoreOptimization,
                IsEnabled = false,
                Notes = source.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Exports profiles to a user-chosen JSON file. Data only, no code is executed.
        /// </summary>
        public static void ExportProfiles(string filePath, IEnumerable<ProcessProfile> profiles)
        {
            var sanitized = SanitizeProfiles(profiles).ToList();
            string jsonContent = JsonSerializer.Serialize(sanitized, JsonOptions);
            _ = JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent, JsonOptions);
            AtomicFileService.WriteAllTextAtomic(filePath, jsonContent);
        }

        /// <summary>
        /// Imports profiles from a JSON file into the live store. Never overwrites blindly:
        /// imported profiles are merged and pass the same sanitize + precedence rules.
        /// Returns counts; the live file is only rewritten when at least one profile imports.
        /// </summary>
        public ProfileImportResult ImportProfiles(string filePath)
        {
            var result = new ProfileImportResult();
            try
            {
                if (!File.Exists(filePath))
                {
                    result.Errors.Add("File not found.");
                    return result;
                }

                string jsonContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var imported = JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent, JsonOptions)
                    ?? new List<ProcessProfile>();
                result.Read = imported.Count;

                var sanitized = SanitizeProfiles(imported).ToList();
                result.SkippedInvalid = result.Read - sanitized.Count;
                if (sanitized.Count == 0)
                {
                    result.Errors.Add("No valid profiles found in file.");
                    return result;
                }

                var current = LoadProfiles();
                SaveProfiles(MergeImported(current, sanitized));
                result.Imported = sanitized.Count;
                _logger.Info($"Profiles imported: {result.Imported} from '{filePath}' ({result.SkippedInvalid} invalid skipped)");
                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                _logger.Error("Failed to import profiles", ex);
                return result;
            }
        }

        /// <summary>
        /// Pure merge used by import: concatenates and re-sanitizes so the
        /// standard dedup + precedence rules decide the winners. Testable without IO.
        /// </summary>
        public static List<ProcessProfile> MergeImported(IEnumerable<ProcessProfile> current, IEnumerable<ProcessProfile> imported)
        {
            return SanitizeProfiles(current.Concat(imported)).ToList();
        }

        /// <summary>
        /// Finds a single profile by its stable <see cref="ProcessProfile.Id"/>.
        /// P0: Update/Delete must target the Id, never resolve by ProcessName
        /// (several path-bound profiles may share one process name).
        /// </summary>
        public static ProcessProfile? FindById(IEnumerable<ProcessProfile> profiles, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return profiles.FirstOrDefault(p => p != null && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Removes exactly the profile with the given <see cref="ProcessProfile.Id"/>.
        /// Siblings that share the process name are preserved.
        /// </summary>
        public static List<ProcessProfile> RemoveById(IEnumerable<ProcessProfile> profiles, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return profiles.ToList();
            return profiles.Where(p => p == null || !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Replaces the slot with the same (name + normalized path), or appends
        /// when no such slot exists. Pure; path-bound siblings are never merged.
        /// </summary>
        public static List<ProcessProfile> ReplacePathBoundOrAdd(IEnumerable<ProcessProfile> current, ProcessProfile incoming)
        {
            string name = NormalizeProcessName(incoming.ProcessName);
            string? path = NormalizeExecutablePath(incoming.ExecutablePath);
            var result = current
                .Where(p => p != null)
                .Where(p => !string.Equals(NormalizeProcessName(p.ProcessName), name, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(NormalizeExecutablePath(p.ExecutablePath), path, StringComparison.OrdinalIgnoreCase))
                .ToList();
            result.Add(incoming);
            return result;
        }

        /// <summary>
        /// Replaces the name-only profile for <paramref name="incoming"/> process name,
        /// preserving any path-bound siblings. Used when saving from a process row,
        /// where no executable path is known: only the name-only slot is replaced.
        /// </summary>
        public static List<ProcessProfile> ReplaceNameOnlyProfile(IEnumerable<ProcessProfile> current, ProcessProfile incoming)
        {
            string name = NormalizeProcessName(incoming.ProcessName);
            bool incomingHasPath = !string.IsNullOrWhiteSpace(NormalizeExecutablePath(incoming.ExecutablePath));
            return current
                .Where(p => p != null)
                .Where(p =>
                {
                    if (!string.Equals(NormalizeProcessName(p.ProcessName), name, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (incomingHasPath)
                        return !string.Equals(NormalizeExecutablePath(p.ExecutablePath), NormalizeExecutablePath(incoming.ExecutablePath), StringComparison.OrdinalIgnoreCase);
                    // Incoming is name-only: evict only the previous name-only slot,
                    // never path-bound siblings.
                    return !string.IsNullOrWhiteSpace(NormalizeExecutablePath(p.ExecutablePath));
                })
                .Concat(new[] { incoming })
                .ToList();
        }

        public static IEnumerable<ProcessProfile> SanitizeProfiles(IEnumerable<ProcessProfile> profiles)
        {
            return profiles
                .Where(p => p != null)
                .Select(SanitizeProfile)
                .Where(p => !string.IsNullOrWhiteSpace(p.ProcessName))
                // Priority-only profiles (ApplyCoreOptimization=false) legitimately have Mask==0.
                // Only core-bound profiles require a non-zero mask.
                .Where(p => !p.ApplyCoreOptimization || p.AffinityMask != 0)
                .Where(p => p.ApplyPriority || p.ApplyCoreOptimization)
                .GroupBy(p => BuildDedupKey(p), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(p => p.UpdatedAt).Last())
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildDedupKey(ProcessProfile profile)
        {
            string name = NormalizeProcessName(profile.ProcessName);
            string? path = NormalizeExecutablePath(profile.ExecutablePath);
            return string.IsNullOrWhiteSpace(path)
                ? $"name:{name}"
                : $"name:{name}|path:{path}";
        }

        private static ProcessProfile SanitizeProfile(ProcessProfile profile)
        {
            profile.SchemaVersion = 2;
            profile.Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id;
            profile.ProcessName = NormalizeProcessName(profile.ProcessName);
            profile.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.ProcessName : profile.DisplayName;
            profile.ExecutablePath = NormalizeExecutablePath(profile.ExecutablePath);
            profile.Priority = PriorityService.Normalize(profile.Priority, allowRealtime: true);
            profile.CreatedAt = profile.CreatedAt == default ? DateTime.UtcNow : profile.CreatedAt;
            profile.UpdatedAt = profile.UpdatedAt == default ? DateTime.UtcNow : profile.UpdatedAt;

#pragma warning disable CS0618
            if (profile.OptimizationMode == OptimizationMode.Exclusive)
            {
                profile.OptimizationMode = OptimizationMode.Affinity;
            }
#pragma warning restore CS0618

            return profile;
        }

        public static string NormalizeProcessName(string? processName)
        {
            string value = processName?.Trim() ?? string.Empty;
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(value)
                : value;
        }

        public static string? NormalizeExecutablePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        /// <summary>
        /// Path-bound profiles never fall back to a same-name executable with a different path.
        /// Profiles without a path match by process name only (legacy behaviour).
        /// </summary>
        public static bool MatchesIdentity(ProcessProfile profile, string? processName, string? executablePath)
        {
            if (profile == null) return false;
            if (!NormalizeProcessName(profile.ProcessName).Equals(NormalizeProcessName(processName), StringComparison.OrdinalIgnoreCase)) return false;
            string? profilePath = NormalizeExecutablePath(profile.ExecutablePath);
            if (profilePath is null) return true;
            string? actualPath = NormalizeExecutablePath(executablePath);
            return actualPath is not null && profilePath.Equals(actualPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ProfilesEquivalent(IReadOnlyList<ProcessProfile> left, IReadOnlyList<ProcessProfile> right)
        {
            if (left.Count != right.Count) return false;

            // Order-independent comparison: SanitizeProfiles sorts by name, raw load order may differ.
            var orderedLeft = left.OrderBy(p => BuildDedupKey(p), StringComparer.OrdinalIgnoreCase).ToList();
            var orderedRight = right
                .Select(p => new
                {
                    Name = NormalizeProcessName(p.ProcessName),
                    Path = NormalizeExecutablePath(p.ExecutablePath),
                    Mask = p.AffinityMask,
                    Priority = PriorityService.Normalize(p.Priority, allowRealtime: true),
                    Mode = NormalizeMode(p.OptimizationMode),
                    Enabled = p.IsEnabled,
                    ApplyP = p.ApplyPriority,
                    ApplyC = p.ApplyCoreOptimization
                })
                .OrderBy(p => string.IsNullOrWhiteSpace(p.Path) ? $"name:{p.Name}" : $"name:{p.Name}|path:{p.Path}", StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < orderedLeft.Count; i++)
            {
                var l = orderedLeft[i];
                var r = orderedRight[i];
                if (!string.Equals(l.ProcessName, r.Name, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(NormalizeExecutablePath(l.ExecutablePath), r.Path, StringComparison.OrdinalIgnoreCase)) return false;
                if (l.AffinityMask != r.Mask) return false;
                if (l.Priority != r.Priority) return false;
                if (l.OptimizationMode != r.Mode) return false;
                if (l.IsEnabled != r.Enabled) return false;
                if (l.ApplyPriority != r.ApplyP) return false;
                if (l.ApplyCoreOptimization != r.ApplyC) return false;
            }

            return true;
        }

        private static OptimizationMode NormalizeMode(OptimizationMode mode)
        {
#pragma warning disable CS0618
            return mode == OptimizationMode.Exclusive ? OptimizationMode.Affinity : mode;
#pragma warning restore CS0618
        }
    }

    public sealed class ProfileImportResult
    {
        public int Read { get; set; }
        public int Imported { get; set; }
        public int SkippedInvalid { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool Success => Imported > 0 && Errors.Count == 0;
    }
}
