namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Represents the application configuration settings, 
    /// primarily focused on behavior and system integration.
    /// </summary>
    public class AppSettings
    {
        #region Startup Settings
        /// <summary>
        /// Gets or sets a value indicating whether the application 
        /// should start automatically when Windows logs in.
        /// </summary>
        public bool StartWithWindows { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the application 
        /// should start in a minimized state.
        /// </summary>
        public bool StartMinimized { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the application 
        /// requires elevated administrator privileges at startup.
        /// </summary>
        public bool RunAsAdministrator { get; set; } = false;
        #endregion

        #region Window Behavior Settings
        /// <summary>
        /// Gets or sets a value indicating whether the application 
        /// should hide to the system tray when minimized.
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether clicking the 'X' button 
        /// should minimize to the tray instead of closing the application.
        /// </summary>
        public bool CloseToTray { get; set; } = true;
        public string Language { get; set; } = "en";
        #endregion
    }
}