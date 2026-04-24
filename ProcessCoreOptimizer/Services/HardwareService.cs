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
    /// Service responsible for analyzing CPU architecture (P/E-Cores, SMT)
    /// and retrieving real-time performance telemetry from Windows counters.
    /// </summary>
    public class HardwareService
    {
        #region Private Fields

        private readonly ILogger _logger;

        /// <summary>
        /// List of system performance counters, one for each logical processor.
        /// </summary>
        private readonly List<PerformanceCounter> _cpuCounters = new();

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes the HardwareService and prepares performance counters for monitoring.
        /// </summary>
        public HardwareService()
        {
            _logger = LoggerService.Instance;
            InitializeCounters();
        }

        #endregion

        #region CPU Topology & Architecture Analysis

        /// <summary>
        /// Detects the physical and logical CPU layout, classifying cores into 
        /// Performance (P), Efficiency (E), and Hyper-Threading (T) types.
        /// </summary>
        /// <returns>A collection of CoreInfo objects for UI binding.</returns>
        public List<CoreInfo> GetCoreTopology()
        {
            var cores = new List<CoreInfo>();
            int logicalProcs = Environment.ProcessorCount;
            int physicalCores = 0;

            // Step 1: Query physical core count via Windows Management Instrumentation (WMI)
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
                _logger.Error("Failed to query physical core count via WMI", ex);
                // Fallback to logical count if WMI access is restricted
                physicalCores = logicalProcs;
            }

            int pCores = physicalCores;
            int eCores = 0;
            bool hasSmt = false;

            // Step 2: Detect Hybrid Architecture (Intel 12th Gen+) or standard SMT (Hyper-Threading)
            if (logicalProcs > physicalCores && logicalProcs < physicalCores * 2)
            {
                // Logic for P-Cores with SMT enabled and E-Cores (always single-threaded)
                pCores = logicalProcs - physicalCores;
                eCores = physicalCores - pCores;
                hasSmt = true;
            }
            else if (logicalProcs == physicalCores * 2)
            {
                // Standard symmetrical SMT scenario
                hasSmt = true;
            }

            int index = 0;

            // Step 3: Populate the core list based on architectural analysis
            for (int i = 0; i < pCores; i++)
            {
                // Add primary Performance Core
                cores.Add(new CoreInfo { Index = index++, TypeTag = "[P]", IsThread = false, IsECore = false });

                // Add accompanying SMT/Hyper-Thread if present
                if (hasSmt)
                {
                    cores.Add(new CoreInfo { Index = index++, TypeTag = "[T]", IsThread = true, IsECore = false });
                }
            }

            // Add single-threaded Efficiency Cores
            for (int i = 0; i < eCores; i++)
            {
                cores.Add(new CoreInfo { Index = index++, TypeTag = "[E]", IsThread = false, IsECore = true });
            }

            _logger.Info($"CPU Topology: {pCores} P-Cores, {eCores} E-Cores, SMT={hasSmt}, Total logical cores: {logicalProcs}");
            return cores;
        }

        #endregion

        #region Performance Telemetry

        /// <summary>
        /// Samples the current real-time utilization for each logical processor core.
        /// </summary>
        /// <returns>A list of load percentages (0-100) per core.</returns>
        public List<double> GetCurrentLoads()
        {
            var loads = new List<double>();

            foreach (var counter in _cpuCounters)
            {
                try
                {
                    // NextValue() triggers a fresh sample from the OS performance provider
                    double val = counter.NextValue();
                    loads.Add(Math.Round(val, 1));
                }
                catch (Exception ex)
                {
                    // If a counter fails (e.g., during system sleep), return zero to prevent crash
                    _logger.Error("Failed to sample CPU load", ex);
                    loads.Add(0);
                }
            }

            double avgLoad = loads.Average();
            _logger.Info($"CPU Load: Avg={avgLoad:F1}%, Max={loads.Max():F1}%, Min={loads.Min():F1}%");
            return loads;
        }

        #endregion

        #region Advanced OS Integration (CPU Sets)

        /// <summary>
        /// Retrieves the mapping from the system kernel: Logical Core Index -> Native CpuSet ID.
        /// Essential for supporting the "CPU Sets (Smart)" optimization mode.
        /// </summary>
        /// <returns>A dictionary mapping logical core indices to their native Windows CPU Set IDs.</returns>
        public Dictionary<int, uint> GetLogicalCoreToCpuSetIdMap()
        {
            var map = new Dictionary<int, uint>();
            IntPtr currentProcessHandle = Process.GetCurrentProcess().Handle;

            // Determine the required buffer size
            NativeMethods.GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint length, currentProcessHandle, 0);
            if (length == 0)
            {
                _logger.Warn("Failed to get CPU Set information - system may not support it");
                return map;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (NativeMethods.GetSystemCpuSetInformation(buffer, length, out length, currentProcessHandle, 0))
                {
                    IntPtr currentPtr = buffer;
                    IntPtr endPtr = IntPtr.Add(buffer, (int)length);

                    while (currentPtr.ToInt64() < endPtr.ToInt64())
                    {
                        uint size = (uint)Marshal.ReadInt32(currentPtr, 0);
                        uint type = (uint)Marshal.ReadInt32(currentPtr, 4);

                        // Type 0 indicates a CpuSetInformation structure
                        if (type == 0)
                        {
                            uint id = (uint)Marshal.ReadInt32(currentPtr, 8);
                            byte logicalIndex = Marshal.ReadByte(currentPtr, 14);

                            if (!map.ContainsKey(logicalIndex))
                            {
                                map.Add(logicalIndex, id);
                            }
                        }

                        // Failsafe to prevent infinite loops
                        if (size == 0) break;
                        currentPtr = IntPtr.Add(currentPtr, (int)size);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to parse CPU Set information", ex);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            _logger.Info($"CPU Set Map: {map.Count} cores mapped");
            return map;
        }

        #endregion

        /// <summary>
        /// Detects the CPU vendor (AMD or Intel) using Windows Management Instrumentation (WMI).
        /// </summary>
        /// <returns>"AMD" for AMD CPUs, "Intel" for Intel CPUs, or "Unknown" if detection fails.</returns>
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
                        return vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD" : "Intel";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to get CPU vendor via WMI", ex);
            }
            return "Unknown";
        }

        #region Private Helper Methods

        /// <summary>
        /// Pre-configures the performance counters to avoid overhead during the main monitoring loop.
        /// </summary>
        private void InitializeCounters()
        {
            try
            {
                int coreCount = Environment.ProcessorCount;
                for (int i = 0; i < coreCount; i++)
                {
                    // Using "Processor Information" category for best compatibility with modern multi-socket/hybrid CPUs
                    var counter = new PerformanceCounter("Processor Information", "% Processor Utility", $"0,{i}");

                    // First call to NextValue always returns 0, so we prime it here
                    counter.NextValue();
                    _cpuCounters.Add(counter);
                }
                _logger.Info($"Initialized {coreCount} CPU performance counters");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to initialize CPU performance counters", ex);
            }
        }

        #endregion
    }
}