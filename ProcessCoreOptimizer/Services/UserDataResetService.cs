using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.WPF.Services
{
    public sealed record DataResetResult(
        bool Success,
        bool Blocked,
        IReadOnlyList<string> DeletedFiles,
        IReadOnlyList<string> DeletedDirectories,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Notes,
        string? StartupRemovalError,
        string? ActiveLogPath);

    /// <summary>
    /// Restore-defaults and factory-reset for PCO user data. Operates only inside
    /// an explicit root directory (production: %APPDATA%\ProcessCoreOptimizer).
    /// Refuses to run when a mutation-relevant operation is in flight and refuses
    /// any path that escapes the root or passes through a link/junction.
    /// </summary>
    public sealed class UserDataResetService
    {
        private static readonly string[] ManagedFileNames =
        {
            "settings.json", "settings.json.bak",
            "profiles.json", "profiles.json.bak",
            "ProcessCoreOptimizer.log",
            "ProcessCoreOptimizer.log.1", "ProcessCoreOptimizer.log.2", "ProcessCoreOptimizer.log.3",
            "watcher-cache.json", "watcher-cache.json.bak",
            "diagnostics.json",
            "first-run.json"
        };

        private readonly string _rootDirectory;
        private readonly ILogger _logger;

        public UserDataResetService(string rootDirectory, ILogger? logger = null)
        {
            _rootDirectory = rootDirectory;
            _logger = logger ?? LoggerService.Instance;
        }

        public static UserDataResetService ForCurrentUser() =>
            new(AppPaths.UserDataDirectory);

        /// <summary>
        /// Builds fresh defaults, preserving the UI language (so post-reset
        /// messages stay readable) and resetting onboarding to "in progress".
        /// Does not touch profiles. Caller persists + reloads + reconciles startup.
        /// </summary>
        public static AppSettings BuildDefaultSettings(AppSettings current)
        {
            return new AppSettings
            {
                Language = current.Language == "pl" ? "pl" : "en",
                OnboardingCompletedVersion = 0,
                MinimizeToTray = true,
                CloseToTray = true
            };
        }

        public async Task<DataResetResult> FactoryResetAsync(
            Func<bool> isBusy,
            Func<Task> prepareForWipeAsync,
            Func<Task<string?>> removeStartupAsync,
            CancellationToken cancellationToken = default)
        {
            if (isBusy())
            {
                return new DataResetResult(false, true, [], [], [], [], null, null);
            }

            string root;
            try
            {
                root = Path.GetFullPath(_rootDirectory);
            }
            catch (Exception ex)
            {
                return new DataResetResult(false, false, [], [], new[] { $"Invalid data directory: {ex.Message}" }, [], null, null);
            }

            var deletedFiles = new List<string>();
            var deletedDirs = new List<string>();
            var errors = new List<string>();
            var notes = new List<string>();

            // Pre-flight: refuse if the root itself is (or contains) a link.
            // A missing root is fine (created below); only an existing,
            // unreadable or reparse-point root refuses the wipe.
            try
            {
                if (Directory.Exists(root) && IsLink(root))
                {
                    return new DataResetResult(false, false, [], [], new[] { $"Refused: data directory '{root}' is a link or junction." }, [], null, null);
                }
            }
            catch (Exception ex)
            {
                return new DataResetResult(false, false, [], [], new[] { $"Cannot inspect data directory: {ex.Message}" }, [], null, null);
            }

            await prepareForWipeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            string? activeLog = null;
            try
            {
                if (Directory.Exists(root))
                {
                    foreach (string name in ManagedFileNames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string candidate = Path.Combine(root, name);
                        if (IsActiveLogFile(candidate))
                        {
                            activeLog = candidate;
                            continue; // logger holds the handle; caller truncates after reconfiguring.
                        }

                        DeleteGuardedFile(root, candidate, deletedFiles, errors);
                    }

                    // Any other *.tmp owned by PCO crashes/legacy runs.
                    foreach (string tmp in Directory.EnumerateFiles(root, "*.tmp"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DeleteGuardedFile(root, tmp, deletedFiles, errors);
                    }

                    foreach (string dir in Directory.EnumerateDirectories(root))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DeleteGuardedDirectory(root, dir, deletedDirs, notes, errors);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"Wipe failed: {ex.Message}");
            }

            string? startupError = null;
            try
            {
                startupError = await removeStartupAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                startupError = $"Startup removal failed: {ex.Message}";
            }

            // Leave one-shot stamps (one per data file) so the next launch does not
            // reimport legacy files from the executable directory.
            try
            {
                Directory.CreateDirectory(root);
                foreach (string dataFile in new[] { "settings.json", "profiles.json" })
                {
                    File.WriteAllText(
                        Path.Combine(root, AppPaths.LegacySuppressStampFileName(dataFile)),
                        DateTime.UtcNow.ToString("o"));
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Could not write reset stamp: {ex.Message}");
            }

            bool success = errors.Count == 0;
            _logger.Info($"Factory reset complete. Success={success}, deleted {deletedFiles.Count} file(s), {deletedDirs.Count} dir(s).");
            return new DataResetResult(success, false, deletedFiles, deletedDirs, errors, notes, startupError, activeLog);
        }

        private static bool IsActiveLogFile(string candidate)
        {
            try
            {
                return string.Equals(Path.GetFullPath(candidate),
                    Path.GetFullPath(AppPaths.ResolveUserLogFilePath("ProcessCoreOptimizer.log")),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLink(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                // Fail-closed: an unreadable path is treated as unsafe in a
                // destructive operation, never as implicitly safe.
                return true;
            }
        }

        private static void DeleteGuardedFile(string root, string candidate, List<string> deleted, List<string> errors)
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Refused: '{candidate}' escapes the data directory.");
                    return;
                }

                if (!File.Exists(full)) return;
                if (IsLink(full))
                {
                    errors.Add($"Refused: '{full}' is a link; delete it manually.");
                    return;
                }

                File.Delete(full);
                deleted.Add(Path.GetFileName(full));
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot delete '{candidate}': {ex.Message}");
            }
        }

        private void DeleteGuardedDirectory(string root, string candidate, List<string> deleted, List<string> notes, List<string> errors)
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Refused: '{candidate}' escapes the data directory.");
                    return;
                }

                if (IsLink(full))
                {
                    errors.Add($"Refused: directory '{full}' is a link; delete it manually.");
                    return;
                }

                // Only empty directories are removed automatically; user content stays.
                if (!Directory.EnumerateFileSystemEntries(full).Any())
                {
                    Directory.Delete(full);
                    deleted.Add(Path.GetFileName(full));
                }
                else
                {
                    notes.Add($"Kept non-empty directory '{Path.GetFileName(full)}' (user content preserved).");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot remove directory '{candidate}': {ex.Message}");
            }
        }
    }
}
