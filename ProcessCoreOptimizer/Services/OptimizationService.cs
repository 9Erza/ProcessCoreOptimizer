using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Central optimization engine for profiles. Cached observations never authorize
    /// a mutation: every target is re-validated (PID + start time + name + path)
    /// immediately before touching the OS. Only fully-applied profiles are cached,
    /// so partial failures are retried on the next watcher tick.
    /// An optional <see cref="SessionRestoreService"/> receives the pre-mutation
    /// baseline on first touch; temporarily suspended instances are skipped until
    /// their suspension expires (e.g. after a manual session restore).
    /// </summary>
    public sealed class OptimizationService
    {
        private readonly ProcessService _processService;
        private readonly Func<Dictionary<int, uint>> _cpuSetMapProvider;
        private readonly SessionRestoreService? _restoreService;
        private readonly ILogger _logger;
        private readonly Dictionary<ProcessInstanceKey, string> _appliedProfileSignatures = new();
        private readonly Dictionary<ProcessInstanceKey, (DateTimeOffset ExpiresAt, string Reason)> _suspensions = new();
        private readonly object _cacheLock = new();

        public OptimizationService(ProcessService processService, Func<Dictionary<int, uint>> cpuSetMapProvider, SessionRestoreService? restoreService = null)
        {
            _processService = processService;
            _cpuSetMapProvider = cpuSetMapProvider;
            _restoreService = restoreService;
            _logger = LoggerService.Instance;
        }

        /// <summary>
        /// Skips automatic application for a live instance until the duration
        /// elapses. Used after manual session restore so the watcher does not
        /// immediately re-apply the profile against the user's intent.
        /// Suspensions are in-memory only and expire on their own.
        /// </summary>
        public void SuspendInstance(ProcessInstanceKey key, TimeSpan duration, string reason)
        {
            if (key.ProcessId <= 0 || key.StartTimeUtc == default) return;
            lock (_cacheLock)
            {
                _suspensions[key] = (DateTimeOffset.UtcNow + duration, reason ?? string.Empty);
            }
        }

        public void SuspendLiveInstances(string processName, TimeSpan duration, string reason)
        {
            Process[] live = Array.Empty<Process>();
            try
            {
                live = Process.GetProcessesByName(ProfileService.NormalizeProcessName(processName));
                lock (_cacheLock)
                {
                    foreach (var proc in live)
                    {
                        try
                        {
                            if (proc.HasExited) continue;
                            var key = ProcessScannerService.CreateInstanceKey(proc);
                            if (key.StartTimeUtc != default) _suspensions[key] = (DateTimeOffset.UtcNow + duration, reason ?? string.Empty);
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                foreach (var proc in live) proc.Dispose();
            }
        }

        public int SuspendedCount
        {
            get
            {
                lock (_cacheLock)
                {
                    PruneSuspensionsLocked();
                    return _suspensions.Count;
                }
            }
        }

        private bool IsSuspended(ProcessInstanceKey key, out string reason)
        {
            reason = string.Empty;
            lock (_cacheLock)
            {
                PruneSuspensionsLocked();
                if (_suspensions.TryGetValue(key, out var entry))
                {
                    reason = entry.Reason;
                    return true;
                }

                return false;
            }
        }

        private void PruneSuspensionsLocked()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in _suspensions.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToList())
            {
                _suspensions.Remove(expired);
            }
        }

        public OptimizationBatchResult ApplyProfileToRunningProcesses(ProcessProfile profile, bool allowRealtimePriority, bool force)
        {
            return ApplyProfileToKeys(profile, processName: null, keys: null, allowRealtimePriority, force);
        }

        /// <summary>
        /// P0 scoped apply: touches only the given instance keys. An empty key set
        /// is a no-op (never widens to the whole group). Identity is re-verified
        /// per key inside <see cref="ApplyProfileToPid"/>; stale keys yield
        /// SKIPPED_IDENTITY_MISMATCH, never a PID-only mutation.
        /// </summary>
        public OptimizationBatchResult ApplyProfileToInstances(
            ProcessProfile profile,
            string processName,
            IReadOnlyCollection<ProcessInstanceKey> keys,
            bool allowRealtimePriority,
            bool force)
        {
            var batch = new OptimizationBatchResult();
            if (!ProcessRowBuilder.IsScopedSelectionValid(keys))
                return batch;

            string normalized = ProfileService.NormalizeProcessName(processName);
            foreach (var key in keys)
            {
                var result = ApplyProfileToPid(key, normalized, profile, allowRealtimePriority, force);
                batch.Results.Add(result);
            }

            batch.Total = batch.Results.Count;
            batch.Successful = batch.Results.Count(r => r.Success);
            batch.Failed = batch.Total - batch.Successful;
            return batch;
        }

        private OptimizationBatchResult ApplyProfileToKeys(
            ProcessProfile profile,
            string? processName,
            IReadOnlyCollection<ProcessInstanceKey>? keys,
            bool allowRealtimePriority,
            bool force)
        {
            var batch = new OptimizationBatchResult();
            Process[] processes = Array.Empty<Process>();

            try
            {
                string name = ProfileService.NormalizeProcessName(processName ?? profile.ProcessName);
                processes = Process.GetProcessesByName(name);
                foreach (var process in processes)
                {
                    if (keys != null && !keys.Contains(ProcessScannerService.CreateInstanceKey(process)))
                        continue;

                    string? livePath = ProcessIdentityValidator.TryGetProcessPath(process);
                    if (!ProfileService.MatchesIdentity(profile, process.ProcessName, livePath)) continue;

                    var result = ApplyProfileToProcess(process, profile, allowRealtimePriority, force);
                    batch.Results.Add(result);
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            batch.Total = batch.Results.Count;
            batch.Successful = batch.Results.Count(r => r.Success);
            batch.Failed = batch.Total - batch.Successful;
            return batch;
        }

        public OptimizationBatchResult ApplyProfilesForSnapshots(IEnumerable<ProcessProfile> profiles, IEnumerable<ProcessGroupSnapshot> snapshots, bool allowRealtimePriority, bool force)
        {
            // Multiple profiles may share one process name (e.g. path-bound variants).
            // Path-bound profiles win over name-only profiles; newest wins on ties.
            var byName = profiles
                .Where(p => p.IsEnabled)
                .GroupBy(p => ProfileService.NormalizeProcessName(p.ProcessName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => !string.IsNullOrWhiteSpace(p.ExecutablePath)).ThenByDescending(p => p.UpdatedAt).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var batch = new OptimizationBatchResult();

            foreach (var snapshot in snapshots)
            {
                if (!byName.TryGetValue(ProfileService.NormalizeProcessName(snapshot.Name), out var matchingProfiles)) continue;

                foreach (var instance in snapshot.Instances)
                {
                    foreach (var profile in matchingProfiles)
                    {
                        var result = ApplyProfileToPid(instance, snapshot.Name, profile, allowRealtimePriority, force);
                        if (result.Message == "SKIPPED_IDENTITY_MISMATCH") continue;
                        batch.Results.Add(result);
                        break;
                    }
                }
            }

            batch.Total = batch.Results.Count;
            batch.Successful = batch.Results.Count(r => r.Success);
            batch.Failed = batch.Total - batch.Successful;
            return batch;
        }

        private OptimizationResult ApplyProfileToProcess(Process process, ProcessProfile profile, bool allowRealtimePriority, bool force)
        {
            return ApplyProfileToPid(ProcessScannerService.CreateInstanceKey(process), process.ProcessName, profile, allowRealtimePriority, force);
        }

        internal OptimizationResult ApplyProfileToPid(ProcessInstanceKey key, string processName, ProcessProfile profile, bool allowRealtimePriority, bool force)
        {
            if (ProcessService.IsProtectedProcessName(processName))
            {
                return new OptimizationResult
                {
                    Success = false,
                    ProcessId = key.ProcessId,
                    ProcessName = processName,
                    Mode = NormalizeOptimizationMode(profile.OptimizationMode),
                    Priority = PriorityService.Normalize(profile.Priority, allowRealtimePriority),
                    Message = "SKIPPED_PROTECTED",
                    Status = OperationStatus.SkippedProtected,
                    Retryable = false,
                    RequestedCore = profile.ApplyCoreOptimization,
                    RequestedPriority = profile.ApplyPriority,
                    RequestedEcoQos = profile.EcoQos.HasValue
                };
            }

            if (IsSuspended(key, out string suspendReason))
            {
                return new OptimizationResult
                {
                    Success = false,
                    ProcessId = key.ProcessId,
                    ProcessName = processName,
                    Mode = NormalizeOptimizationMode(profile.OptimizationMode),
                    Priority = PriorityService.Normalize(profile.Priority, allowRealtimePriority),
                    Message = string.IsNullOrWhiteSpace(suspendReason) ? "SKIPPED_SUSPENDED" : $"SKIPPED_SUSPENDED: {suspendReason}",
                    Status = OperationStatus.SkippedSuspended,
                    Retryable = false,
                    RequestedCore = profile.ApplyCoreOptimization,
                    RequestedPriority = profile.ApplyPriority,
                    RequestedEcoQos = profile.EcoQos.HasValue
                };
            }

            if (!ProcessIdentityValidator.MatchesLiveProcess(key, processName, profile))
            {
                return new OptimizationResult
                {
                    Success = false,
                    ProcessId = key.ProcessId,
                    ProcessName = processName,
                    Mode = NormalizeOptimizationMode(profile.OptimizationMode),
                    Priority = PriorityService.Normalize(profile.Priority, allowRealtimePriority),
                    Message = "SKIPPED_IDENTITY_MISMATCH",
                    Status = OperationStatus.SkippedIdentityMismatch,
                    Retryable = false,
                    RequestedCore = profile.ApplyCoreOptimization,
                    RequestedPriority = profile.ApplyPriority,
                    RequestedEcoQos = profile.EcoQos.HasValue
                };
            }

            string priority = PriorityService.Normalize(profile.Priority, allowRealtimePriority);
            var mode = NormalizeOptimizationMode(profile.OptimizationMode);
            string signature = BuildProfileSignature(profile, priority, mode, allowRealtimePriority);

            lock (_cacheLock)
            {
                if (!force && _appliedProfileSignatures.TryGetValue(key, out string? existingSignature) && existingSignature == signature)
                {
                    return new OptimizationResult
                    {
                        Success = false,
                        ProcessId = key.ProcessId,
                        ProcessName = processName,
                        Mode = mode,
                        Priority = priority,
                        Message = "SKIPPED_ALREADY_APPLIED",
                        Status = OperationStatus.SkippedAlreadyApplied,
                        Retryable = false,
                        RequestedCore = profile.ApplyCoreOptimization,
                        RequestedPriority = profile.ApplyPriority,
                        RequestedEcoQos = profile.EcoQos.HasValue
                    };
                }
            }

            bool coreApplied = false;
            bool priorityApplied = false;
            bool ecoApplied = false;
            string coreMessage = "SKIPPED_CORE";
            string priorityMessage = "SKIPPED_PRIORITY";
            string ecoMessage = "SKIPPED_NO_ECOQOS";
            bool requiresAdmin = false;
            int? win32Error = null;

            if (!ProcessMutationTarget.TryCreate(
                    key.ProcessId, key.StartTimeUtc,
                    ProfileService.NormalizeProcessName(processName), profile.ExecutablePath,
                    out ProcessMutationTarget target))
            {
                return new OptimizationResult
                {
                    Success = false,
                    ProcessId = key.ProcessId,
                    ProcessName = processName,
                    Mode = mode,
                    Priority = priority,
                    Message = "SKIPPED_IDENTITY_MISMATCH",
                    Status = OperationStatus.SkippedIdentityMismatch,
                    Retryable = false,
                    RequestedCore = profile.ApplyCoreOptimization,
                    RequestedPriority = profile.ApplyPriority
                };
            }

            if (profile.ApplyCoreOptimization || profile.ApplyPriority || profile.EcoQos.HasValue)
            {
                // Baseline before the first real mutation; no-ops when one exists.
                // EcoQoS-only profiles capture too: without a verified baseline a
                // later restore must leave EcoQoS untouched (Unknown != Disabled).
                try
                {
                    _restoreService?.TryCaptureBaseline(
                        key, ProfileService.NormalizeProcessName(processName), profile.ExecutablePath,
                        _processService, _cpuSetMapProvider());
                }
                catch { }
            }

            if (profile.ApplyCoreOptimization)
            {
                coreMessage = _processService.ApplyCoreOptimization(
                    key.ProcessId, profile.AffinityMask, mode, _cpuSetMapProvider(), target);
                coreApplied = coreMessage.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                requiresAdmin = ProcessService.RequiresAdminForMessage(coreMessage);
                win32Error = ExtractWin32Error(coreMessage);
            }

            if (profile.ApplyPriority && PriorityService.TryParse(priority, allowRealtimePriority, out ProcessPriorityClass parsedPriority))
            {
                priorityApplied = _processService.SetPriority(key.ProcessId, parsedPriority, target);
                priorityMessage = priorityApplied ? "OK_PRIORITY" : "ERR_PRIORITY";
                requiresAdmin |= !priorityApplied && IsAccessDeniedPriority(key.ProcessId);
            }

            if (profile.EcoQos.HasValue)
            {
                var eco = _processService.SetEcoQos(key.ProcessId, profile.EcoQos.Value, target);
                ecoApplied = eco.Success;
                ecoMessage = eco.Message;
                requiresAdmin |= !eco.Success && eco.RequiresAdmin;
                if (!eco.Success && eco.Message.StartsWith("ERR_OPEN_PROCESS_", StringComparison.OrdinalIgnoreCase))
                    win32Error ??= ExtractWin32Error(eco.Message);
            }

            bool success = coreApplied || priorityApplied || ecoApplied;
            bool allRequested = (!profile.ApplyCoreOptimization || coreApplied)
                && (!profile.ApplyPriority || priorityApplied)
                && (!profile.EcoQos.HasValue || ecoApplied);

            OperationStatus status = ResolveStatus(
                success, allRequested, requiresAdmin,
                profile.ApplyCoreOptimization, profile.ApplyPriority,
                coreMessage, priorityMessage,
                profile.EcoQos.HasValue, ecoMessage);

            lock (_cacheLock)
            {
                if (status == OperationStatus.FullyApplied)
                {
                    _appliedProfileSignatures[key] = signature;
                }
                else
                {
                    // Partial success must NOT be cached: the failed half is retried next tick.
                    _appliedProfileSignatures.Remove(key);
                }
            }

            if (!success)
            {
                _logger.Debug($"Optimization failed for PID {key.ProcessId}: core={coreMessage}, priority={priorityMessage}");
            }

            return new OptimizationResult
            {
                Success = success,
                ProcessId = key.ProcessId,
                ProcessName = processName,
                Mode = mode,
                Priority = priority,
                Message = $"{coreMessage}; {priorityMessage}; {ecoMessage}",
                CoreApplied = coreApplied,
                PriorityApplied = priorityApplied,
                EcoQosApplied = ecoApplied,
                RequiresAdmin = requiresAdmin,
                Win32Error = win32Error,
                Status = status,
                Retryable = status.IsRetryable(),
                RequestedCore = profile.ApplyCoreOptimization,
                RequestedPriority = profile.ApplyPriority,
                RequestedEcoQos = profile.EcoQos.HasValue
            };
        }

        /// <summary>
        /// Testable status mapping. Pure failure + elevation evidence becomes
        /// SkippedNoPermission (retry without elevation is pointless); invalid
        /// requests become Unsupported; anything attempted-but-incomplete stays
        /// Failed/PartiallyApplied so the watcher retries.
        /// </summary>
        public static OperationStatus ResolveStatus(
            bool success, bool allRequested, bool requiresAdmin,
            bool requestedCore, bool requestedPriority,
            string coreMessage, string priorityMessage,
            bool requestedEcoQos = false, string ecoMessage = "SKIPPED_NO_ECOQOS")
        {
            if (success && allRequested) return OperationStatus.FullyApplied;
            if (success) return OperationStatus.PartiallyApplied;
            if (requiresAdmin) return OperationStatus.SkippedNoPermission;
            if (!requestedCore && !requestedPriority && !requestedEcoQos) return OperationStatus.Unsupported;
            if (requestedEcoQos && !requestedCore && !requestedPriority && IsEcoQosUnsupported(ecoMessage))
                return OperationStatus.Unsupported;
            if (IsValidationError(coreMessage) && (!requestedPriority || priorityMessage == "SKIPPED_PRIORITY")
                && (!requestedEcoQos || IsEcoQosUnsupported(ecoMessage)))
                return OperationStatus.Unsupported;
            return OperationStatus.Failed;
        }

        private static bool IsEcoQosUnsupported(string ecoMessage) =>
            ecoMessage.StartsWith("ERR_ECOQOS_UNSUPPORTED", StringComparison.OrdinalIgnoreCase)
            || ecoMessage.StartsWith("SKIPPED_NO_ECOQOS", StringComparison.OrdinalIgnoreCase);

        private static bool IsValidationError(string coreMessage) =>
            coreMessage.StartsWith("ERR_EMPTY_MASK", StringComparison.OrdinalIgnoreCase)
            || coreMessage.StartsWith("ERR_MASK_OUT_OF_RANGE", StringComparison.OrdinalIgnoreCase)
            || coreMessage.StartsWith("ERR_UNKNOWN_MODE", StringComparison.OrdinalIgnoreCase)
            || coreMessage.StartsWith("ERR_CPUSET_MAP_EMPTY", StringComparison.OrdinalIgnoreCase)
            || coreMessage.StartsWith("ERR_NO_VALID_CPUSETS", StringComparison.OrdinalIgnoreCase);

        private bool IsAccessDeniedPriority(int pid)
        {
            // Best-effort elevation hint: only when the OS reports access-denied.
            // Never invents RequiresAdmin for generic failures.
            try
            {
                IntPtr hProc = Helpers.NativeMethods.OpenProcess(
                    Helpers.NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProc == IntPtr.Zero)
                {
                    int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    return error == 5;
                }

                Helpers.NativeMethods.CloseHandle(hProc);
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void ClearProfileCacheForProcess(string processName)
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                string normalized = ProfileService.NormalizeProcessName(processName);
                processes = Process.GetProcessesByName(normalized);
                lock (_cacheLock)
                {
                    foreach (var process in processes)
                    {
                        _appliedProfileSignatures.Remove(ProcessScannerService.CreateInstanceKey(process));
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        /// <summary>
        /// Drops the cached FullyApplied signature for one instance only, so the
        /// next tick re-evaluates just that PID (P1-1 scoped invalidation).
        /// </summary>
        public void ClearProfileCacheForKey(ProcessInstanceKey key)
        {
            lock (_cacheLock) _appliedProfileSignatures.Remove(key);
        }

        public void ClearAllCache()
        {
            lock (_cacheLock) _appliedProfileSignatures.Clear();
        }

        public void CleanupStaleCache(HashSet<ProcessInstanceKey> activeInstances)
        {
            lock (_cacheLock)
            {
                foreach (var staleKey in _appliedProfileSignatures.Keys.Where(k => !activeInstances.Contains(k)).ToList())
                {
                    _appliedProfileSignatures.Remove(staleKey);
                }

                foreach (var staleKey in _suspensions.Keys.Where(k => !activeInstances.Contains(k)).ToList())
                {
                    _suspensions.Remove(staleKey);
                }

                PruneSuspensionsLocked();
            }
        }

        internal static string BuildProfileSignature(ProcessProfile profile, string priority, OptimizationMode mode, bool allowRealtimePriority)
        {
            return $"{ProfileService.NormalizeProcessName(profile.ProcessName)}|{profile.AffinityMask:X}|{priority}|{mode}|{profile.IsEnabled}|{profile.ApplyPriority}|{profile.ApplyCoreOptimization}|eco:{(profile.EcoQos.HasValue ? profile.EcoQos.Value ? "1" : "0" : "-")}|RT:{allowRealtimePriority}|path:{ProfileService.NormalizeExecutablePath(profile.ExecutablePath) ?? "-"}";
        }

        private static OptimizationMode NormalizeOptimizationMode(OptimizationMode mode)
        {
#pragma warning disable CS0618
            return mode == OptimizationMode.Exclusive ? OptimizationMode.Affinity : mode;
#pragma warning restore CS0618
        }

        private static int? ExtractWin32Error(string message)
        {
            const string openPrefix = "ERR_OPEN_PROCESS_";
            const string cpuSetPrefix = "ERR_CPUSETS_";

            if (message.StartsWith(openPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(message[openPrefix.Length..], out int openError))
            {
                return openError;
            }

            if (message.StartsWith(cpuSetPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(message[cpuSetPrefix.Length..], out int cpuSetError))
            {
                return cpuSetError;
            }

            return null;
        }
    }
}
