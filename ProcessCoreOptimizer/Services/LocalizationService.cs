using ProcessCoreOptimizer.WPF.Logging;
using System;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Centralizes runtime localization resource updates.
    /// This is a transition step before moving all strings to resource dictionaries.
    /// </summary>
    public sealed class LocalizationService
    {
        private readonly ILogger _logger = LoggerService.Instance;

        public void ApplyLanguage(string langCode)
        {
            try
            {
                var res = WpfApplication.Current.Resources;
                bool isPl = langCode == "pl";

                res["StrSystemProcesses"] = isPl ? "Procesy Systemowe" : "System Processes";
                res["StrSavedProfiles"] = isPl ? "Zapisane Profile" : "Saved Profiles";
                res["StrHardwareMonitor"] = isPl ? "Monitor Sprzętu" : "Hardware Monitor";
                res["StrSettings"] = isPl ? "Ustawienia" : "Settings";
                res["StrConsoleLog"] = isPl ? "LOGI KONSOLI" : "CONSOLE LOG";
                res["StrProcessList"] = isPl ? "Lista Procesów" : "Process List";
                res["StrYourProfiles"] = isPl ? "Twoje Profile" : "Your Profiles";
                res["StrCpuUsage"] = isPl ? "UŻYCIE CPU" : "CPU USAGE";
                res["StrCpuTemp"] = isPl ? "TEMPERATURA CPU" : "CPU TEMPERATURE";
                res["StrGpuUsage"] = isPl ? "UŻYCIE GPU" : "GPU USAGE";
                res["StrGpuTemp"] = isPl ? "TEMPERATURA GPU" : "GPU TEMPERATURE";
                res["StrRamUsage"] = isPl ? "UŻYCIE RAM (%)" : "RAM USAGE (%)";
                res["StrRamUsedAvail"] = isPl ? "RAM ZAJĘTY / DOSTĘPNY" : "RAM USED / AVAILABLE";
                res["StrExpandHardware"] = isPl ? "Rozwiń Szczegóły Sprzętu" : "Detailed Hardware Data";
                res["StrGpuDetails"] = isPl ? "SZCZEGÓŁY GPU" : "GPU DETAILS";
                res["StrCpuDetails"] = isPl ? "SZCZEGÓŁY CPU" : "CPU DETAILS";
                res["StrAppSettings"] = isPl ? "Ustawienia Aplikacji" : "Application Settings";
                res["StrStartWindows"] = isPl ? "Uruchamiaj z systemem Windows" : "Start with Windows";
                res["StrStartMinimized"] = isPl ? "Uruchom zminimalizowany" : "Start Minimized";
                res["StrMinToTray"] = isPl ? "Minimalizuj do zasobnika" : "Minimize to Tray";
                res["StrCloseToTray"] = isPl ? "Zamykaj do zasobnika" : "Close to Tray";
                res["StrStartAdmin"] = isPl ? "Uruchom jako Administrator" : "Start as Administrator";
                res["StrAdminReq"] = isPl ? "Wymaga restartu. Pozwala na optymalizację w tle." : "Requires restart. Allows background optimization.";
                res["StrAllowRealtime"] = isPl ? "Pokaż priorytet RealTime (zaawansowane)" : "Show RealTime priority (advanced)";
                res["StrRealtimeWarn"] = isPl ? "Może spowodować przycięcia systemu. Używaj tylko świadomie." : "Can make the system unresponsive. Use only if you know what you are doing.";
                res["StrLogEnabled"] = isPl ? "Włącz logowanie do pliku" : "Enable file logging";
                res["StrHardwareMonitorSwitch"] = isPl ? "Monitor sprzętu" : "Hardware monitor";
                res["StrHardwareMonitorOffInfo"] = isPl ? "Monitor sprzętu jest wyłączony. Włącz go tylko wtedy, gdy chcesz podgląd temperatur i użycia podzespołów." : "Hardware monitor is disabled. Enable it only when you want live temperatures and usage metrics.";
                res["StrBackgroundWatcherStatus"] = isPl ? "Watcher profili w tle: aktywny" : "Background profile watcher: active";
                res["StrEnableStorageSensors"] = isPl ? "Włącz czujniki dysków (zaawansowane)" : "Enable storage sensors (advanced)";
                res["StrStorageSensorsWarn"] = isPl ? "Domyślnie wyłączone, aby monitor sprzętu nie odpytywał niepotrzebnie dysków." : "Disabled by default to avoid unnecessary disk polling.";
                res["StrCheckUpdates"] = isPl ? "Sprawdzaj aktualizacje przy starcie" : "Check for updates on startup";
                res["StrLanguage"] = isPl ? "Język aplikacji" : "Language";
                res["StrCpuCores"] = isPl ? "Rdzenie CPU" : "CPU Cores";
                res["StrSelectAll"] = isPl ? "Zaznacz Wszystko" : "Select All";
                res["StrClearAll"] = isPl ? "Odznacz Wszystko" : "Clear All";
                res["StrDisableECores"] = isPl ? "Wyłącz E-Cores" : "Disable E-Cores";
                res["StrSetPriority"] = isPl ? "Ustaw Priorytet" : "Set Priority";
                res["StrSetAffinity"] = isPl ? "Zastosuj Optymalizację" : "Apply Optimization";
                res["StrSaveProfile"] = isPl ? "Zapisz Profil" : "Save Profile";
                res["StrUpdateProfile"] = isPl ? "Aktualizuj Profil" : "Update Profile";
                res["StrDeleteProfile"] = isPl ? "Usuń Profil" : "Delete Profile";
                res["StrColName"] = isPl ? "NAZWA" : "NAME";
                res["StrColInstances"] = "INST.";
                res["StrColPriority"] = isPl ? "PRIORYTET" : "PRIORITY";
                res["StrColRam"] = "RAM";
                res["StrColCpu"] = "CPU";
                res["StrColMode"] = isPl ? "TRYB" : "MODE";
                res["StrVramUsage"] = isPl ? "Zużycie VRAM:" : "VRAM Usage:";
                res["StrCoreClock"] = isPl ? "Takt. Rdzenia:" : "Core Clock:";
                res["StrMemClock"] = isPl ? "Takt. Pamięci:" : "Mem Clock:";
                res["StrHotSpot"] = "Hot Spot:";
                res["StrVramTemp"] = isPl ? "Temp. VRAM:" : "VRAM Temp:";
                res["StrPowerDraw"] = isPl ? "Pobór Mocy:" : "Power Draw:";
                res["StrAvgClock"] = isPl ? "Średnie Takt.:" : "Avg Clock:";
                res["StrCoreTemps"] = isPl ? "Temperatury Rdzeni:" : "Core Temperatures:";
                res["StrOptimizationMode"] = isPl ? "Tryb Optymalizacji:" : "Optimization Mode:";
                res["ModeAffinity"] = isPl ? "Koligacja (Affinity)" : "Affinity (Standard)";
                res["ModeCpuSets"] = isPl ? "Zestawy (CPU Sets)" : "CPU Sets (Smart)";
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to apply localization: {ex.Message}");
            }
        }
    }
}
