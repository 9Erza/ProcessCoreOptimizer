using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using ProcessCoreOptimizer.WPF.Models;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for analyzing CPU architecture (P/E-Cores, SMT) 
    /// and retrieving real-time performance telemetry from Windows counters.
    /// </summary>
    public class HardwareService
    {
        #region Fields
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
            InitializeCounters();
        }
        #endregion

        #region Public Methods
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
            catch
            {
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

            return cores;
        }

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
                catch
                {
                    // If a counter fails (e.g., during system sleep), return zero to prevent crash
                    loads.Add(0);
                }
            }

            return loads;
        }
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// Pre-configures the performance counters to avoid overhead during the main loop.
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize CPU counters: {ex.Message}");
            }
        }
        #endregion
    }
}