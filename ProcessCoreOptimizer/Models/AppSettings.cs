namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents persistent user configuration.
    /// </summary>
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool RunAsAdministrator { get; set; } = false;

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
    }
}
