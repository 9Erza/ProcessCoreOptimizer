using System;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Required identity for any OS mutation. Unlike snapshots (which may carry
    /// unknown fields), a mutation target is only valid when every field needed
    /// for verification is present. There is no null/default bypass: invalid
    /// targets are refused before any handle is opened.
    /// </summary>
    public readonly record struct ProcessMutationTarget(
        int ProcessId,
        DateTime StartTimeUtc,
        string ProcessName,
        string? ExecutablePath)
    {
        public bool IsValid =>
            ProcessId > 0
            && StartTimeUtc != default
            && !string.IsNullOrWhiteSpace(ProcessName);

        public static bool TryCreate(
            int processId,
            DateTime startTimeUtc,
            string? processName,
            string? executablePath,
            out ProcessMutationTarget target)
        {
            target = default;
            if (processId <= 0) return false;
            if (startTimeUtc == default) return false;
            if (string.IsNullOrWhiteSpace(processName)) return false;

            target = new ProcessMutationTarget(processId, startTimeUtc, processName, executablePath);
            return true;
        }
    }
}
