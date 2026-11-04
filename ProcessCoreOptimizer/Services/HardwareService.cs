using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Handles CPU topology discovery and lightweight per-core telemetry.
    /// </summary>
    public class HardwareService : IDisposable
    {
        private const int MaxAffinityMaskLogicalProcessors = 64;
        private readonly ILogger _logger;
        private readonly List<PerformanceCounter> _cpuTimeCounters = new();
        private readonly List<PerformanceCounter> _cpuUtilityCounters = new();
        private readonly object _counterGate = new();
        private bool _countersInitialized;
        private int _readsCompleted;
        private bool _disposed;

        public HardwareService()
        {
            _logger = LoggerService.Instance;
        }

        public List<CoreInfo> GetCoreTopology()
        {
            try
            {
                var cpuSets = GetSystemCpuSetInfos()
                    .Where(x => x.Group == 0)
                    .OrderBy(x => x.LogicalProcessorIndex)
                    .Take(MaxAffinityMaskLogicalProcessors)
                    .ToList();

                if (cpuSets.Count > 0)
                {
                    var cores = BuildTopologyFromCpuSets(cpuSets);
                    _logger.Info($"CPU topology from CPU Sets: logical={cores.Count}, P={cores.Count(c => !c.IsECore && !c.IsThread)}, T={cores.Count(c => c.IsThread)}, E={cores.Count(c => c.IsECore)}");

                    var report = BuildTopologyReport(cores);
                    if (report.LimitedToGroup0)
                    {
                        _logger.Warn(report.LimitationMessage);
                    }

                    return cores;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"CPU Sets topology detection failed, falling back to WMI heuristic: {ex.Message}");
            }

            return GetFallbackTopology();
        }

        /// <summary>
        /// One logical processor's telemetry. Utilization is the share of time
        /// the processor was busy (0-100%); Utility includes frequency scaling
        /// and may exceed 100% (never presented as busy time). Null = unknown
        /// (first sample, missing counter, NaN/Infinity) and must render as "—".
        /// </summary>
        public sealed class CoreLoadSample
        {
            public double? UtilizationPercent { get; set; }
            public double? UtilityPercent { get; set; }
        }

        public List<CoreLoadSample> GetCurrentLoads()
        {
            List<(PerformanceCounter Time, PerformanceCounter Utility)> snapshot;
            lock (_counterGate)
            {
                EnsureCountersInitialized();
                snapshot = new List<(PerformanceCounter, PerformanceCounter)>(_cpuTimeCounters.Count);
                for (int i = 0; i < _cpuTimeCounters.Count && i < _cpuUtilityCounters.Count; i++)
                    snapshot.Add((_cpuTimeCounters[i], _cpuUtilityCounters[i]));
            }

            bool discard = false;
            lock (_counterGate)
            {
                // The first reads after (re)init have no valid interval behind
                // them: perform the reads to prime PDH but report Unknown.
                if (_readsCompleted < 2)
                {
                    _readsCompleted++;
                    discard = _readsCompleted < 2;
                }
            }

            var loads = new List<CoreLoadSample>(snapshot.Count);
            foreach (var (time, utility) in snapshot)
            {
                try
                {
                    double busy = time.NextValue();
                    double boosted = utility.NextValue();
                    if (discard)
                    {
                        loads.Add(new CoreLoadSample());
                        continue;
                    }

                    // Utilization and Utility are independent metrics: a bad read on one
                    // counter must not invalidate a verified sample from the other.
                    loads.Add(new CoreLoadSample
                    {
                        UtilizationPercent = NormalizeUtilization(busy),
                        UtilityPercent = NormalizeUtility(boosted)
                    });
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Failed to sample CPU load: {ex.Message}");
                    loads.Add(new CoreLoadSample());
                }
            }

            return loads;
        }

        /// <summary>
        /// Busy-time share, always 0-100. NaN/Infinity/negative read as unknown (null).
        /// Unit-testable; the UI renders null as "—", never as 0%.
        /// </summary>
        public static double? NormalizeUtilization(double raw) =>
            double.IsNaN(raw) || double.IsInfinity(raw) ? null : Math.Min(100, Math.Max(0, Math.Round(raw, 1)));

        /// <summary>
        /// Frequency-scaled utility; intentionally NOT clamped (Turbo Boost may
        /// legitimately exceed 100%). NaN/Infinity read as unknown (null).
        /// </summary>
        public static double? NormalizeUtility(double raw) =>
            double.IsNaN(raw) || double.IsInfinity(raw) ? null : Math.Round(raw, 1);

        public Dictionary<int, uint> GetLogicalCoreToCpuSetIdMap()
        {
            try
            {
                var map = GetSystemCpuSetInfos()
                    .Where(x => x.Group == 0)
                    .GroupBy(x => x.LogicalProcessorIndex)
                    .ToDictionary(g => (int)g.Key, g => g.First().Id);

                _logger.Info($"CPU Set Map: {map.Count} logical processors mapped");
                return map;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to build CPU Set map", ex);
                return new Dictionary<int, uint>();
            }
        }

        /// <summary>
        /// Describes what the OS exposes beyond what PCO can address.
        /// Multi-group machines are detected here; the 64-bit mask model stays
        /// limited to group 0 by design (see CpuTopologyReport).
        /// </summary>
        public CpuTopologyReport GetTopologyReport(IReadOnlyList<CoreInfo>? cores = null)
        {
            try
            {
                var all = GetSystemCpuSetInfos();
                int total = all.Count;
                int groups = all.Select(x => x.Group).Distinct().Count();
                int visible = cores?.Count ?? GetCoreTopology().Count;
                bool limited = total > visible;
                return new CpuTopologyReport
                {
                    TotalCpuSets = total,
                    ProcessorGroups = Math.Max(groups, 1),
                    VisibleLogicalProcessors = visible,
                    LimitedToGroup0 = limited,
                    LimitationMessage = limited
                        ? $"System exposes {total} CPU Sets across {Math.Max(groups, 1)} processor group(s); PCO addresses group 0 / first {visible} logical processors (64-bit affinity mask limit)."
                        : string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.Debug($"Topology report failed: {ex.Message}");
                return new CpuTopologyReport { ProcessorGroups = 1 };
            }
        }

        private CpuTopologyReport BuildTopologyReport(List<CoreInfo> cores)
        {
            try
            {
                var all = GetSystemCpuSetInfos();
                int total = all.Count;
                int groups = all.Select(x => x.Group).Distinct().Count();
                bool limited = total > cores.Count;
                return new CpuTopologyReport
                {
                    TotalCpuSets = total,
                    ProcessorGroups = Math.Max(groups, 1),
                    VisibleLogicalProcessors = cores.Count,
                    LimitedToGroup0 = limited,
                    LimitationMessage = limited
                        ? $"System exposes {total} CPU Sets across {Math.Max(groups, 1)} processor group(s); PCO addresses group 0 / first {cores.Count} logical processors (64-bit affinity mask limit)."
                        : string.Empty
                };
            }
            catch
            {
                return new CpuTopologyReport { ProcessorGroups = 1, VisibleLogicalProcessors = cores.Count };
            }
        }

        /// <summary>CPU model name for the informational overview (WMI, cached, honest Unknown).</summary>
        public string GetCpuModel()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("Select Name from Win32_Processor");
                foreach (var item in searcher.Get())
                {
                    var name = item["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to get CPU model via WMI: {ex.Message}");
            }

            return "Unknown";
        }

        public string GetCpuVendor()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("Select Manufacturer from Win32_Processor");
                foreach (var item in searcher.Get())
                {
                    var vendor = item["Manufacturer"]?.ToString();
                    if (!string.IsNullOrEmpty(vendor))
                    {
                        if (vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return "AMD";
                        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
                        return vendor;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to get CPU vendor via WMI: {ex.Message}");
            }

            return "Unknown";
        }

        private List<CoreInfo> BuildTopologyFromCpuSets(List<CpuSetInfo> cpuSets)
        {
            byte maxEfficiency = cpuSets.Max(x => x.EfficiencyClass);
            byte minEfficiency = cpuSets.Min(x => x.EfficiencyClass);
            bool isHeterogeneous = maxEfficiency > minEfficiency;

            var result = new List<CoreInfo>();

            var coreGroups = cpuSets
                .GroupBy(x => new { x.Group, x.CoreIndex })
                .OrderBy(g => g.Min(x => x.LogicalProcessorIndex));

            foreach (var coreGroup in coreGroups)
            {
                var logicalProcessors = coreGroup
                    .OrderBy(x => x.LogicalProcessorIndex)
                    .ToList();

                bool isECore = isHeterogeneous && logicalProcessors.Max(x => x.EfficiencyClass) < maxEfficiency;

                for (int i = 0; i < logicalProcessors.Count; i++)
                {
                    var cpu = logicalProcessors[i];
                    bool isThread = !isECore && logicalProcessors.Count > 1 && i > 0;

                    result.Add(new CoreInfo
                    {
                        Index = cpu.LogicalProcessorIndex,
                        Group = cpu.Group,
                        CoreIndex = cpu.CoreIndex,
                        EfficiencyClass = cpu.EfficiencyClass,
                        IsECore = isECore,
                        IsThread = isThread,
                        IsClassificationUnknown = false,
                        CpuSetId = cpu.Id,
                        TypeTag = isECore ? "[E]" : isThread ? "[T]" : "[P]"
                    });
                }
            }

            return result
                .OrderBy(c => c.Index)
                .Take(MaxAffinityMaskLogicalProcessors)
                .ToList();
        }

        private List<CoreInfo> GetFallbackTopology()
        {
            var cores = new List<CoreInfo>();
            int logicalProcs = Math.Min(Environment.ProcessorCount, MaxAffinityMaskLogicalProcessors);
            int physicalCores = 0;

            try
            {
                using var searcher = new ManagementObjectSearcher("Select NumberOfCores from Win32_Processor");
                foreach (var item in searcher.Get())
                {
                    physicalCores += int.Parse(item["NumberOfCores"]?.ToString() ?? "0");
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to query physical core count via WMI: {ex.Message}");
                physicalCores = logicalProcs;
            }

            if (physicalCores <= 0) physicalCores = logicalProcs;

            bool hasSmt = logicalProcs == physicalCores * 2;
            int pCores = hasSmt ? physicalCores : logicalProcs;
            int index = 0;

            for (int i = 0; i < pCores && index < logicalProcs; i++)
            {
                cores.Add(new CoreInfo { Index = index++, CoreIndex = i, TypeTag = "[?]", IsThread = false, IsECore = false, IsClassificationUnknown = true });
                if (hasSmt && index < logicalProcs)
                {
                    cores.Add(new CoreInfo { Index = index++, CoreIndex = i, TypeTag = "[?]", IsThread = false, IsClassificationUnknown = true });
                }
            }

            _logger.Warn("Using fallback CPU topology. P/E-core classification is unavailable (Unknown) on this system.");
            return cores;
        }

        private List<CpuSetInfo> GetSystemCpuSetInfos()
        {
            var results = new List<CpuSetInfo>();
            using var currentProcess = Process.GetCurrentProcess();
            IntPtr currentProcessHandle = currentProcess.Handle;

            NativeMethods.GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint length, currentProcessHandle, 0);
            if (length == 0)
            {
                return results;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!NativeMethods.GetSystemCpuSetInformation(buffer, length, out length, currentProcessHandle, 0))
                {
                    int error = Marshal.GetLastWin32Error();
                    _logger.Warn($"GetSystemCpuSetInformation failed. Win32Error={error}");
                    return results;
                }

                IntPtr currentPtr = buffer;
                IntPtr endPtr = IntPtr.Add(buffer, (int)length);

                while (currentPtr.ToInt64() < endPtr.ToInt64())
                {
                    uint size = (uint)Marshal.ReadInt32(currentPtr, 0);
                    uint type = (uint)Marshal.ReadInt32(currentPtr, 4);

                    if (size == 0) break;

                    if (type == 0 && size >= 20)
                    {
                        results.Add(new CpuSetInfo
                        {
                            Id = (uint)Marshal.ReadInt32(currentPtr, 8),
                            Group = (ushort)Marshal.ReadInt16(currentPtr, 12),
                            LogicalProcessorIndex = Marshal.ReadByte(currentPtr, 14),
                            CoreIndex = Marshal.ReadByte(currentPtr, 15),
                            EfficiencyClass = Marshal.ReadByte(currentPtr, 18)
                        });
                    }

                    currentPtr = IntPtr.Add(currentPtr, (int)size);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return results;
        }

        private void EnsureCountersInitialized()
        {
            if (_countersInitialized || _disposed) return;
            _countersInitialized = true;
            _readsCompleted = 0;

            try
            {
                int coreCount = Math.Min(Environment.ProcessorCount, MaxAffinityMaskLogicalProcessors);
                // Instance "0,i" = group 0, logical processor i. Both counters
                // share the instance mapping (verified against a pinned burner).
                for (int i = 0; i < coreCount; i++)
                {
                    var time = new PerformanceCounter("Processor Information", "% Processor Time", $"0,{i}");
                    time.NextValue();
                    _cpuTimeCounters.Add(time);
                    var utility = new PerformanceCounter("Processor Information", "% Processor Utility", $"0,{i}");
                    utility.NextValue();
                    _cpuUtilityCounters.Add(utility);
                }

                _logger.Info($"Initialized {coreCount} CPU telemetry counter pairs (time + utility)");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to initialize CPU performance counters: {ex.Message}");
            }
        }

        public void ReleaseCpuLoadCounters()
        {
            lock (_counterGate)
            {
                foreach (var counter in _cpuTimeCounters)
                {
                    counter.Dispose();
                }

                foreach (var counter in _cpuUtilityCounters)
                {
                    counter.Dispose();
                }

                if (_cpuTimeCounters.Count > 0)
                {
                    _logger.Debug($"Released {_cpuTimeCounters.Count} CPU telemetry counter pairs");
                }

                _cpuTimeCounters.Clear();
                _cpuUtilityCounters.Clear();
                _countersInitialized = false;
                _readsCompleted = 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseCpuLoadCounters();
        }

        private sealed class CpuSetInfo
        {
            public uint Id { get; set; }
            public int Group { get; set; }
            public int LogicalProcessorIndex { get; set; }
            public int CoreIndex { get; set; }
            public byte EfficiencyClass { get; set; }
        }
    }
}
