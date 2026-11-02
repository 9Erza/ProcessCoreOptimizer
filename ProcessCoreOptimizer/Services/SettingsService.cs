using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.IO;
using System.Text.Json;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Handles settings persistence. Windows startup registration lives in
    /// <see cref="StartupService"/> + <see cref="StartupApplyCoordinator"/>
    /// (plan → execute → verify); this service never touches Registry/Task directly.
    /// </summary>
    public class SettingsService : IDisposable
    {
        private readonly string _filePath = AppPaths.GetUserDataFilePath("settings.json");
        private readonly ILogger _logger = LoggerService.Instance;
        private bool _disposed;

        public bool HadExistingSettings()
        {
            try
            {
                AppPaths.MigrateLegacyFileIfNeeded("settings.json");
                return File.Exists(_filePath);
            }
            catch
            {
                return false;
            }
        }

        public AppSettings LoadSettings()
        {
            AppPaths.MigrateLegacyFileIfNeeded("settings.json");

            if (!File.Exists(_filePath) && !File.Exists(_filePath + ".bak"))
            {
                return new AppSettings();
            }

            try
            {
                string? jsonContent = AtomicFileService.ReadValidatedWithBackup(_filePath, content =>
                {
                    if (string.IsNullOrWhiteSpace(content)) return true;
                    try
                    {
                        _ = JsonSerializer.Deserialize<AppSettings>(content);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (string.IsNullOrWhiteSpace(jsonContent)) return new AppSettings();
                var settings = JsonSerializer.Deserialize<AppSettings>(jsonContent) ?? new AppSettings();
                settings = SanitizeSettings(settings);
                StartupSettingsMigration.Apply(settings);
                return settings;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to load settings. Defaults will be used. {ex.Message}");
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                settings = SanitizeSettings(settings);
                string jsonContent = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileService.WriteAllTextAtomic(_filePath, jsonContent);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save settings", ex);
            }
        }

        private static AppSettings SanitizeSettings(AppSettings settings)
        {
            settings.Language = settings.Language == "pl" ? "pl" : "en";
            settings.LogLevelValue = Math.Clamp(settings.LogLevelValue, 0, 4);
            settings.LogFilePath = string.IsNullOrWhiteSpace(settings.LogFilePath)
                ? "ProcessCoreOptimizer.log"
                : settings.LogFilePath.Trim();
            settings.LogSourceName = string.IsNullOrWhiteSpace(settings.LogSourceName)
                ? "ProcessCoreOptimizer"
                : settings.LogSourceName.Trim();
            settings.ProcessListRefreshSeconds = Math.Clamp(settings.ProcessListRefreshSeconds, 1, 10);
            settings.ProfileWatcherSeconds = Math.Clamp(settings.ProfileWatcherSeconds, 1, 30);
            settings.HardwareRefreshSeconds = Math.Clamp(settings.HardwareRefreshSeconds, 1, 10);
            if (!Enum.IsDefined(settings.StartupWindowMode)) settings.StartupWindowMode = StartupWindowMode.Normal;
            return settings;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
