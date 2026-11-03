using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Session-only restore of pre-optimization process settings (affinity, CPU Sets,
    /// priority). Entries live in memory, are bounded, and capture the baseline only
    /// once — never overwritten by an already-optimized state. Restore re-validates
    /// the live identity per instance and reports partial success honestly.
    /// Restore order: affinity first (it clears CPU Sets), then CPU Sets
    /// (apply or clear back to no-explicit-sets), then priority.
    /// </summary>
    public sealed class SessionRestoreService
    {
        public const int MaxEntries = 100;

        private readonly List<SessionRestoreEntry> _entries = new();
        private readonly object _lock = new();
        private readonly ILogger _logger = LoggerService.Instance;

        public void Capture(ProcessInstanceKey key, string processName, string? executablePath, long? affinityMask, ProcessPriorityClass? priority)
        {
            CaptureFull(key, processName, executablePath, affinityMask, cpuSetsObserved: false, cpuSetMask: null, priority);
        }

        private void CaptureFull(
            ProcessInstanceKey key, string processName, string? executablePath,
            long? affinityMask, bool cpuSetsObserved, long? cpuSetMask, ProcessPriorityClass? priority)
        {
            if (key.ProcessId <= 0 || key.StartTimeUtc == default) return;
            if (affinityMask is null or 0 && !cpuSetsObserved && priority is null) return;
            lock (_lock)
            {
                if (_entries.Any(e => e.Key.Equals(key))) return; // First baseline wins.
                _entries.Add(new SessionRestoreEntry
                {
                    Key = key,
                    ProcessName = processName,
                    ExecutablePath = executablePath,
                    AffinityMask = affinityMask,
                    CpuSetsObserved = cpuSetsObserved,
                    CpuSetMask = cpuSetMask,
                    Priority = priority,
                    CapturedAtUtc = DateTime.UtcNow
                });

                while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }
        }

        /// <summary>
        /// Best-effort baseline capture from live OS state. Stores only when no
        /// baseline exists for the key and at least one value was readable.
        /// </summary>
        public bool TryCaptureBaseline(ProcessInstanceKey key, string processName, string? executablePath, ProcessService processService, Dictionary<int, uint> cpuSetMap)
        {
            if (key.ProcessId <= 0 || key.StartTimeUtc == default) return false;
            lock (_lock)
            {
                if (_entries.Any(e => e.Key.Equals(key))) return false;
            }

            long? affinityMask = null;
            bool cpuSetsObserved = false;
            long? cpuSetMask = null;
            ProcessPriorityClass? priority = null;

            try
            {
                var selection = processService.GetCurrentCoreSelection(key.ProcessId, cpuSetMap);
                if (selection.Success && selection.Mask != 0)
                {
                    if (selection.Source == "CpuSets")
                    {
                        cpuSetsObserved = true;
                        cpuSetMask = selection.Mask;
                    }
                    else
                    {
                        affinityMask = selection.Mask;
                        cpuSetsObserved = true; // Readable selection with no explicit sets.
                    }
                }
            }
            catch { }

            try { priority = processService.TryGetPriority(key.ProcessId); } catch { }

            if (affinityMask is null or 0 && !cpuSetsObserved && priority is null) return false;
            CaptureFull(key, processName, executablePath, affinityMask, cpuSetsObserved, cpuSetMask, priority);
            return true;
        }

        public bool HasBaseline(ProcessInstanceKey key)
        {
            lock (_lock) return _entries.Any(e => e.Key.Equals(key));
        }

        public IReadOnlyList<SessionRestoreEntry> ForProcess(string processName)
        {
            string normalized = ProfileService.NormalizeProcessName(processName);
            lock (_lock)
            {
                return _entries
                    .Where(e => string.Equals(ProfileService.NormalizeProcessName(e.ProcessName), normalized, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.CapturedAtUtc)
                    .ToList();
            }
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        /// <summary>
        /// Restores captured settings for live instances of a process. Each instance
        /// is identity-checked; recycled PIDs are skipped, never restored into.
        /// </summary>
        public SessionRestoreResult RestoreForProcess(string processName, ProcessService processService, Dictionary<int, uint> cpuSetMap, IReadOnlyCollection<ProcessInstanceKey>? onlyKeys = null)
        {
            var result = new SessionRestoreResult { ProcessName = processName };
            var entries = ForProcess(processName);
            if (entries.Count == 0)
            {
                result.Message = "No captured session state for this process.";
                return result;
            }

            Process[] live = Array.Empty<Process>();
            try
            {
                live = Process.GetProcessesByName(ProfileService.NormalizeProcessName(processName));
                foreach (var proc in live)
                {
                    SessionRestoreEntry? entry;
                    try
                    {
                        if (proc.HasExited) continue;
                        var key = new ProcessInstanceKey(proc.Id, proc.StartTime.ToUniversalTime());
                        if (onlyKeys != null && !onlyKeys.Contains(key))
                        {
                            result.Skipped++;
                            continue;
                        }

                        entry = entries.FirstOrDefault(e => e.Key.Equals(key));
                    }
                    catch
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (entry == null)
                    {
                        // No verified baseline for this instance (e.g. started after capture).
                        result.Skipped++;
                        continue;
                    }

                    if (!ProcessMutationTarget.TryCreate(
                            entry.Key.ProcessId, entry.Key.StartTimeUtc,
                            entry.ProcessName, entry.ExecutablePath,
                            out ProcessMutationTarget target))
                    {
                        result.Skipped++;
                        continue;
                    }

                    bool affinityOk = true, cpuSetsOk = true, priorityOk = true;
                    bool wantsAffinity = entry.AffinityMask.HasValue && entry.AffinityMask.Value != 0;
                    bool wantsCpuSets = entry.CpuSetsObserved;
                    bool wantsPriority = entry.Priority.HasValue;

                    // 1. Affinity first: the OS call clears CPU Sets as a side effect.
                    if (wantsAffinity)
                    {
                        try
                        {
                            affinityOk = processService.SetProcessAffinity(proc.Id, entry.AffinityMask!.Value, target);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Restore affinity failed for PID {proc.Id}: {ex.Message}");
                            affinityOk = false;
                        }
                    }

                    // 2. CPU Sets: re-apply captured sets, or clear back to
                    // no-explicit-sets when the baseline had none. Unknown stays untouched.
                    if (wantsCpuSets)
                    {
                        try
                        {
                            if (entry.CpuSetMask.HasValue && entry.CpuSetMask.Value != 0)
                            {
                                string setsResult = processService.ApplyCoreOptimization(
                                    proc.Id, entry.CpuSetMask.Value, OptimizationMode.CpuSets, cpuSetMap, target);
                                cpuSetsOk = setsResult.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                            }
                            else
                            {
                                cpuSetsOk = processService.ClearProcessCpuSets(proc.Id, target);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Restore CPU Sets failed for PID {proc.Id}: {ex.Message}");
                            cpuSetsOk = false;
                        }
                    }

                    // 3. Priority is independent of core placement.
                    if (wantsPriority)
                    {
                        try
                        {
                            priorityOk = processService.SetPriority(proc.Id, entry.Priority!.Value, target);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Restore priority failed for PID {proc.Id}: {ex.Message}");
                            priorityOk = false;
                        }
                    }

                    bool allOk = (!wantsAffinity || affinityOk)
                        && (!wantsCpuSets || cpuSetsOk)
                        && (!wantsPriority || priorityOk);
                    bool anyOk = (wantsAffinity && affinityOk)
                        || (wantsCpuSets && cpuSetsOk)
                        || (wantsPriority && priorityOk);

                    if (allOk) result.Restored++;
                    else if (anyOk) result.PartiallyRestored++;
                    else result.Failed++;

                    if (wantsAffinity && affinityOk) result.AffinityRestored++;
                    if (wantsCpuSets && cpuSetsOk) result.CpuSetsRestored++;
                    if (wantsPriority && priorityOk) result.PriorityRestored++;
                }

                result.Message = $"Restored {result.Restored} instance(s), {result.PartiallyRestored} partial, {result.Failed} failed, {result.Skipped} skipped (no verified baseline).";
                return result;
            }
            finally
            {
                foreach (var proc in live) proc.Dispose();
            }
        }
    }

    public sealed class SessionRestoreEntry
    {
        public ProcessInstanceKey Key { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public long? AffinityMask { get; set; }
        /// <summary>True when the baseline read could determine the sets state (even "none").</summary>
        public bool CpuSetsObserved { get; set; }
        public long? CpuSetMask { get; set; }
        public ProcessPriorityClass? Priority { get; set; }
        public DateTime CapturedAtUtc { get; set; }
    }

    public sealed class SessionRestoreResult
    {
        public string ProcessName { get; set; } = string.Empty;
        public int Restored { get; set; }
        public int PartiallyRestored { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int AffinityRestored { get; set; }
        public int CpuSetsRestored { get; set; }
        public int PriorityRestored { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
