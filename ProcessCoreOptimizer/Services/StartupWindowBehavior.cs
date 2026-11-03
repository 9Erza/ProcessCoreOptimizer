using ProcessCoreOptimizer.WPF.Models;

namespace ProcessCoreOptimizer.WPF.Services
{
    public enum StartupWindowAction
    {
        ShowNormal,
        ShowMinimized,
        HideToTray
    }

    /// <summary>
    /// Pure decision table for the main window at startup.
    /// Minimized/Normal/Tray are distinct choices; MinimizeToTray governs only
    /// manual minimizing and never reinterprets the explicit startup mode.
    /// </summary>
    public static class StartupWindowBehavior
    {
        public static StartupWindowAction Resolve(
            StartupWindowMode settingMode,
            bool startMinimizedFlag,
            bool startToTrayFlag)
        {
            if (settingMode == StartupWindowMode.Tray || startToTrayFlag)
            {
                return StartupWindowAction.HideToTray;
            }

            if (settingMode == StartupWindowMode.Minimized || startMinimizedFlag)
            {
                return StartupWindowAction.ShowMinimized;
            }

            return StartupWindowAction.ShowNormal;
        }
    }
}
