using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Session-only restore of pre-optimization process settings. Entries live in
    /// memory, are bounded, and restore only after re-validating the live identity
    /// (PID + start time + name + path). Never restores into a recycled PID.
    /// </summary>
    public sealed class SessionRestoreService
    {
        public const int MaxEntries = 100;

        private readonly List<SessionRestoreEntry> _entries = new();
        private readonly object _lock = new();
        private readonly ILogger _logger = LoggerService.Instance;

        public void Capture(ProcessInstanceKey key, string processName, string? executablePath, long? affinityMask, ProcessPriorityClass? priority)
        {
            if (key.ProcessId <= 0 || key.StartTimeUtc == default) return;
            lock (_lock)
            {
                _entries.RemoveAll(e => e.Key.Equals(key));
                _entries.Add(new SessionRestoreEntry
                {
                    Key = key,
                    ProcessName = processName,
                    ExecutablePath = executablePath,
                    AffinityMask = affinityMask,
                    Priority = priority,
                    CapturedAtUtc = DateTime.UtcNow
                });

                while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }
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
        public SessionRestoreResult RestoreForProcess(string processName, ProcessService processService, Dictionary<int, uint> cpuSetMap)
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

                    string? livePath = null;
                    bool restoredAny = false;

                    if (entry.AffinityMask.HasValue && entry.AffinityMask.Value != 0)
                    {
                        try
                        {
                            if (processService.SetProcessAffinity(proc.Id, entry.AffinityMask.Value, entry.Key, entry.ProcessName, entry.ExecutablePath))
                            {
                                restoredAny = true;
                            }
                            else
                            {
                                result.Failed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Restore affinity failed for PID {proc.Id}: {ex.Message}");
                            result.Failed++;
                        }
                    }

                    if (entry.Priority.HasValue)
                    {
                        try
                        {
                            if (processService.SetPriority(proc.Id, entry.Priority.Value, entry.Key, entry.ProcessName, entry.ExecutablePath))
                            {
                                restoredAny = true;
                            }
                            else
                            {
                                result.Failed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Restore priority failed for PID {proc.Id}: {ex.Message}");
                            result.Failed++;
                        }
                    }

                    if (restoredAny) result.Restored++;
                }

                result.Message = $"Restored {result.Restored} instance(s), {result.Failed} failed, {result.Skipped} skipped (no verified baseline).";
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
        public ProcessPriorityClass? Priority { get; set; }
        public DateTime CapturedAtUtc { get; set; }
    }

    public sealed class SessionRestoreResult
    {
        public string ProcessName { get; set; } = string.Empty;
        public int Restored { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
