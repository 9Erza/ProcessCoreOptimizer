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
        public const string FactoryResetStampFileName = ".factory-reset-stamp";

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

            // A factory reset leaves a one-shot stamp: consume it and skip the
            // legacy import once, so wiped data is never silently resurrected.
            string stampPath = Path.Combine(UserDataDirectory, FactoryResetStampFileName);
            if (File.Exists(stampPath))
            {
                try { File.Delete(stampPath); } catch { }
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
