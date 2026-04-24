using Microsoft.Win32;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for managing application settings persistence
    /// and handling system-level integrations like Windows Startup and Administrator elevation.
    /// </summary>
    public class SettingsService : IDisposable
    {
        #region Private Fields

        private readonly string _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private readonly string _appName = "ProcessCoreOptimizer";
        private readonly string _exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        private readonly ILogger _logger = LoggerService.Instance;
        private bool _disposed;

        #endregion

        #region Settings Persistence

        /// <summary>
        /// Loads application settings from the local JSON configuration file. 
        /// Returns default settings if the file is missing, empty, or invalid.
        /// </summary>
        /// <returns>The deserialized AppSettings object.</returns>
        public AppSettings LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                _logger.Info("Settings file not found - using default settings");
                return new AppSettings();
            }

            try
            {
                string jsonContent = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(jsonContent) ?? new AppSettings();
                _logger.Debug($"Settings loaded successfully from {_filePath}");
                return settings;
            }
            catch (JsonException ex)
            {
                _logger.Error($"Failed to deserialize settings - using default settings. Error: {ex.Message}", ex);
                return new AppSettings();
            }
            catch (IOException ex)
            {
                _logger.Error($"Failed to read settings file - using default settings. Error: {ex.Message}", ex);
                return new AppSettings();
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error loading settings - using default settings. Error: {ex.Message}", ex);
                return new AppSettings();
            }
        }

        /// <summary>
        /// Saves the provided settings to the local JSON file and immediately applies Windows startup configurations.
        /// </summary>
        /// <param name="settings">The AppSettings instance to serialize.</param>
        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonContent = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_filePath, jsonContent);
                _logger.Info($"Settings saved successfully to {_filePath}");
            }
            catch (JsonException ex)
            {
                _logger.Error($"Failed to serialize settings. Error: {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                _logger.Error($"Failed to write settings file. Error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error saving settings. Error: {ex.Message}", ex);
            }

            ApplyWindowsStartup(settings);
        }

        #endregion

        #region Elevation & UAC Logic

        /// <summary>
        /// Checks if the current application process is running with elevated Administrator privileges.
        /// </summary>
        /// <returns>True if the user is in the Administrator role; otherwise, false.</returns>
        public bool IsRunAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Restarts the application and triggers a Windows User Account Control (UAC) prompt 
        /// to elevate the process to Administrator rights.
        /// </summary>
        public void RestartAsAdmin()
        {
            if (IsRunAsAdmin()) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = true,
                Verb = "runas" // Standard Windows verb for triggering the elevation prompt
            };

            try
            {
                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch
            {
                // The user likely cancelled the UAC prompt; silently fail and remain in the current session
            }
        }

        #endregion

        #region Windows Startup Integration

        /// <summary>
        /// Configures the application to start with Windows. 
        /// Uses the Registry for standard startup, or an XML-defined Windows Task Scheduler entry for silent Admin-privileged startup.
        /// </summary>
        /// <param name="settings">The current application settings.</param>
        public void ApplyWindowsStartup(AppSettings settings)
        {
            try
            {
                _logger.Info($"Applying Windows startup configuration: Start={settings.StartWithWindows}, Admin={settings.RunAsAdministrator}");

                // Access the CurrentUser Run key for standard startup behavior
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

                if (settings.StartWithWindows)
                {
                    if (settings.RunAsAdministrator)
                    {
                        key?.DeleteValue(_appName, false);

                        // CRITICAL: We cannot create an elevated scheduled task without Admin rights.
                        // If the user hasn't elevated yet, bail out. The MainViewModel will re-trigger this upon admin restart.
                        if (!IsRunAsAdmin())
                        {
                            _logger.Warn("Cannot create elevated scheduled task - not running as admin");
                            return;
                        }

                        CreateAdvancedScheduledTask();
                    }
                    else
                    {
                        // Standard non-admin startup via the Windows Registry
                        RemoveScheduledTask();
                        key?.SetValue(_appName, $"\"{_exePath}\"");
                        _logger.Info("Registry startup entry created successfully");
                    }
                }
                else
                {
                    // Cleanup both startup methods if auto-start is disabled
                    key?.DeleteValue(_appName, false);
                    RemoveScheduledTask();
                    _logger.Info("Windows startup configuration removed");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to apply Windows startup configuration - likely insufficient permissions", ex);
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Generates a temporary XML configuration to create a highly-privileged scheduled task.
        /// This bypasses default Windows restrictions such as "Do not start on batteries" or execution time limits.
        /// </summary>
        private void CreateAdvancedScheduledTask()
        {
            string tempXmlFile = Path.GetTempFileName();

            try
            {
                _logger.Info($"Creating advanced scheduled task for {_appName}");

                // Raw XML schema required by Windows Task Scheduler
                string xmlConfig = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
                <Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                    </LogonTrigger>
                  </Triggers>
                  <Principals>
                    <Principal>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <RunOnlyIfIdle>false</RunOnlyIfIdle>
                    <WakeToRun>false</WakeToRun>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Actions>
                    <Exec>
                      <Command>""{_exePath}""</Command>
                    </Exec>
                  </Actions>
                </Task>";

                File.WriteAllText(tempXmlFile, xmlConfig);

                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"{_appName}\" /xml \"{tempXmlFile}\" /f",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                proc?.WaitForExit();
            }
            finally
            {
                if (File.Exists(tempXmlFile))
                {
                    File.Delete(tempXmlFile);
                }
            }
        }

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Logger jest singletonem i zarządzany przez GC, nie musimy go disposalować tutaj
        }

        #endregion

        /// <summary>
        /// Removes any existing auto-start entries for the application from the Windows Task Scheduler.
        /// </summary>
        private void RemoveScheduledTask()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{_appName}\" /f",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                proc?.WaitForExit();
            }
            catch
            {
                // Ignore cleanup errors if the task does not exist
            }
        }

        #endregion
    }
}