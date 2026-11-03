using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Safe wrapper around process priority, affinity and CPU Sets APIs.
    /// Every mutation requires a valid <see cref="Models.ProcessMutationTarget"/>
    /// (PID + start time + process name + EXE path when bound), re-validated
    /// immediately before touching the OS. There is no null/default bypass:
    /// invalid targets are refused before any handle is opened.
    /// </summary>
    public class ProcessService
    {
        private static readonly ILogger _logger = LoggerService.Instance;

        private static readonly HashSet<string> SystemProcessBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "taskhostw", "explorer", "sihost", "searchhost",
            "startmenuexperiencehost", "runtimebroker", "applicationframehost",
            "shellhost", "system", "idle", "conhost", "wmiprvse", "ctfmon",
            "fontdrvhost", "dwm", "spoolsv", "lsass", "csrss", "smss", "winlogon",
            "services", "registry", "securityhealthservice", "audiodg", "taskmgr"
        };

        public static bool IsProtectedProcessName(string? processName)
        {
            return !string.IsNullOrWhiteSpace(processName) && SystemProcessBlacklist.Contains(processName.Trim());
        }

        public bool IsUserProcess(Process p)
        {
            try
            {
                if (p.Id <= 4 || p.HasExited) return false;
                if (p.SessionId == 0) return false;
                if (SystemProcessBlacklist.Contains(p.ProcessName)) return false;

                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Could not classify process as user process: {ex.Message}");
                return false;
            }
        }

        public virtual bool SetProcessAffinity(int pid, long mask, ProcessMutationTarget target)
        {
            if (mask == 0) return false;
            if (!target.IsValid || target.ProcessId != pid)
            {
                _logger.Warn($"Refusing affinity change for PID {pid}: incomplete mutation identity.");
                return false;
            }

            try
            {
                if (!VerifyIdentityBeforeMutation(pid, target))
                {
                    return false;
                }

                using var proc = Process.GetProcessById(pid);
                if (!MatchesExpectedIdentity(proc, target))
                {
                    _logger.Warn($"Process identity mismatch for PID {pid}; affinity not set.");
                    return false;
                }

                proc.ProcessorAffinity = (IntPtr)mask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to set CPU affinity for PID {pid}: {ex.Message}");
                return false;
            }
        }

        public virtual bool SetPriority(int pid, ProcessPriorityClass priority, ProcessMutationTarget target)
        {
            if (!target.IsValid || target.ProcessId != pid)
            {
                _logger.Warn($"Refusing priority change for PID {pid}: incomplete mutation identity.");
                return false;
            }

            try
            {
                IntPtr hProc = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                    false, pid);
                if (hProc == IntPtr.Zero)
                {
                    _logger.Warn($"OpenProcess failed for PID {pid} during priority set: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                try
                {
                    if (!TryGetProcessStartTimeUtc(hProc, out DateTime startUtc)
                        || startUtc != target.StartTimeUtc)
                    {
                        _logger.Warn($"Process identity start time mismatch or process recycled for PID {pid}; priority not set.");
                        return false;
                    }

                    using var proc = Process.GetProcessById(pid);
                    if (!MatchesExpectedIdentity(proc, target))
                    {
                        _logger.Warn($"Process identity mismatch for PID {pid}; priority not set.");
                        return false;
                    }

                    bool ok = NativeMethods.SetPriorityClass(hProc, (uint)priority);
                    if (!ok)
                    {
                        _logger.Warn($"SetPriorityClass failed for PID {pid}: {Marshal.GetLastWin32Error()}");
                        return false;
                    }

                    return true;
                }
                finally
                {
                    NativeMethods.CloseHandle(hProc);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to set priority for PID {pid}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Use <see cref="TryGetPriority"/> for new code (no string guessing).</summary>
        public virtual ProcessPriorityClass? TryGetPriority(int pid)
        {
            try
            {
                IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProc == IntPtr.Zero) return null;

                try
                {
                    uint prio = NativeMethods.GetPriorityClass(hProc);
                    if (prio == 0) return null;
                    return (ProcessPriorityClass)prio;
                }
                finally
                {
                    NativeMethods.CloseHandle(hProc);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read priority for PID {pid}: {ex.Message}");
                return null;
            }
        }

        /// <summary>EcoQoS (Efficiency mode) needs Windows 10 1809+ (build 17763).</summary>
        public static bool IsEcoQosSupported()
        {
            try
            {
                var v = Environment.OSVersion.Version;
                return v.Major > 10 || (v.Major == 10 && v.Build >= 17763);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Best-effort EcoQoS read. Null Enabled (with an Error) means the OS
        /// refused or the feature is unsupported — never an invented value.
        /// </summary>
        public virtual (bool Readable, bool? Enabled, string? Error) TryGetEcoQos(int pid)
        {
            if (!IsEcoQosSupported()) return (false, null, "ERR_ECOQOS_UNSUPPORTED");
            try
            {
                IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProc == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    return (false, null, error == 5 ? "ERR_ACCESS_DENIED_5" : $"ERR_OPEN_PROCESS_{error}");
                }

                try
                {
                    // The GET requires Version preset to CURRENT_VERSION (else ERROR_INVALID_PARAMETER).
                    var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
                    {
                        Version = NativeMethods.POWER_THROTTLING_CURRENT_VERSION
                    };
                    bool ok = NativeMethods.GetProcessInformation(
                        hProc,
                        NativeMethods.ProcessPowerThrottling,
                        ref state,
                        (uint)Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>());
                    if (!ok)
                    {
                        int error = Marshal.GetLastWin32Error();
                        return (false, null, error == 5 ? "ERR_ACCESS_DENIED_5" : $"ERR_ECOQOS_READ_{error}");
                    }

                    bool enabled = (state.ControlMask & NativeMethods.POWER_THROTTLING_EXECUTION_SPEED) != 0
                        && (state.StateMask & NativeMethods.POWER_THROTTLING_EXECUTION_SPEED) != 0;
                    return (true, enabled, null);
                }
                finally
                {
                    NativeMethods.CloseHandle(hProc);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read EcoQoS for PID {pid}: {ex.Message}");
                return (false, null, "ERR_ECOQOS_READ_EXCEPTION");
            }
        }

        /// <summary>
        /// Explicit EcoQoS on/off with the same identity discipline as priority
        /// changes: incomplete identity or recycled PID is refused, never set.
        /// </summary>
        public virtual (bool Success, string Message, bool RequiresAdmin) SetEcoQos(int pid, bool enable, ProcessMutationTarget target)
        {
            if (!IsEcoQosSupported()) return (false, "ERR_ECOQOS_UNSUPPORTED", false);
            if (!target.IsValid || target.ProcessId != pid)
            {
                _logger.Warn($"Refusing EcoQoS change for PID {pid}: incomplete mutation identity.");
                return (false, "ERR_IDENTITY_MISMATCH", false);
            }

            try
            {
                IntPtr hProc = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                    false, pid);
                if (hProc == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    _logger.Warn($"OpenProcess failed for PID {pid} during EcoQoS set: {error}");
                    return (false, $"ERR_OPEN_PROCESS_{error}", error == 5);
                }

                try
                {
                    if (!TryGetProcessStartTimeUtc(hProc, out DateTime startUtc)
                        || startUtc != target.StartTimeUtc)
                    {
                        _logger.Warn($"Process identity start time mismatch or process recycled for PID {pid}; EcoQoS not set.");
                        return (false, "ERR_IDENTITY_MISMATCH", false);
                    }

                    using var proc = Process.GetProcessById(pid);
                    if (!MatchesExpectedIdentity(proc, target))
                    {
                        _logger.Warn($"Process identity mismatch for PID {pid}; EcoQoS not set.");
                        return (false, "ERR_IDENTITY_MISMATCH", false);
                    }

                    uint mask = enable ? NativeMethods.POWER_THROTTLING_EXECUTION_SPEED : 0;
                    var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
                    {
                        Version = NativeMethods.POWER_THROTTLING_CURRENT_VERSION,
                        ControlMask = NativeMethods.POWER_THROTTLING_EXECUTION_SPEED,
                        StateMask = mask
                    };
                    bool ok = NativeMethods.SetProcessInformation(
                        hProc,
                        NativeMethods.ProcessPowerThrottling,
                        ref state,
                        (uint)Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>());
                    if (!ok)
                    {
                        int error = Marshal.GetLastWin32Error();
                        _logger.Warn($"SetProcessInformation (EcoQoS) failed for PID {pid}: {error}");
                        return (false, $"ERR_ECOQOS_{error}", error == 5);
                    }

                    return (true, enable ? "OK_ECOQOS_ON" : "OK_ECOQOS_OFF", false);
                }
                finally
                {
                    NativeMethods.CloseHandle(hProc);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to set EcoQoS for PID {pid}: {ex.Message}");
                return (false, "ERR_ECOQOS_EXCEPTION", false);
            }
        }

        public virtual string ApplyCoreOptimization(
            int pid, long mask, OptimizationMode mode, Dictionary<int, uint> cpuSetMap,
            ProcessMutationTarget target)
        {
            if (!target.IsValid || target.ProcessId != pid)
            {
                _logger.Warn($"Refusing core optimization for PID {pid}: incomplete mutation identity.");
                return "ERR_IDENTITY_MISMATCH";
            }

            string? maskError = ValidateCoreMask(mask, cpuSetMap);
            if (maskError != null) return maskError;

#pragma warning disable CS0618
            if (mode == OptimizationMode.Exclusive)
            {
                _logger.Warn("Legacy Exclusive mode detected. Falling back to Affinity.");
                mode = OptimizationMode.Affinity;
            }
#pragma warning restore CS0618

            try
            {
                if (!VerifyIdentityBeforeMutation(pid, target))
                {
                    return "ERR_IDENTITY_MISMATCH";
                }

                return mode switch
                {
                    OptimizationMode.Affinity => ApplyAffinityMode(pid, mask, target),
                    OptimizationMode.CpuSets => ApplyCpuSetsMode(pid, mask, cpuSetMap, target),
                    _ => "ERR_UNKNOWN_MODE"
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to apply core optimization to PID {pid}: {ex.Message}");
                return $"ERR_EXC: {ex.Message}";
            }
        }

        private string ApplyAffinityMode(int pid, long mask, ProcessMutationTarget target)
        {
            using var proc = Process.GetProcessById(pid);
            if (!MatchesExpectedIdentity(proc, target))
            {
                _logger.Warn($"Process identity mismatch for PID {pid}; affinity not set.");
                return "ERR_IDENTITY_MISMATCH";
            }

            proc.ProcessorAffinity = (IntPtr)mask;
            // Affinity and CPU Sets are independent mechanisms: the OS keeps stale
            // CPU Sets unless we clear them. A failed clear must stay visible instead
            // of hiding behind a plain OK_AFFINITY.
            bool cleared = ClearCpuSets(pid, target);
            return ResolveAffinityResult(cleared);
        }

        /// <summary>
        /// Testable mapping of the affinity outcome. A failed CPU Sets cleanup is
        /// still a success for the affinity itself, but the caller must see it.
        /// </summary>
        public static string ResolveAffinityResult(bool cpuSetsCleared) =>
            cpuSetsCleared ? "OK_AFFINITY" : "OK_AFFINITY_CPUSETS_NOT_CLEARED";

        /// <summary>
        /// Clears explicit CPU Sets of a verified instance (restores the
        /// no-explicit-sets state). Refuses recycled PIDs like every mutation.
        /// </summary>
        public virtual bool ClearProcessCpuSets(int pid, ProcessMutationTarget target)
        {
            if (!target.IsValid || target.ProcessId != pid)
            {
                _logger.Warn($"Refusing CPU Sets clear for PID {pid}: incomplete mutation identity.");
                return false;
            }

            return ClearCpuSets(pid, target);
        }

        private string ApplyCpuSetsMode(int pid, long mask, Dictionary<int, uint> cpuSetMap, ProcessMutationTarget target)
        {
            if (cpuSetMap.Count == 0)
            {
                return "ERR_CPUSET_MAP_EMPTY";
            }

            uint[] selectedCpuSetIds = ConvertMaskToCpuSetIds(mask, cpuSetMap);
            if (selectedCpuSetIds.Length == 0)
            {
                return "ERR_NO_VALID_CPUSETS";
            }

            IntPtr hProc = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_SET_LIMITED_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                false, pid);
            if (hProc == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                return $"ERR_OPEN_PROCESS_{error}";
            }

            try
            {
                // Start-time gate on the stable handle, then full name/path
                // re-check before the write: a recycled PID never gets foreign sets.
                if (!TryGetProcessStartTimeUtc(hProc, out DateTime startUtc)
                    || startUtc != target.StartTimeUtc)
                {
                    return "ERR_IDENTITY_MISMATCH";
                }

                using var proc = Process.GetProcessById(pid);
                if (!MatchesExpectedIdentity(proc, target))
                {
                    _logger.Warn($"Process identity mismatch for PID {pid}; CPU Sets not set.");
                    return "ERR_IDENTITY_MISMATCH";
                }

                bool ok = NativeMethods.SetProcessDefaultCpuSets(hProc, selectedCpuSetIds, (uint)selectedCpuSetIds.Length);
                if (!ok)
                {
                    int error = Marshal.GetLastWin32Error();
                    return $"ERR_CPUSETS_{error}";
                }

                return $"OK_CPUSETS_{selectedCpuSetIds.Length}";
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        private bool ClearCpuSets(int pid, ProcessMutationTarget target)
        {
            // QUERY rights are required: the start-time gate uses GetProcessTimes.
            IntPtr hProc = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_SET_LIMITED_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                false, pid);
            if (hProc == IntPtr.Zero) return false;

            try
            {
                // Never clear CPU Sets of a recycled PID.
                if (!TryGetProcessStartTimeUtc(hProc, out DateTime startUtc)
                    || startUtc != target.StartTimeUtc)
                {
                    return false;
                }

                using var proc = Process.GetProcessById(pid);
                if (!MatchesExpectedIdentity(proc, target))
                {
                    return false;
                }

                return NativeMethods.SetProcessDefaultCpuSets(hProc, Array.Empty<uint>(), 0);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Could not clear CPU Sets for PID {pid}: {ex.Message}");
                return false;
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        /// <summary>
        /// Validates a requested core mask against the known processor map.
        /// Returns null when the mask may be attempted, otherwise a stable error code.
        /// </summary>
        public static string? ValidateCoreMask(long mask, Dictionary<int, uint> cpuSetMap)
        {
            if (mask == 0) return "ERR_EMPTY_MASK";
            if ((mask & ~KnownProcessorMask(cpuSetMap)) != 0) return "ERR_MASK_OUT_OF_RANGE";
            return null;
        }

        /// <summary>
        /// Bitmask of processors PCO is allowed to address (group 0, max 64).
        /// Derived from the CPU Set map when available, otherwise from ProcessorCount.
        /// </summary>
        public static long KnownProcessorMask(Dictionary<int, uint> cpuSetMap)
        {
            if (cpuSetMap.Count > 0)
            {
                long known = 0;
                foreach (int index in cpuSetMap.Keys)
                {
                    if (index >= 0 && index < 64) known |= (1L << index);
                }

                return known;
            }

            int count = Math.Min(Math.Max(Environment.ProcessorCount, 1), 64);
            return count >= 64 ? -1L : ((1L << count) - 1);
        }

        public (bool Success, long Mask, OptimizationMode Mode, string Source, string Message) GetCurrentCoreSelection(int pid, Dictionary<int, uint> cpuSetMap)
        {
            if (pid <= 0)
            {
                return (false, 0, OptimizationMode.CpuSets, "None", "Invalid PID.");
            }

            if (TryReadProcessDefaultCpuSets(pid, cpuSetMap, out long cpuSetMask, out string cpuSetMessage) && cpuSetMask != 0)
            {
                return (true, cpuSetMask, OptimizationMode.CpuSets, "CpuSets", cpuSetMessage);
            }

            try
            {
                using var proc = Process.GetProcessById(pid);
                long affinityMask = proc.ProcessorAffinity.ToInt64();
                if (affinityMask != 0)
                {
                    return (true, affinityMask, OptimizationMode.Affinity, "Affinity", "Read current processor affinity.");
                }

                return (false, 0, OptimizationMode.CpuSets, "Affinity", "Processor affinity mask is empty.");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read current CPU selection for PID {pid}: {ex.Message}");
                return (false, 0, OptimizationMode.CpuSets, "None", ex.Message);
            }
        }

        private bool TryReadProcessDefaultCpuSets(int pid, Dictionary<int, uint> cpuSetMap, out long mask, out string message)
        {
            mask = 0;
            message = "No process CPU Sets assigned.";

            if (cpuSetMap.Count == 0)
            {
                message = "CPU Set map is empty.";
                return false;
            }

            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                message = $"OpenProcess for CPU Sets query failed: {error}.";
                return false;
            }

            try
            {
                bool initialRead = NativeMethods.GetProcessDefaultCpuSets(hProc, null, 0, out uint requiredCount);
                if (!initialRead && requiredCount == 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    message = $"GetProcessDefaultCpuSets failed: {error}.";
                    return false;
                }

                if (requiredCount == 0)
                {
                    return false;
                }

                var cpuSetIds = new uint[(int)requiredCount];
                if (!NativeMethods.GetProcessDefaultCpuSets(hProc, cpuSetIds, requiredCount, out requiredCount))
                {
                    int error = Marshal.GetLastWin32Error();
                    message = $"GetProcessDefaultCpuSets buffer read failed: {error}.";
                    return false;
                }

                var logicalByCpuSetId = cpuSetMap
                    .GroupBy(x => x.Value)
                    .ToDictionary(g => g.Key, g => g.First().Key);

                foreach (uint cpuSetId in cpuSetIds.Distinct())
                {
                    if (logicalByCpuSetId.TryGetValue(cpuSetId, out int logicalIndex) && logicalIndex >= 0 && logicalIndex < 64)
                    {
                        mask |= 1L << logicalIndex;
                    }
                }

                message = mask == 0
                    ? "Process CPU Sets are assigned, but they do not map to visible logical processors."
                    : "Read current process CPU Sets.";

                return mask != 0;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read CPU Sets for PID {pid}: {ex.Message}");
                message = ex.Message;
                return false;
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        public static uint[] ConvertMaskToCpuSetIds(long mask, Dictionary<int, uint> cpuSetMap)
        {
            var ids = new List<uint>();
            int maxIndex = Math.Min(cpuSetMap.Keys.DefaultIfEmpty(-1).Max(), 63);

            for (int i = 0; i <= maxIndex; i++)
            {
                if ((mask & (1L << i)) != 0 && cpuSetMap.TryGetValue(i, out uint cpuSetId))
                {
                    ids.Add(cpuSetId);
                }
            }

            return ids.Distinct().ToArray();
        }

        public static bool RequiresAdminForMessage(string message)
        {
            // Only real errors imply elevation problems. Past code flagged
            // successful "OK_CPUSETS_5" because it contains "_5".
            if (string.IsNullOrEmpty(message)) return false;
            if (!message.StartsWith("ERR_", StringComparison.OrdinalIgnoreCase)) return false;
            return message.Contains("_5", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ACCESS", StringComparison.OrdinalIgnoreCase)
                || message.Contains("DENIED", StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyIdentityBeforeMutation(int pid, ProcessMutationTarget target)
        {
            if (pid <= 0 || pid != target.ProcessId) return false;
            if (!target.IsValid) return false;

            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return false;

            try
            {
                if (!TryGetProcessStartTimeUtc(hProc, out DateTime startUtc)) return false;
                if (startUtc != target.StartTimeUtc)
                {
                    _logger.Warn($"Process identity start time mismatch or process recycled for PID {pid}; mutation refused.");
                    return false;
                }

                return true;
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        public static bool MatchesExpectedIdentity(Process process, ProcessMutationTarget target)
        {
            if (!target.IsValid) return false;
            try
            {
                if (process.Id != target.ProcessId) return false;
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

                if (startUtc != target.StartTimeUtc) return false;

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

                if (!string.Equals(
                    ProfileService.NormalizeProcessName(target.ProcessName),
                    ProfileService.NormalizeProcessName(liveName),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(target.ExecutablePath))
                {
                    string? expectedNorm = ProfileService.NormalizeExecutablePath(target.ExecutablePath);
                    string? liveNorm = ProfileService.NormalizeExecutablePath(livePath);
                    if (expectedNorm == null || liveNorm == null) return false;
                    if (!string.Equals(expectedNorm, liveNorm, StringComparison.OrdinalIgnoreCase)) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetProcessStartTimeUtc(IntPtr processHandle, out DateTime startTimeUtc)
        {
            startTimeUtc = default;
            if (!NativeMethods.GetProcessTimes(processHandle, out var creationTime, out _, out _, out _))
            {
                return false;
            }

            try
            {
                long fileTime = ((long)(uint)creationTime.dwHighDateTime << 32) | (uint)creationTime.dwLowDateTime;
                startTimeUtc = DateTime.FromFileTimeUtc(fileTime);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
