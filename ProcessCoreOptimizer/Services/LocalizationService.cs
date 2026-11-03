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
                res["StrActivity"] = isPl ? "Aktywność" : "Activity";
                res["StrSelectProcessHint"] = isPl ? "Wybierz proces, aby zobaczyć szczegóły i dostępne działania." : "Select a process to view details and available actions.";
                res["StrSelectedProcess"] = isPl ? "Wybrany proces" : "Selected process";
                res["StrInitialLoading"] = isPl ? "Ładowanie procesów…" : "Loading processes…";
                res["StrClosePanel"] = isPl ? "Zamknij panel" : "Close panel";
                res["StrNoProfilesTitle"] = isPl ? "Brak zapisanych profili" : "No saved profiles yet";
                res["StrNoProfilesBody"] = isPl ? "Utwórz profil, aby automatycznie stosować ustawienia CPU i priorytetu przy starcie aplikacji." : "Create a profile to automatically apply CPU and priority settings when an application starts.";
                res["StrSelectProfileHint"] = isPl ? "Wybierz profil, aby zobaczyć szczegóły i go edytować." : "Select a profile to view and edit its configuration.";
                res["StrNoRulesTitle"] = isPl ? "Brak reguł" : "No rules yet";
                res["StrNoRulesBody"] = isPl ? "Reguły stosują profil, gdy proces startuje lub trafia na pierwszy plan. Utwórz pierwszą regułę, aby zautomatyzować optymalizację." : "Rules apply a profile when a process launches or comes to the foreground. Create your first rule to automate optimization.";
                res["StrProcessorOverview"] = isPl ? "Przegląd procesora" : "Processor overview";
                res["StrCoreTopology"] = isPl ? "Topologia rdzeni" : "Core topology";
                res["StrTopologyLegend"] = isPl ? "[P] wątek główny · [T] wątek SMT · [E] rdzeń efficiency · [?] nieznany" : "[P] primary thread · [T] SMT sibling · [E] efficiency core · [?] unknown";
                res["StrAffinityExplained"] = isPl ? "Affinity to twarde ograniczenie: proces może działać tylko na wybranych procesorach logicznych." : "Affinity is a hard limit: the process may run only on the selected logical processors.";
                res["StrCpuSetsExplained"] = isPl ? "CPU Sets to preferencja: Windows preferuje wybrane procesory, ale może szeregować gdzie indziej. Żaden FPS nie jest gwarantowany." : "CPU Sets are a preference: Windows prefers the selected processors but may schedule elsewhere. No FPS is guaranteed.";
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
                res["StrLanguage"] = isPl ? "Język aplikacji" : "Language";
                res["StrStartWindows"] = isPl ? "Uruchamiaj z systemem Windows" : "Start with Windows";
                res["StrMinToTray"] = isPl ? "Minimalizuj do zasobnika" : "Minimize to Tray";
                res["StrCloseToTray"] = isPl ? "Zamykaj do zasobnika" : "Close to Tray";
                res["StrSetGroupGeneral"] = isPl ? "Ogólne" : "General";
                res["StrSetGroupOptimization"] = isPl ? "Optymalizacja" : "Optimization";
                res["StrSetGroupStartup"] = isPl ? "Uruchamianie i uprawnienia" : "Startup & Privileges";
                res["StrSetGroupMonitoring"] = isPl ? "Monitorowanie procesów" : "Process Monitoring";
                res["StrSetGroupHardware"] = isPl ? "Monitor sprzętu" : "Hardware Monitoring";
                res["StrSetGroupDiagnostics"] = isPl ? "Diagnostyka" : "Diagnostics";
                res["StrSetGroupUpdates"] = isPl ? "Aktualizacje" : "Updates";
                res["StrSetGroupData"] = isPl ? "Dane aplikacji i reset" : "Application Data & Reset";
                res["StrSetGroupAbout"] = isPl ? "O programie" : "About";
                res["StrStartupWindowBehavior"] = isPl ? "Zachowanie okna przy logowaniu" : "Window behavior on logon";
                res["StrStartupRunElevated"] = isPl ? "Uruchamiaj z prawami administratora przy starcie" : "Run with administrator rights at startup";
                res["StrStartupElevatedHint"] = isPl ? "Elevated startup używa zadania harmonogramu (HighestAvailable). Zwykły autostart używa wpisu rejestru Run. Aktywny jest zawsze tylko jeden kanał." : "Elevated startup uses a Task Scheduler logon task with HighestAvailable. Normal startup uses a registry Run entry. Only one channel is active at a time.";
                res["StrStartupStatus"] = isPl ? "Rzeczywisty stan autostartu" : "Actual startup state";
                res["StrStartupRefresh"] = isPl ? "Odśwież" : "Refresh";
                res["StrStartupWorking"] = isPl ? "Stosowanie…" : "Applying…";
                res["StrCurrentPrivileges"] = isPl ? "Aktualne uprawnienia" : "Current privileges";
                res["StrRestartAsAdmin"] = isPl ? "Uruchom ponownie jako Administrator…" : "Restart as Administrator…";
                res["StrTrayHint"] = isPl ? "Zachowanie tray dotyczy ręcznych przycisków minimalizacji/zamykania." : "Tray behavior applies to manual minimize/close buttons.";
                res["StrOpenWizard"] = isPl ? "Otwórz kreator pierwszego uruchomienia…" : "Open First-Run Wizard…";
                res["StrRestoreDefaults"] = isPl ? "Przywróć ustawienia domyślne" : "Restore Default Settings";
                res["StrFactoryResetTitle"] = isPl ? "Factory Reset — usuń wszystkie dane aplikacji" : "Factory Reset — erase all application data";
                res["StrFactoryResetWarning"] = isPl ? "Usuwa ustawienia, profile, kopie, logi i cache PCO. Ręczne eksporty poza katalogiem danych zostają. Wpisz RESET, aby uzbroić." : "Deletes settings, profiles, backups, logs and caches owned by PCO. Exported files outside the data folder are kept. Type RESET to arm.";
                res["StrFactoryReset"] = isPl ? "Usuń wszystkie dane aplikacji" : "Erase All Application Data";
                res["StrAboutLine"] = isPl ? "Samodzielne narzędzie do CPU i procesów. Bez sterowników, usług i ukrytych zmian." : "Standalone CPU & process control utility. No drivers, no services, no hidden tweaks.";
                res["StrWizardWelcome"] = isPl ? "Witaj w Process Core Optimizer" : "Welcome to Process Core Optimizer";
                res["StrWizardWelcomeBody"] = isPl ? "Skupione narzędzie do kontroli CPU procesów: affinity, CPU Sets, priorytety i profile. Nic nie działa w tle bez Twoich jawnych profili." : "A focused tool for process CPU control: affinity, CPU Sets, priorities and profiles. Nothing runs in the background without your explicit profiles.";
                res["StrWizardModesTitle"] = isPl ? "Affinity vs CPU Sets" : "Affinity vs CPU Sets";
                res["StrWizardModesBody"] = isPl ? "Affinity przypina proces do wybranych rdzeni (sztywno). CPU Sets jedynie preferują wybrane rdzenie, zostawiając schedulerowi Windows więcej swobody (zwykle łagodniej na CPU hybrydowych)." : "Affinity pins a process to selected cores (strict). CPU Sets only prefer selected cores and let Windows schedule more freely (usually friendlier on hybrid CPUs).";
                res["StrWizardModesNote"] = isPl ? "Monitor sprzętu pozostaje WYŁĄCZONY i domyślnie nie włączamy żadnych agresywnych optymalizacji. Wszystko stosujesz jawnie." : "Hardware monitoring stays OFF and no aggressive optimization is enabled by default. You apply everything explicitly.";
                res["StrWizardStartupTitle"] = isPl ? "Zachowanie przy starcie" : "Startup behavior";
                res["StrWizardNoAdminNote"] = isPl ? "Prawa administratora są opcjonalne. Aplikacja działa bez eskalacji; podwyższenie proponujemy tylko tam, gdzie wymaga go Windows (np. elevated autostart albo chronione procesy)." : "Administrator rights are optional. The app works without elevation; elevation is only offered where Windows requires it (for example, elevated autostart or protected processes).";
                res["StrWizardSkip"] = isPl ? "Pomiń" : "Skip";
                res["StrWizardBack"] = isPl ? "Wstecz" : "Back";
                res["StrWizardNext"] = isPl ? "Dalej" : "Next";
                res["StrWizardFinish"] = isPl ? "Zakończ" : "Finish";
                res["StrAllowRealtime"] = isPl ? "Pokaż priorytet RealTime (zaawansowane)" : "Show RealTime priority (advanced)";
                res["StrRealtimeWarn"] = isPl ? "Może spowodować przycięcia systemu. Używaj tylko świadomie." : "Can make the system unresponsive. Use only if you know what you are doing.";
                res["StrLogEnabled"] = isPl ? "Włącz logowanie do pliku" : "Enable file logging";
                res["StrHardwareMonitorSwitch"] = isPl ? "Monitor sprzętu" : "Hardware monitor";
                res["StrHardwareMonitorOffInfo"] = isPl ? "Monitor sprzętu jest wyłączony. Włącz go tylko wtedy, gdy chcesz podgląd temperatur i użycia podzespołów." : "Hardware monitor is disabled. Enable it only when you want live temperatures and usage metrics.";
                res["StrHwEmptyTitle"] = isPl ? "Brak danych na żywo" : "No live data";
                res["StrHwEmptyBody"] = isPl ? "Włącz monitoring powyżej, aby zobaczyć temperatury, taktowania, VRAM i pobór mocy. Czujniki pozostają wyłączone do czasu Twojej decyzji." : "Enable monitoring above to see temperatures, clocks, VRAM and power draw. Sensors stay off until you opt in.";
                res["StrBackgroundWatcherStatus"] = isPl ? "Watcher profili w tle: aktywny" : "Background profile watcher: active";
                res["StrEnableStorageSensors"] = isPl ? "Włącz czujniki dysków (zaawansowane)" : "Enable storage sensors (advanced)";
                res["StrStorageSensorsWarn"] = isPl ? "Domyślnie wyłączone, aby monitor sprzętu nie odpytywał niepotrzebnie dysków." : "Disabled by default to avoid unnecessary disk polling.";
                res["StrCheckUpdates"] = isPl ? "Sprawdzaj aktualizacje przy starcie" : "Check for updates on startup";
                res["StrCheckUpdatesNow"] = isPl ? "Sprawdź aktualizacje" : "Check for updates";
                res["StrSetGroupGeneral"] = isPl ? "Ogólne" : "General";
                res["StrSetGroupOptimization"] = isPl ? "Optymalizacja" : "Optimization";
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
                res["StrNavCpuTopology"] = isPl ? "CPU i topologia" : "CPU & Topology";
                res["StrDiagnostics"] = isPl ? "Diagnostyka" : "Diagnostics";
                res["StrProcessFilter"] = isPl ? "Filtruj procesy..." : "Filter processes...";
                res["StrProfileFilter"] = isPl ? "Filtruj profile..." : "Filter profiles...";
                res["StrProcessDetails"] = isPl ? "Szczegóły procesu" : "Process details";
                res["StrNoSelection"] = isPl ? "Wybierz proces lub profil, aby zobaczyć szczegóły." : "Select a process or profile to see details.";
                res["StrCorePresets"] = isPl ? "Presety rdzeni" : "Core presets";
                res["StrPreviewPreset"] = isPl ? "Podgląd" : "Preview";
                res["StrTopologyNote"] = isPl ? "Uwaga o topologii" : "Topology note";
                res["StrConflictsTitle"] = isPl ? "Konflikty profili" : "Profile conflicts";
                res["StrNoConflicts"] = isPl ? "Brak konfliktów. Kolejność: path-bound, potem najnowszy." : "No conflicts. Precedence: path-bound, then newest.";
                res["StrRecentOps"] = isPl ? "Ostatnie operacje" : "Recent operations";
                res["StrWatcherTitle"] = isPl ? "Status watchera" : "Watcher status";
                res["StrCaptureLive"] = isPl ? "Przechwyć bieżące ustawienia" : "Capture live settings";
                res["StrDuplicateProfile"] = isPl ? "Duplikuj" : "Duplicate";
                res["StrExportProfiles"] = isPl ? "Eksport" : "Export";
                res["StrImportProfiles"] = isPl ? "Import" : "Import";
                res["StrColStatus"] = isPl ? "STATUS" : "STATUS";
                res["StrUnknown"] = isPl ? "Nieznany" : "Unknown";
                res["StrApplyPreset"] = isPl ? "Zastosuj" : "Apply";
                res["StrRestoreSettings"] = isPl ? "Przywróć sesję" : "Restore session";
                res["StrDeveloper"] = isPl ? "Autor" : "Developer";
                res["StrSourceCode"] = isPl ? "Kod źródłowy" : "Source Code";
                res["StrSupportDev"] = isPl ? "Wesprzyj rozwój" : "Support Development";
                res["StrAuthorTooltip"] = isPl ? "GitHub autora — 9Erza" : "Developer GitHub — 9Erza";
                res["StrRepoTooltip"] = isPl ? "Repozytorium projektu — Process Core Optimizer" : "Project Repository — Process Core Optimizer";
                res["StrSupportTooltip"] = isPl ? "Postaw kawę — Wesprzyj rozwój" : "Buy Me a Coffee — Support Development";
                res["StrFooterRepoLabel"] = isPl ? "Repozytorium projektu" : "Project Repository";
                res["StrFooterWebsiteTooltip"] = isPl ? "Strona autora — DobryPC.pl" : "Author website — DobryPC.pl";
                res["StrFooterSupportLabel"] = isPl ? "Wesprzyj projekt" : "Support the Project";
                res["StrWebsiteCaption"] = isPl ? "Strona internetowa" : "Website";
                res["StrLinkOpenFailed"] = isPl ? "Nie udało się otworzyć strony w przeglądarce." : "Could not open the link in your browser.";
                // Process Explorer 2.0 (grouped/instance views, filters, context menu)
                res["StrViewGrouped"] = isPl ? "Grupy" : "Grouped";
                res["StrViewInstances"] = isPl ? "Instancje" : "Instances";
                res["StrOnlyOptimized"] = isPl ? "Tylko zoptymalizowane" : "Only optimized";
                res["StrOnlyWithProfile"] = isPl ? "Z profilem" : "With profile";
                res["StrColPid"] = "PID";
                res["StrColPath"] = isPl ? "ŚCIEŻKA" : "PATH";
                res["StrColProfile"] = isPl ? "PROFIL" : "PROFILE";
                res["StrCtxDetails"] = isPl ? "Szczegóły" : "View Details";
                res["StrCtxCreateProfile"] = isPl ? "Utwórz profil" : "Create Profile";
                res["StrCtxCapture"] = isPl ? "Przechwyć ustawienia" : "Capture Current Settings";
                res["StrCtxPreview"] = isPl ? "Podgląd zmian" : "Preview Changes";
                res["StrCtxApply"] = isPl ? "Zastosuj profil" : "Apply Profile";
                res["StrCtxRestore"] = isPl ? "Przywróć ustawienia" : "Restore Session Settings";
                res["StrCtxCopyPid"] = isPl ? "Kopiuj PID" : "Copy PID";
                res["StrCtxCopyPath"] = isPl ? "Kopiuj ścieżkę EXE" : "Copy EXE Path";
                res["StrCtxOpenLocation"] = isPl ? "Otwórz lokalizację pliku" : "Open File Location";
                // Profile Studio editor
                res["StrProfileEditorTitle"] = isPl ? "Edytor profilu" : "Profile Editor";
                res["StrProfileName"] = isPl ? "Nazwa procesu" : "Process name";
                res["StrProfilePath"] = isPl ? "Ścieżka EXE (opcjonalna, czyni profil path-bound)" : "EXE path (optional, makes the profile path-bound)";
                res["StrBrowse"] = isPl ? "Przeglądaj…" : "Browse…";
                res["StrAffinityMaskHex"] = isPl ? "Maska affinity (hex, np. FF)" : "Affinity mask (hex, e.g. FF)";
                res["StrApplyPriority"] = isPl ? "Zastosuj priorytet" : "Apply priority";
                res["StrApplyCores"] = isPl ? "Zastosuj rdzenie CPU" : "Apply CPU cores";
                res["StrProfileEnabled"] = isPl ? "Włączony" : "Enabled";
                res["StrNotes"] = isPl ? "Notatki" : "Notes";
                res["StrSave"] = isPl ? "Zapisz" : "Save";
                res["StrCancel"] = isPl ? "Anuluj" : "Cancel";
                res["StrNewProfile"] = isPl ? "Nowy" : "New";
                res["StrEditProfile"] = isPl ? "Edytuj" : "Edit";
                res["StrCtxToggleEnabled"] = isPl ? "Włącz / Wyłącz" : "Enable / Disable";
                // Smart Rules
                res["StrSmartRules"] = isPl ? "Inteligentne reguły" : "Smart Rules";
                res["StrRuleEditorTitle"] = isPl ? "Edytor reguły" : "Rule Editor";
                res["StrRuleName"] = isPl ? "Nazwa reguły (opcjonalna)" : "Rule name (optional)";
                res["StrRuleProcess"] = isPl ? "PROCES" : "PROCESS";
                res["StrRuleTrigger"] = isPl ? "WYZWALACZ" : "TRIGGER";
                res["StrRuleAction"] = isPl ? "AKCJA" : "ACTION";
                res["StrRuleDelayS"] = isPl ? "Opóźnienie (s)" : "Delay (s)";
                res["StrRuleCooldownS"] = isPl ? "Cooldown (s)" : "Cooldown (s)";
                res["StrRulePriority"] = "PRIO";
                res["StrRuleProfile"] = isPl ? "Profil do zastosowania" : "Profile to apply";
                res["StrRuleRecent"] = isPl ? "Ostatnia aktywność reguł (dlaczego reguła zadziałała)" : "Recent rule activity (why this rule fired)";
                res["StrClearOverride"] = isPl ? "Wyczyść obejście" : "Clear override";
                res["StrDeleteRule"] = isPl ? "Usuń regułę" : "Delete rule";
                res["StrTriggerLaunch"] = isPl ? "Przy uruchomieniu procesu" : "On process launch";
                res["StrTriggerForeground"] = isPl ? "Na aktywnym oknie" : "On foreground window";
                res["StrActionApply"] = isPl ? "Zastosuj profil" : "Apply profile";
                res["StrActionRestore"] = isPl ? "Przywróć sesję" : "Restore session";
                res["StrPauseAutomation"] = isPl ? "Wstrzymaj automatyczną optymalizację" : "Pause automatic optimization";
                // EcoQoS
                res["StrEcoQos"] = isPl ? "EcoQoS (tryb wydajności)" : "EcoQoS (Efficiency mode)";
                res["StrEcoQosHint"] = isPl ? "Opcjonalne. Zaznaczone = żądaj włączenia EcoQoS, odznaczone = żądaj wyłączenia, nieustalone = nie zmieniaj. Nigdy nie włączane automatycznie; tylko przez jawny profil." : "Optional. Checked = request EcoQoS on, unchecked = request off, indeterminate = leave alone. Never auto-enabled; applies only via explicit profile.";
                // Live Insights + Diagnostics
                res["StrCpuHistory"] = isPl ? "Historia CPU" : "CPU history";
                res["StrMemHistory"] = isPl ? "Historia pamięci (zielony = working set, przerywana = prywatna)" : "Memory history (green = working set, dashed = private)";
                res["StrWhyThis"] = isPl ? "Dlaczego ten profil?" : "Why this profile?";
                res["StrExportDiagnostics"] = isPl ? "Eksport diagnostyki (ZIP, częściowo zanonimizowany)" : "Export diagnostics (ZIP, partially anonymized)";
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to apply localization: {ex.Message}");
            }
        }
    }
}
