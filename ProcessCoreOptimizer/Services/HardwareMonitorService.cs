using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Service responsible for interfacing with hardware drivers via LibreHardwareMonitor 
    /// to retrieve temperatures, loads, power consumption, and clock speeds.
    /// </summary>
    public class HardwareMonitorService : IDisposable
    {
        #region Fields
        private readonly Computer _computer;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the HardwareMonitorService and opens driver connections.
        /// </summary>
        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true
            };
            _computer.Open();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Scans all hardware components and returns a snapshot of current performance metrics.
        /// </summary>
        /// <returns>A HardwareMetrics object containing current sensor data.</returns>
        public HardwareMetrics GetAllMetrics()
        {
            var metrics = new HardwareMetrics();

            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();

                // --- CPU MONITORING ---
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    metrics.CpuLoad = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"))?.Value ?? 0;

                    // Fallback logic for CPU Temperature: Prioritize "Package" sensor, then any available temp sensor
                    var pkgTemp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Package"));
                    metrics.CpuTemp = pkgTemp?.Value ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature)?.Value ?? 0;

                    metrics.CpuPower = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"))?.Value ?? 0;
                    metrics.CpuClock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock)?.Value ?? 0;

                    metrics.CoreTemps.Clear();
                    var coreSensors = hardware.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Name.Contains("Core")).ToList();
                    foreach (var s in coreSensors)
                    {
                        metrics.CoreTemps.Add($"{s.Name}: {s.Value:N1}°C");
                    }
                }

                // --- GPU MONITORING (Nvidia & AMD) ---
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                {
                    metrics.GpuLoad = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core"))?.Value ?? 0;
                    metrics.GpuTemp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == "GPU Core")?.Value ?? 0;
                    metrics.GpuHotSpot = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Hot Spot"))?.Value ?? 0;
                    metrics.GpuVramTemp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Memory"))?.Value ?? 0;
                    metrics.GpuCoreClock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"))?.Value ?? 0;
                    metrics.GpuMemClock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Memory"))?.Value ?? 0;
                    metrics.GpuPower = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power)?.Value ?? 0;

                    var vramLoad = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Memory"));
                    metrics.VramUsagePct = vramLoad?.Value ?? 0;
                }

                // --- RAM MONITORING ---
                if (hardware.HardwareType == HardwareType.Memory)
                {
                    // Używamy dokładnych nazw "==", aby uniknąć pomyłki z "Virtual Memory Used" z pliku stronicowania
                    metrics.RamUsagePct = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "Memory")?.Value ?? 0;
                    metrics.RamUsedGB = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name == "Memory Used")?.Value ?? 0;
                    metrics.RamAvailableGB = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name == "Memory Available")?.Value ?? 0;
                }

                // --- STORAGE MONITORING ---
                if (hardware.HardwareType == HardwareType.Storage)
                {
                    var temp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (temp != null)
                        metrics.StorageInfo = $"{hardware.Name}: {temp.Value:N0}°C";
                }
            }

            // --- NATIVE RAM FALLBACK ---
            // Jeśli LibreHardwareMonitor zawiedzie (np. przez konflikt sterowników Ring0 z prawami Admina), 
            // pobieramy dane o RAM bezpośrednio i bezpiecznie z jądra systemu Windows.
            if (metrics.RamUsagePct == 0 || metrics.RamUsedGB == 0)
            {
                try
                {
                    using var availableCounter = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
                    double availableMB = availableCounter.NextValue();

                    // Windows Management Instrumentation (WMI) do pobrania całkowitej pamięci
                    using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    double totalMB = 0;
                    foreach (var item in searcher.Get())
                    {
                        totalMB = Convert.ToDouble(item["TotalVisibleMemorySize"]) / 1024; // KB to MB
                    }

                    if (totalMB > 0)
                    {
                        double usedMB = totalMB - availableMB;
                        metrics.RamAvailableGB = Math.Round(availableMB / 1024.0, 1);
                        metrics.RamUsedGB = Math.Round(usedMB / 1024.0, 1);
                        metrics.RamUsagePct = Math.Round((usedMB / totalMB) * 100, 1);
                    }
                }
                catch { /* Ignorujemy błędy fallbacku, żeby nie wysypać aplikacji */ }
            }

            return metrics;
        }
        #endregion

        #region Cleanup
        /// <summary>
        /// Safely closes the hardware driver connections and releases resources.
        /// </summary>
        public void Dispose()
        {
            try { _computer.Close(); } catch { /* Ignore cleanup errors */ }
        }
        #endregion
    }

    /// <summary>
    /// Data Transfer Object (DTO) containing a snapshot of all hardware sensor readings.
    /// </summary>
    public class HardwareMetrics
    {
        #region CPU Metrics
        public double CpuLoad { get; set; }
        public double CpuTemp { get; set; }
        public double CpuPower { get; set; }
        public double CpuClock { get; set; }
        public List<string> CoreTemps { get; set; } = new List<string>();
        #endregion

        #region GPU Metrics
        public double GpuLoad { get; set; }
        public double GpuTemp { get; set; }
        public double GpuHotSpot { get; set; }
        public double GpuVramTemp { get; set; }
        public double GpuCoreClock { get; set; }
        public double GpuMemClock { get; set; }
        public double GpuPower { get; set; }
        public double VramUsagePct { get; set; }
        #endregion

        #region RAM & Storage Metrics
        public double RamUsagePct { get; set; }
        public double RamUsedGB { get; set; }
        public double RamAvailableGB { get; set; }
        public string StorageInfo { get; set; } = "No data";
        #endregion
    }
}