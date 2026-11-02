using System;
using System.IO;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Centralized path handling for user-writable application files.
    /// PCO_USER_DATA_DIR (absolute) overrides the user directory for tests
    /// and portable scenarios; production default is %APPDATA%\ProcessCoreOptimizer.
    /// </summary>
    public static class AppPaths
    {
        public const string LegacySuppressStampPrefix = ".nolegacy.";

        public static string LegacySuppressStampFileName(string fileName) =>
            LegacySuppressStampPrefix + fileName;

        public static string UserDataDirectory
        {
            get
            {
                string? overrideDir = Environment.GetEnvironmentVariable("PCO_USER_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDir) && Path.IsPathRooted(overrideDir))
                {
                    return overrideDir;
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProcessCoreOptimizer");
            }
        }

        public static string GetUserDataFilePath(string fileName)
        {
            Directory.CreateDirectory(UserDataDirectory);
            return Path.Combine(UserDataDirectory, fileName);
        }

        public static string ResolveUserLogFilePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                configuredPath = "ProcessCoreOptimizer.log";
            }

            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : GetUserDataFilePath(configuredPath);
        }

        public static void MigrateLegacyFileIfNeeded(string fileName)
        {
            Directory.CreateDirectory(UserDataDirectory);

            // A factory reset leaves persistent per-file stamps so later launches
            // never silently reimport wiped data. Each stamp is checked only by
            // its own file's migration, making the decision order-independent.
            // Stamps persist until the user deletes them explicitly.
            string stampPath = Path.Combine(UserDataDirectory, LegacySuppressStampFileName(fileName));
            if (File.Exists(stampPath))
            {
                return;
            }

            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            string newPath = GetUserDataFilePath(fileName);

            if (!File.Exists(legacyPath) || File.Exists(newPath))
            {
                return;
            }

            File.Copy(legacyPath, newPath, overwrite: false);
        }
    }
}
