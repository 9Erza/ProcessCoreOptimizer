using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Provides throttled process snapshots for the UI and lightweight profile watching.
    /// </summary>
    public sealed class ProcessScannerService
    {
        private readonly ProcessService _processService;
        private readonly ILogger _logger;
        private readonly Dictionary<ProcessInstanceKey, TimeSpan> _lastCpuTimes = new();
        private readonly object _cpuTimesLock = new();
        private DateTime _lastSampleUtc = DateTime.UtcNow;

        public ProcessScannerService(ProcessService processService)
        {
            _processService = processService;
            _logger = LoggerService.Instance;
        }

        public Task<ProcessScanResult> ScanUserProcessesAsync()
        {
            return Task.Run(ScanUserProcesses);
        }

        public Task<ProcessScanResult> ScanProfileProcessesAsync(IEnumerable<ProcessProfile> profiles)
        {
            return Task.Run(() => ScanProfileProcesses(profiles));
        }

        private ProcessScanResult ScanUserProcesses()
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcesses();
                double elapsedSeconds = Math.Max((DateTime.UtcNow - _lastSampleUtc).TotalSeconds, 0.1);
                _lastSampleUtc = DateTime.UtcNow;

                var userProcesses = processes
                    .Where(p => _processService.IsUserProcess(p))
                    .ToList();

                var result = BuildGroupedSnapshot(userProcesses, elapsedSeconds, includeResources: true);
                CleanupCpuCache(result.ActiveInstances);
                return result;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private ProcessScanResult ScanProfileProcesses(IEnumerable<ProcessProfile> profiles)
        {
            var processNames = profiles
                .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ProcessName))
                .Select(p => ProfileService.NormalizeProcessName(p.ProcessName))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var collected = new List<Process>();
            try
            {
                foreach (var processName in processNames)
                {
                    try
                    {
                        collected.AddRange(Process.GetProcessesByName(processName));
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Profile process scan skipped for '{processName}': {ex.Message}");
                    }
                }

                var result = BuildGroupedSnapshot(collected, elapsedSeconds: 1, includeResources: false);
                CleanupCpuCache(result.ActiveInstances);
                return result;
            }
            finally
            {
                foreach (var process in collected)
                {
                    process.Dispose();
                }
            }
        }

        private ProcessScanResult BuildGroupedSnapshot(IEnumerable<Process> processes, double elapsedSeconds, bool includeResources)
        {
            var activeInstances = new HashSet<ProcessInstanceKey>();
            var snapshots = new List<ProcessGroupSnapshot>();

            var groups = processes
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                long totalMemory = 0;
                double totalCpu = 0;
                ProcessPriorityClass priority = ProcessPriorityClass.Normal;
                var instances = new List<ProcessInstanceKey>();
                var observedPaths = new List<string?>();
                int firstPid = 0;

                foreach (var process in group)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        firstPid = firstPid == 0 ? process.Id : firstPid;

                        var key = CreateInstanceKey(process);
                        instances.Add(key);
                        activeInstances.Add(key);
                        observedPaths.Add(ProcessIdentityValidator.TryGetProcessPath(process));

                        if (includeResources)
                        {
                            totalMemory += process.WorkingSet64;
                            TimeSpan currentCpu = process.TotalProcessorTime;
                            lock (_cpuTimesLock)
                            {
                                if (_lastCpuTimes.TryGetValue(key, out TimeSpan lastCpu))
                                {
                                    double usageMs = Math.Max((currentCpu - lastCpu).TotalMilliseconds, 0);
                                    totalCpu += (usageMs / (elapsedSeconds * 1000 * Math.Max(Environment.ProcessorCount, 1))) * 100;
                                }

                                _lastCpuTimes[key] = currentCpu;
                            }
                            priority = process.PriorityClass;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Failed to snapshot process '{group.Key}': {ex.Message}");
                    }
                }

                if (instances.Count == 0) continue;

                snapshots.Add(new ProcessGroupSnapshot
                {
                    Name = group.Key,
                    FirstProcessId = firstPid,
                    InstanceCount = instances.Count,
                    TotalMemoryBytes = totalMemory,
                    CpuUsagePercent = totalCpu,
                    Priority = priority,
                    Instances = instances,
                    ExecutablePath = ResolveGroupPath(observedPaths)
                });
            }

            return new ProcessScanResult
            {
                Groups = snapshots,
                ActiveInstances = activeInstances
            };
        }

        public static ProcessInstanceKey CreateInstanceKey(Process process)
        {
            DateTime startTimeUtc;
            try
            {
                startTimeUtc = process.StartTime.ToUniversalTime();
            }
            catch
            {
                startTimeUtc = DateTime.MinValue;
            }

            return new ProcessInstanceKey(process.Id, startTimeUtc);
        }

        /// <summary>
        /// Testable group-path rule: one unique readable path wins, disagreement
        /// or fully-denied reads yield null (never a guess).
        /// </summary>
        public static string? ResolveGroupPath(IEnumerable<string?> observedPaths)
        {
            var distinct = observedPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => ProfileService.NormalizeExecutablePath(p))
                .Where(p => p != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        private void CleanupCpuCache(HashSet<ProcessInstanceKey> activeInstances)
        {
            lock (_cpuTimesLock)
            {
                foreach (var staleKey in _lastCpuTimes.Keys.Where(k => !activeInstances.Contains(k)).ToList())
                {
                    _lastCpuTimes.Remove(staleKey);
                }
            }
        }
    }
}
