using ProcessCoreOptimizer.WPF.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Safe text file writer using tmp + backup + atomic replace to avoid corrupt JSON files.
    /// </summary>
    public static class AtomicFileService
    {
        private static readonly ILogger Logger = LoggerService.Instance;
        private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);

        private static object GetLock(string filePath) => FileLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new object());

        public static void WriteAllTextAtomic(string filePath, string content)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = filePath + ".tmp";
            string backupPath = filePath + ".bak";

            lock (GetLock(filePath))
            {
                File.WriteAllText(tempPath, content, Encoding.UTF8);

                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Copy(filePath, backupPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Could not refresh backup for '{filePath}': {ex.Message}");
                    }
                }

                try
                {
                    // File.Replace is atomic on the same volume and preserves the backup copy.
                    if (File.Exists(filePath))
                    {
                        File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, filePath);
                    }
                }
                catch
                {
                    // Fallback for cross-volume or unusual filesystems: non-atomic copy.
                    File.Copy(tempPath, filePath, overwrite: true);
                    try { File.Delete(tempPath); } catch { }
                    throw;
                }

                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Could not delete temp file '{tempPath}': {ex.Message}");
                }
            }
        }

        public static string? ReadAllTextWithBackup(string filePath)
        {
            string backupPath = filePath + ".bak";

            lock (GetLock(filePath))
            {
                try
                {
                    return File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : null;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to read '{filePath}'. Trying backup. {ex.Message}");
                }

                try
                {
                    return File.Exists(backupPath) ? File.ReadAllText(backupPath, Encoding.UTF8) : null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to read backup '{backupPath}'", ex);
                    return null;
                }
            }
        }

        /// <summary>
        /// Reads the live file, falling back to the .bak copy when the live content
        /// is missing or fails the supplied validation (e.g. truncated JSON).
        /// Returns the first content that passes validation, or null.
        /// </summary>
        public static string? ReadValidatedWithBackup(string filePath, Func<string?, bool> isValid)
        {
            string? live;
            string backupPath = filePath + ".bak";

            lock (GetLock(filePath))
            {
                try
                {
                    live = File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : null;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to read '{filePath}'. Trying backup. {ex.Message}");
                    live = null;
                }

                if (isValid(live)) return live;

                if (!string.IsNullOrWhiteSpace(live))
                {
                    Logger.Warn($"Live file '{filePath}' failed validation. Trying backup.");
                }

                try
                {
                    string? backup = File.Exists(backupPath) ? File.ReadAllText(backupPath, Encoding.UTF8) : null;
                    if (isValid(backup)) return backup;
                    return null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to read backup '{backupPath}'", ex);
                    return null;
                }
            }
        }
    }
}
