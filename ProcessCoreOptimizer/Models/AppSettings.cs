using System.Text.Json.Serialization;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents persistent user configuration.
    /// Startup uses versioned fields (see <see cref="Services.StartupSettingsMigration"/>):
    /// legacy 1.3 keys are read once via Legacy* properties, then cleared.
    /// </summary>
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public StartupWindowMode StartupWindowMode { get; set; } = StartupWindowMode.Normal;
        public bool StartupRunElevated { get; set; } = false;
        public int StartupSettingsVersion { get; set; } = 0;

        /// <summary>Legacy 1.3 key (StartMinimized). Migrated once, then cleared.</summary>
        [JsonPropertyName("StartMinimized")]
        public bool? LegacyStartMinimized { get; set; }
        /// <summary>Legacy 1.3 key (RunAsAdministrator). Migrated once, then cleared.</summary>
        [JsonPropertyName("RunAsAdministrator")]
        public bool? LegacyRunAsAdministrator { get; set; }

        /// <summary>-1 = pre-onboarding data, 0 = onboarding in progress, >=1 = completed.</summary>
        public int OnboardingCompletedVersion { get; set; } = -1;

        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public string Language { get; set; } = "en";

        /// <summary>
        /// RealTime priority can make Windows feel frozen. It is hidden unless the user explicitly enables it.
        /// </summary>
        public bool AllowRealtimePriority { get; set; } = false;

        public bool LogEnabled { get; set; } = true;
        public int LogLevelValue { get; set; } = 1; // Debug=0, Info=1, Warn=2, Error=3, Fatal=4
        public string LogFilePath { get; set; } = "ProcessCoreOptimizer.log";
        public bool EnableConsoleOutput { get; set; } = false;
        public string LogSourceName { get; set; } = "ProcessCoreOptimizer";

        public int ProcessListRefreshSeconds { get; set; } = 2;
        public int ProfileWatcherSeconds { get; set; } = 2;
        public int HardwareRefreshSeconds { get; set; } = 1;
        /// <summary>
        /// Hardware sensors are opt-in. The background profile watcher does not depend on this.
        /// </summary>
        public bool HardwareMonitorEnabled { get; set; } = false;
        public bool EnableStorageSensors { get; set; } = false;
        public bool CheckForUpdates { get; set; } = true;

        [JsonIgnore]
        public DesiredStartupConfiguration DesiredStartup =>
            new(StartWithWindows, StartupWindowMode, StartupRunElevated);
    }
}
