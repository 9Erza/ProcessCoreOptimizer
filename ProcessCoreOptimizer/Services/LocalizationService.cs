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

                res["StrSystemProcesses"] = isPl ? "Procesy" : "Processes";
                res["StrSavedProfiles"] = isPl ? "Profile" : "Profiles";
                res["StrHardwareMonitor"] = isPl ? "Monitor sprzętu" : "Hardware monitor";
                res["StrSettings"] = isPl ? "Ustawienia" : "Settings";
                res["StrConsoleLog"] = isPl ? "Log aktywności" : "Activity log";
                res["StrActivity"] = isPl ? "Aktywność" : "Activity";
                res["StrSelectProcessHint"] = isPl ? "Wybierz proces, aby zobaczyć szczegóły i dostępne działania." : "Select a process to view details and available actions.";
                res["StrSelectedProcess"] = isPl ? "Wybrany proces" : "Selected process";
                res["StrInitialLoading"] = isPl ? "Ładowanie procesów…" : "Loading processes…";
                res["StrClosePanel"] = isPl ? "Zamknij panel" : "Close panel";
                res["StrSystemTargetWarning"] = isPl ? "Składnik Windows — zmiana ustawień CPU może zdestabilizować system. Reguły automatyczne nigdy go nie tykają; ręczny Apply pozostaje dostępny." : "Windows component — changing CPU settings may destabilize the system. Automatic rules never touch it; manual Apply remains available.";
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
                res["StrCpuUtility"] = isPl ? "Użycie CPU (z boostem)" : "CPU utility (incl. boost)";
                res["StrUtilityNote"] = isPl ? "Zawiera skalowanie częstotliwości; może przekraczać 100%. To nie to samo co czas zajętości." : "Includes frequency scaling; may exceed 100%. Not the same as busy time.";
                res["StrProcessList"] = isPl ? "Procesy" : "Processes";
                res["StrYourProfiles"] = isPl ? "Profile" : "Profiles";
                res["StrCpuUsage"] = isPl ? "Użycie CPU" : "CPU usage";
                res["StrCpuTemp"] = isPl ? "Temperatura CPU" : "CPU temperature";
                res["StrGpuUsage"] = isPl ? "Użycie GPU" : "GPU usage";
                res["StrGpuTemp"] = isPl ? "Temperatura GPU" : "GPU temperature";
                res["StrRamUsage"] = isPl ? "Użycie RAM" : "RAM usage";
                res["StrRamUsedAvail"] = isPl ? "RAM zajęty / łącznie" : "RAM used / total";
                res["StrExpandHardware"] = isPl ? "Szczegóły sprzętu" : "Hardware details";
                res["StrGpuDetails"] = "GPU";
                res["StrCpuDetails"] = "CPU";
                res["StrAppSettings"] = isPl ? "Ustawienia" : "Settings";
                res["StrLanguage"] = isPl ? "Język aplikacji" : "Language";
                res["StrStartWindows"] = isPl ? "Uruchamiaj z systemem Windows" : "Start with Windows";
                res["StrMinToTray"] = isPl ? "Minimalizuj do zasobnika" : "Minimize to tray";
                res["StrCloseToTray"] = isPl ? "Zamykaj do zasobnika" : "Close to tray";
                res["StrSetGroupGeneral"] = isPl ? "Ogólne" : "General";
                res["StrSetGroupOptimization"] = isPl ? "Optymalizacja" : "Optimization";
                res["StrSetGroupStartup"] = isPl ? "Uruchamianie i uprawnienia" : "Startup & privileges";
                res["StrSetGroupMonitoring"] = isPl ? "Monitorowanie procesów" : "Process Monitoring";
                res["StrSetGroupHardware"] = isPl ? "Monitor sprzętu" : "Hardware monitoring";
                res["StrSetGroupDiagnostics"] = isPl ? "Diagnostyka" : "Diagnostics";
                res["StrSetGroupUpdates"] = isPl ? "Aktualizacje" : "Updates";
                res["StrSetGroupData"] = isPl ? "Dane i reset" : "Data & reset";
                res["StrSetGroupAbout"] = isPl ? "O programie" : "About";
                res["StrStartupWindowBehavior"] = isPl ? "Zachowanie okna przy logowaniu" : "Window behavior on logon";
                res["StrStartupRunElevated"] = isPl ? "Uruchamiaj z prawami administratora przy starcie" : "Run with administrator rights at startup";
                res["StrStartupElevatedHint"] = isPl ? "Elevated startup używa zadania harmonogramu (HighestAvailable). Zwykły autostart używa wpisu rejestru Run. Aktywny jest zawsze tylko jeden kanał." : "Elevated startup uses a Task Scheduler logon task with HighestAvailable. Normal startup uses a registry Run entry. Only one channel is active at a time.";
                res["StrStartupStatus"] = isPl ? "Rzeczywisty stan autostartu" : "Actual startup state";
                res["StrStartupRefresh"] = isPl ? "Odśwież" : "Refresh";
                res["StrStartupWorking"] = isPl ? "Stosowanie…" : "Applying…";
                res["StrCurrentPrivileges"] = isPl ? "Aktualne uprawnienia" : "Current privileges";
                res["StrRestartAsAdmin"] = isPl ? "Uruchom ponownie jako administrator…" : "Restart as administrator…";
                res["StrTrayHint"] = isPl ? "Zachowanie tray dotyczy ręcznych przycisków minimalizacji/zamykania." : "Tray behavior applies to manual minimize/close buttons.";
                res["StrOpenWizard"] = isPl ? "Otwórz kreator pierwszego uruchomienia…" : "Open first-run wizard…";
                res["StrRestoreDefaults"] = isPl ? "Przywróć ustawienia domyślne" : "Restore default settings";
                res["StrFactoryResetTitle"] = isPl ? "Reset fabryczny — usuń wszystkie dane aplikacji" : "Factory reset — erase all application data";
                res["StrFactoryResetWarning"] = isPl ? "Usuwa ustawienia, profile, kopie, logi i cache PCO. Ręczne eksporty poza katalogiem danych zostają. Wpisz RESET, aby uzbroić." : "Deletes settings, profiles, backups, logs and caches owned by PCO. Exported files outside the data folder are kept. Type RESET to arm.";
                res["StrFactoryReset"] = isPl ? "Usuń wszystkie dane aplikacji" : "Erase all application data";
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
                res["StrBackgroundWatcherStatus"] = isPl ? "Watcher profili w tle jest aktywny" : "Background profile watcher is active";
                res["StrEnableStorageSensors"] = isPl ? "Włącz czujniki dysków (zaawansowane)" : "Enable storage sensors (advanced)";
                res["StrStorageSensorsWarn"] = isPl ? "Domyślnie wyłączone, aby monitor sprzętu nie odpytywał niepotrzebnie dysków." : "Disabled by default to avoid unnecessary disk polling.";
                res["StrCheckUpdates"] = isPl ? "Sprawdzaj aktualizacje przy starcie" : "Check for updates on startup";
                res["StrCheckUpdatesNow"] = isPl ? "Sprawdź aktualizacje" : "Check for updates";
                res["StrSetGroupGeneral"] = isPl ? "Ogólne" : "General";
                res["StrSetGroupOptimization"] = isPl ? "Optymalizacja" : "Optimization";
                res["StrCpuCores"] = isPl ? "Rdzenie CPU" : "CPU cores";
                res["StrSelectAll"] = isPl ? "Wszystkie" : "All";
                res["StrClearAll"] = isPl ? "Żadne" : "None";
                res["StrDisableECores"] = isPl ? "Bez E-core" : "No E-cores";
                res["StrEmptyMaskWarning"] = isPl ? "Nie wybrano rdzeni — nic się nie zmieni. Wybierz co najmniej jeden procesor logiczny." : "No cores selected — nothing would change. Select at least one logical processor.";
                res["StrNoData"] = isPl ? "Brak danych — licznik nie zwrócił jeszcze zweryfikowanej próbki." : "No data — counter has not returned a verified sample yet.";
                res["StrSetPriority"] = isPl ? "Priorytet" : "Priority";
                res["StrSetAffinity"] = isPl ? "Zastosuj" : "Apply";
                res["StrSaveProfile"] = isPl ? "Zapisz profil" : "Save profile";
                res["StrUpdateProfile"] = isPl ? "Aktualizuj profil" : "Update profile";
                res["StrDeleteProfile"] = isPl ? "Usuń profil" : "Delete profile";
                res["StrColName"] = isPl ? "Nazwa" : "Name";
                res["StrColInstances"] = "Inst.";
                res["StrColPriority"] = isPl ? "Priorytet" : "Priority";
                res["StrColRam"] = "RAM";
                res["StrColCpu"] = "CPU";
                res["StrColMode"] = isPl ? "Tryb" : "Mode";
                res["StrVramUsage"] = isPl ? "Zużycie VRAM" : "VRAM usage";
                res["StrCoreClock"] = isPl ? "Taktowanie rdzenia" : "Core clock";
                res["StrMemClock"] = isPl ? "Taktowanie pamięci" : "Memory clock";
                res["StrHotSpot"] = "Hot spot";
                res["StrVramTemp"] = isPl ? "Temperatura VRAM" : "VRAM temperature";
                res["StrPowerDraw"] = isPl ? "Pobór mocy" : "Power draw";
                res["StrAvgClock"] = isPl ? "Średnie taktowanie" : "Average clock";
                res["StrCoreTemps"] = isPl ? "Temperatury rdzeni" : "Core temperatures";
                res["StrOptimizationMode"] = isPl ? "Tryb" : "Mode";
                res["ModeAffinity"] = isPl ? "Koligacja (Affinity)" : "Affinity (Standard)";
                res["ModeCpuSets"] = isPl ? "Zestawy (CPU Sets)" : "CPU Sets (Smart)";
                res["StrNavCpuTopology"] = isPl ? "CPU i topologia" : "CPU & Topology";
                res["StrDiagnostics"] = isPl ? "Diagnostyka" : "Diagnostics";
                res["StrProcessFilter"] = isPl ? "Szukaj procesów" : "Search processes";
                res["StrProfileFilter"] = isPl ? "Szukaj profili" : "Search profiles";
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
                res["StrColStatus"] = "Status";
                res["StrUnknown"] = isPl ? "Nieznany" : "Unknown";
                res["StrApplyPreset"] = isPl ? "Zastosuj" : "Apply";
                res["StrRestoreSettings"] = isPl ? "Przywróć sesję" : "Restore session";
                res["StrDeveloper"] = isPl ? "Autor" : "Developer";
                res["StrSourceCode"] = isPl ? "Kod źródłowy" : "Source code";
                res["StrSupportDev"] = isPl ? "Wesprzyj rozwój" : "Support development";
                res["StrAuthorTooltip"] = isPl ? "GitHub autora — 9Erza" : "Developer GitHub — 9Erza";
                res["StrRepoTooltip"] = isPl ? "Repozytorium projektu — Process Core Optimizer" : "Project repository — Process Core Optimizer";
                res["StrSupportTooltip"] = isPl ? "Postaw kawę — wesprzyj rozwój" : "Buy Me a Coffee — support development";
                res["StrFooterRepoLabel"] = isPl ? "Repozytorium projektu" : "Project repository";
                res["StrFooterWebsiteTooltip"] = isPl ? "Strona autora — DobryPC.pl" : "Author website — DobryPC.pl";
                res["StrFooterSupportLabel"] = isPl ? "Wesprzyj projekt" : "Support the project";
                res["StrWebsiteCaption"] = isPl ? "Strona internetowa" : "Website";
                res["StrLinkOpenFailed"] = isPl ? "Nie udało się otworzyć strony w przeglądarce." : "Could not open the link in your browser.";
                // Process Explorer 2.0 (grouped/instance views, filters, context menu)
                res["StrViewGrouped"] = isPl ? "Grupy" : "Grouped";
                res["StrViewInstances"] = isPl ? "Instancje" : "Instances";
                res["StrOnlyOptimized"] = isPl ? "Tylko zoptymalizowane" : "Only optimized";
                res["StrOnlyWithProfile"] = isPl ? "Z profilem" : "With profile";
                res["StrColPid"] = "PID";
                res["StrColPath"] = isPl ? "Ścieżka" : "Path";
                res["StrColProfile"] = isPl ? "Profil" : "Profile";
                res["StrCtxDetails"] = isPl ? "Szczegóły" : "View details";
                res["StrCtxCreateProfile"] = isPl ? "Utwórz profil" : "Create profile";
                res["StrCtxCapture"] = isPl ? "Przechwyć ustawienia" : "Capture current settings";
                res["StrCtxPreview"] = isPl ? "Podgląd zmian" : "Preview changes";
                res["StrCtxApply"] = isPl ? "Zastosuj" : "Apply";
                res["StrCtxRestore"] = isPl ? "Przywróć ustawienia sesji" : "Restore session settings";
                res["StrCtxCopyPid"] = isPl ? "Kopiuj PID" : "Copy PID";
                res["StrCtxCopyPath"] = isPl ? "Kopiuj ścieżkę EXE" : "Copy EXE path";
                res["StrCtxOpenLocation"] = isPl ? "Otwórz lokalizację pliku" : "Open file location";
                // Profile Studio editor
                res["StrProfileEditorTitle"] = isPl ? "Edytor profilu" : "Profile editor";
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
                res["StrNewProfile"] = isPl ? "Nowy profil" : "New profile";
                res["StrEditProfile"] = isPl ? "Edytuj" : "Edit";
                res["StrCtxToggleEnabled"] = isPl ? "Włącz / wyłącz" : "Enable / disable";
                // Smart Rules
                res["StrSmartRules"] = isPl ? "Reguły" : "Smart rules";
                res["StrRuleEditorTitle"] = isPl ? "Edytor reguły" : "Rule editor";
                res["StrRuleName"] = isPl ? "Nazwa reguły (opcjonalna)" : "Rule name (optional)";
                res["StrRuleProcess"] = isPl ? "Proces" : "Process";
                res["StrRuleTrigger"] = isPl ? "Wyzwalacz" : "Trigger";
                res["StrRuleAction"] = isPl ? "Akcja" : "Action";
                res["StrRuleDelayS"] = isPl ? "Opóźnienie (s)" : "Delay (s)";
                res["StrRuleCooldownS"] = isPl ? "Cooldown (s)" : "Cooldown (s)";
                res["StrRulePriority"] = isPl ? "Priorytet" : "Priority";
                res["StrRuleProfile"] = isPl ? "Profil do zastosowania" : "Profile to apply";
                res["StrRuleRecent"] = isPl ? "Ostatnia aktywność reguł" : "Recent rule activity";
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
                res["StrMemHistory"] = isPl ? "Pamięć: linia ciągła = working set, przerywana = prywatna" : "Memory: solid = working set, dashed = private";
                res["StrWhyThis"] = isPl ? "Dlaczego ten profil?" : "Why this profile?";
                res["StrExportDiagnostics"] = isPl ? "Eksportuj diagnostykę (ZIP)" : "Export diagnostics (ZIP)";
                // 2.1 interface redesign
                res["StrPageProcessesSub"] = isPl ? "Uruchomione aplikacje na żywo. Wybierz jedną, aby dobrać jej rdzenie i priorytet." : "Running applications, live. Select one to tune its cores and priority.";
                res["StrPageCpuSub"] = isPl ? "Układ procesora i gotowe presety rdzeni. Tylko podgląd — tutaj nic nie jest zmieniane." : "Your processor layout and core presets. Preview only — nothing is changed here.";
                res["StrPageProfilesSub"] = isPl ? "Zapisane ustawienia stosowane automatycznie, gdy aplikacja się uruchamia." : "Saved settings applied automatically when an application starts.";
                res["StrPageRulesSub"] = isPl ? "Stosuj profile, gdy proces startuje lub trafia na pierwszy plan." : "Apply profiles when a process launches or comes to the foreground.";
                res["StrPageHardwareSub"] = isPl ? "Czujniki na żywo. Domyślnie wyłączone, aby oszczędzać zasoby." : "Live sensors. Off by default to save resources.";
                res["StrPageDiagnosticsSub"] = isPl ? "Watcher w tle, konflikty profili, ostatnie operacje i pełny log." : "Background watcher, profile conflicts, recent operations and the full log.";
                res["StrPageSettingsSub"] = isPl ? "Preferencje, autostart, aktualizacje i dane aplikacji." : "Preferences, startup, updates and application data.";
                res["StrDetailPath"] = isPl ? "Ścieżka" : "Path";
                res["StrDetailCpu"] = "CPU";
                res["StrDetailProfile"] = isPl ? "Profil" : "Profile";
                res["StrDetailLast"] = isPl ? "Ostatnio" : "Last action";
                res["StrStatLogical"] = isPl ? "Procesory logiczne" : "Logical processors";
                res["StrStatPhysical"] = isPl ? "Rdzenie fizyczne" : "Physical cores";
                res["StrStatSmt"] = isPl ? "Wątki SMT" : "SMT threads";
                res["StrStatPCores"] = isPl ? "Rdzenie P" : "P-cores";
                res["StrStatECores"] = isPl ? "Rdzenie E" : "E-cores";
                res["StrStatAffinityRange"] = isPl ? "Zakres affinity" : "Affinity range";
                res["StrNewRule"] = isPl ? "Nowa reguła" : "New rule";
                res["StrColMessage"] = isPl ? "Komunikat" : "Message";
                res["StrStartupRepair"] = isPl ? "Napraw" : "Repair";
                res["StrStartupRepairHint"] = isPl ? "Ponownie zapisuje wpis autostartu (np. gdy został wyłączony w Menedżerze zadań → Aplikacje autostartu)." : "Re-applies the startup entry (e.g. when it was disabled in Task Manager → Startup apps).";
                res["StrWinMinimize"] = isPl ? "Minimalizuj" : "Minimize";
                res["StrWinMaximize"] = isPl ? "Maksymalizuj" : "Maximize";
                res["StrWinRestore"] = isPl ? "Przywróć" : "Restore down";
                res["StrWinClose"] = isPl ? "Zamknij" : "Close";
                res["StrOpFullyApplied"] = isPl ? "Zastosowano" : "Applied";
                res["StrOpPartiallyApplied"] = isPl ? "Częściowo" : "Partially applied";
                res["StrOpSkippedSuspended"] = isPl ? "Wstrzymano" : "Suspended";
                res["StrOpFailed"] = isPl ? "Błąd" : "Failed";
                res["StrOpSkippedAlreadyApplied"] = isPl ? "Już zastosowane" : "Already applied";
                res["StrOpSkippedIdentityMismatch"] = isPl ? "Inny proces (PID)" : "Identity mismatch";
                res["StrOpSkippedProtected"] = isPl ? "Proces chroniony" : "Protected process";
                res["StrOpSkippedSystemLocation"] = isPl ? "Składnik Windows" : "Windows component";
                res["StrOpSkippedNoPermission"] = isPl ? "Brak uprawnień" : "No permission";
                res["StrOpUnsupported"] = isPl ? "Nieobsługiwane" : "Unsupported";
                res["StrOpCancelled"] = isPl ? "Anulowano" : "Cancelled";
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to apply localization: {ex.Message}");
            }
        }
    }
}
