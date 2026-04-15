using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using ProcessCoreOptimizer.WPF.Models;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for managing application settings persistence 
    /// and handling system-level integrations like Windows Startup and Administrator elevation.
    /// </summary>
    public class SettingsService
    {
        #region Fields
        private readonly string _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private readonly string _appName = "ProcessCoreOptimizer";
        private readonly string _exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        #endregion

        #region Public Methods - Settings Persistence
        /// <summary>
        /// Loads application settings from the local JSON file. 
        /// Returns default settings if the file is missing or invalid.
        /// </summary>
        public AppSettings LoadSettings()
        {
            if (!File.Exists(_filePath)) return new AppSettings();

            try
            {
                string jsonContent = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppSettings>(jsonContent) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        /// <summary>
        /// Saves the provided settings to a JSON file and applies Windows startup configurations.
        /// </summary>
        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, options));
            }
            catch { /* Consider logging I/O failure */ }

            ApplyWindowsStartup(settings);
        }
        #endregion

        #region Public Methods - Elevation Logic
        /// <summary>
        /// Checks if the current process is running with elevated Administrator privileges.
        /// </summary>
        public bool IsRunAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Restarts the application and triggers a Windows UAC prompt to run as Administrator.
        /// </summary>
        public void RestartAsAdmin()
        {
            if (IsRunAsAdmin()) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = true,
                Verb = "runas" // Standard Windows verb for triggering UAC
            };

            try
            {
                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch
            {
                // User likely cancelled the UAC prompt; stay in current session
            }
        }
        #endregion

        #region Public Methods - Windows Integration
        /// <summary>
        /// Configures the application to start with Windows. 
        /// Uses the Registry for standard startup or Task Scheduler for Admin-privileged startup.
        /// </summary>
        public void ApplyWindowsStartup(AppSettings settings)
        {
            try
            {
                // Accessing the CurrentUser Run key for standard startup
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

                if (settings.StartWithWindows)
                {
                    if (settings.RunAsAdministrator)
                    {
                        // To start as Admin without showing a UAC prompt every boot, 
                        // we must create a task in the Windows Task Scheduler with 'Highest' privileges.
                        key?.DeleteValue(_appName, false);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/create /tn \"{_appName}\" /tr \"\\\"{_exePath}\\\"\" /sc onlogon /rl highest /f",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        })?.WaitForExit();
                    }
                    else
                    {
                        // Standard non-admin startup via Registry
                        RemoveScheduledTask();
                        key?.SetValue(_appName, $"\"{_exePath}\"");
                    }
                }
                else
                {
                    // Cleanup both startup methods if disabled
                    key?.DeleteValue(_appName, false);
                    RemoveScheduledTask();
                }
            }
            catch
            {
                // Likely insufficient permissions to modify Registry or Task Scheduler
            }
        }
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// Removes any existing entries for the application from the Windows Task Scheduler.
        /// </summary>
        private void RemoveScheduledTask()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{_appName}\" /f",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                })?.WaitForExit();
            }
            catch { }
        }
        #endregion
    }
}