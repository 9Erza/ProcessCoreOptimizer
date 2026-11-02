using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Diagnostics;
using System.IO;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Pure, testable process-identity validation.
    /// A cached observation (PID snapshot) NEVER authorizes a mutation on its own:
    /// every mutating path must re-acquire PID + start time + name (+ EXE path when
    /// the profile is path-bound) immediately before touching the OS.
    /// </summary>
    public static class ProcessIdentityValidator
    {
        public static bool MatchesIdentityCore(
            int expectedPid,
            DateTime expectedStartTimeUtc,
            string? expectedProcessName,
            string? expectedExecutablePath,
            int actualPid,
            DateTime actualStartTimeUtc,
            string? actualProcessName,
            string? actualExecutablePath)
        {
            if (expectedPid <= 0 || actualPid <= 0) return false;
            if (expectedPid != actualPid) return false;
            if (expectedStartTimeUtc == default || actualStartTimeUtc == default) return false;
            if (expectedStartTimeUtc != actualStartTimeUtc) return false;

            if (string.IsNullOrWhiteSpace(expectedProcessName) || string.IsNullOrWhiteSpace(actualProcessName)) return false;
            string expectedName = ProfileService.NormalizeProcessName(expectedProcessName);
            string actualName = ProfileService.NormalizeProcessName(actualProcessName);
            if (!string.Equals(expectedName, actualName, StringComparison.OrdinalIgnoreCase)) return false;

            // Path-bound profiles never fall back to a same-name executable.
            if (!string.IsNullOrWhiteSpace(expectedExecutablePath))
            {
                if (string.IsNullOrWhiteSpace(actualExecutablePath)) return false;
                string? expectedNorm = ProfileService.NormalizeExecutablePath(expectedExecutablePath);
                string? actualNorm = ProfileService.NormalizeExecutablePath(actualExecutablePath);
                if (expectedNorm == null || actualNorm == null) return false;
                if (!string.Equals(expectedNorm, actualNorm, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        public static bool MatchesProfile(ProcessProfile profile, string? actualProcessName, string? actualExecutablePath)
        {
            return ProfileService.MatchesIdentity(profile, actualProcessName, actualExecutablePath);
        }

        public static bool MatchesLiveProcess(ProcessInstanceKey key, string processName, ProcessProfile profile)
        {
            if (key.ProcessId <= 0 || key.StartTimeUtc == default) return false;

            try
            {
                using var process = Process.GetProcessById(key.ProcessId);
                if (process.HasExited) return false;

                DateTime startUtc;
                try
                {
                    startUtc = process.StartTime.ToUniversalTime();
                }
                catch
                {
                    return false;
                }

                if (startUtc != key.StartTimeUtc) return false;

                string liveName;
                try
                {
                    liveName = process.ProcessName;
                }
                catch
                {
                    return false;
                }

                string? livePath;
                try
                {
                    livePath = process.MainModule?.FileName;
                }
                catch
                {
                    livePath = null;
                }

                // Snapshot name must still match (cheap pre-check before path logic).
                if (!string.Equals(
                    ProfileService.NormalizeProcessName(processName),
                    ProfileService.NormalizeProcessName(liveName),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return ProfileService.MatchesIdentity(profile, liveName, livePath);
            }
            catch
            {
                return false;
            }
        }

        public static string? TryGetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        public static string NormalizePath(string? path)
        {
            string? normalized = ProfileService.NormalizeExecutablePath(path);
            return normalized ?? string.Empty;
        }

        public static string ExecutableFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFileName(path) ?? string.Empty;
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}
