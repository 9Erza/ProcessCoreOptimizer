using ProcessCoreOptimizer.WPF.Models;
using System;

namespace ProcessCoreOptimizer.WPF.Services
{
    public static class StartupCommandParser
    {
        public static (string? ExecutablePath, string Arguments) Parse(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return (null, string.Empty);
            string value = command.Trim();
            if (value[0] == '"')
            {
                int closing = value.IndexOf('"', 1);
                return closing < 0 ? (null, string.Empty) : (value[1..closing], value[(closing + 1)..].Trim());
            }

            int argumentStart = value.IndexOf(" --", StringComparison.Ordinal);
            return argumentStart < 0 ? (value, string.Empty) : (value[..argumentStart].Trim(), value[(argumentStart + 1)..].Trim());
        }
    }

    public static class StartupSettingsMigration
    {
        public const int CurrentVersion = 1;

        /// <summary>
        /// One-way migration of legacy 1.3 keys (StartMinimized, RunAsAdministrator)
        /// into the versioned startup model. Never touches user profiles.
        /// </summary>
        public static void Apply(AppSettings settings)
        {
            if (settings == null) return;
            if (settings.StartupSettingsVersion >= CurrentVersion)
            {
                settings.LegacyStartMinimized = null;
                settings.LegacyRunAsAdministrator = null;
                return;
            }

            if (settings.LegacyStartMinimized == true)
            {
                settings.StartupWindowMode = StartupWindowMode.Minimized;
            }

            if (settings.LegacyRunAsAdministrator == true)
            {
                settings.StartupRunElevated = true;
            }

            settings.LegacyStartMinimized = null;
            settings.LegacyRunAsAdministrator = null;
            settings.StartupSettingsVersion = CurrentVersion;
        }
    }
}
