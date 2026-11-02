using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Elevated helper protocol: the unelevated app re-launches itself with these
    /// arguments; UAC decides. Cancelled UAC (Win32 1223) is a clean cancel, not a crash.
    /// </summary>
    public sealed record StartupHelperCommand(bool RemoveTask, StartupWindowMode WindowMode)
    {
        public const string Flag = "--startup-helper";

        public static StartupHelperCommand? TryParse(string[] args)
        {
            if (args == null || args.Length < 2) return null;
            if (!args[0].Equals(Flag, StringComparison.OrdinalIgnoreCase)) return null;
            if (args[1].Equals("remove-task", StringComparison.OrdinalIgnoreCase) && args.Length == 2)
                return new StartupHelperCommand(true, StartupWindowMode.Normal);
            if (args[1].Equals("create-task", StringComparison.OrdinalIgnoreCase) && args.Length == 4
                && args[2].Equals("--mode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<StartupWindowMode>(args[3], ignoreCase: true, out var mode))
                return new StartupHelperCommand(false, mode);
            return null;
        }

        public string ToArguments() => RemoveTask
            ? $"{Flag} remove-task"
            : $"{Flag} create-task --mode {WindowMode.ToString().ToLowerInvariant()}";

        public static string[] ForwardedRestartArguments(string[] args)
        {
            string[] allowed = ["--tray", "--minimized", "--restarted"];
            return args
                .Where(a => allowed.Contains(a.ToLowerInvariant()))
                .Select(a => a.ToLowerInvariant())
                .Distinct()
                .ToArray();
        }
    }
}
