using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProcessCoreOptimizer.WPF.ViewModels
{
    /// <summary>Sidebar mini-console status dot. Error = startup conflict needs repair.</summary>
    public enum ActivityLevel
    {
        Active,
        Attention,
        Error
    }

    /// <summary>Manual/automatic update check outcome shown in Settings.</summary>
    public enum UpdateStatusKind
    {
        None,
        Checking,
        UpToDate,
        Failed,
        NoInstaller,
        Available
    }
    /// <summary>
    /// Main view model coordinating UI state, process telemetry, profiles and settings.
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private HardwareMonitorService? _hwService;
        private readonly SettingsService _settingsService = new();
        private readonly ProcessService _processService = new();
        private readonly HardwareService _hardwareService = new();
        private readonly ProfileService _profileService = new();
        private readonly ProcessScannerService _processScannerService;
        private readonly OptimizationService _optimizationService;
        private readonly ProfileWatcherService _profileWatcher;
        private readonly AppliedOperationHistory _operationHistory = new();
        private readonly SessionRestoreService _sessionRestore = new();
        private readonly LocalizationService _localizationService = new();
        private readonly UpdateService _updateService = new();
        private readonly UpdateCheckSession _updateSession;
        private readonly System.Threading.CancellationTokenSource _updateCts = new();
        private bool _updateDialogShown;
        private string? _pendingUpdateVersion;

        private readonly DispatcherTimer _hwTimer;
        private readonly DispatcherTimer _refreshTimer;

        private AppSettings _appSettings;
        private Dictionary<int, uint> _cpuSetMap = new();
        private List<ProcessProfile> _profiles = new();
        private readonly Dictionary<string, DateTime> _lastOptimizationDiagnosticLogUtc = new();
        private string? _cachedCpuVendor;
        private bool _realtimeCacheFlag;
        private string _lastStartupSignature = string.Empty;
        private int _startupApplyInFlight;
        private readonly StartupService _startupService = new();
        private readonly StartupApplyCoordinator _startupCoordinator;
        private string _startupStatusText = string.Empty;
        private string _startupDetailText = string.Empty;
        private bool _startupBusy;
        private bool _isElevated;

        private bool _isRefreshing;
        private bool _firstRefreshDone;
        private bool _disposed;

        private double _monCpuTemp;
        private double _monGpuTemp;
        private double _monGpuLoad;
        private double _monRamUsage;

        private ProcessItem? _selectedProcess;
        private ProcessProfile? _selectedSavedProfile;
        private ProcessDetails? _selectedProcessDetails;
        private string _selectedPriority = "Normal";
        private OptimizationMode _selectedOptimizationMode = OptimizationMode.Affinity;
        private string _activeTab = "Processes";
        private string _processFilterText = string.Empty;
        private string _profileFilterText = string.Empty;
        private bool _processOnlyOptimized;
        private bool _processOnlyWithProfile;
        private ProcessViewMode _processViewMode = ProcessViewMode.Grouped;
        private ProcessInstanceRow? _selectedInstanceRow;
        private ProcessScanResult? _lastUiScan;
        private readonly SmartRuleService _ruleService = new();
        private List<SmartRule> _rules = new();
        private List<SmartRule> _rulesSnapshot = new();
        private readonly SmartRuleEngine _ruleEngine = new();
        private readonly ProcessInsightsSampler _insightsSampler = new();
        private readonly Dictionary<string, DateTime> _lastRuleLogUtc = new(StringComparer.OrdinalIgnoreCase);
        private string? _topologyLimitation;

        private Visibility _viewProcesses = Visibility.Visible;
        private Visibility _viewProfiles = Visibility.Collapsed;
        private Visibility _viewCpu = Visibility.Collapsed;
        private Visibility _viewHardwareMonitor = Visibility.Collapsed;
        private Visibility _viewSettings = Visibility.Collapsed;
        private Visibility _viewDiagnostics = Visibility.Collapsed;
        private Visibility _viewSmartRules = Visibility.Collapsed;
        private string _watcherStatusText = string.Empty;
        private string _conflictSummaryText = string.Empty;
        private HardwareMetrics _fullMetrics = new();

        public ObservableCollection<ProcessItem> Processes { get; } = new();
        public ObservableCollection<ProcessInstanceRow> InstanceRows { get; } = new();
        public ObservableCollection<SmartRule> Rules { get; } = new();
        public ObservableCollection<RuleDecision> RecentRuleDecisions { get; } = new();
        private PointCollection _cpuHistoryPoints = new();
        private PointCollection _memWsHistoryPoints = new();
        private PointCollection _memPrivateHistoryPoints = new();
        private string _insightsSummary = string.Empty;
        private bool _hasInsights;

        public PointCollection CpuHistoryPoints
        {
            get => _cpuHistoryPoints;
            private set => SetProperty(ref _cpuHistoryPoints, value);
        }

        public PointCollection MemWsHistoryPoints
        {
            get => _memWsHistoryPoints;
            private set => SetProperty(ref _memWsHistoryPoints, value);
        }

        public PointCollection MemPrivateHistoryPoints
        {
            get => _memPrivateHistoryPoints;
            private set => SetProperty(ref _memPrivateHistoryPoints, value);
        }

        public string InsightsSummary
        {
            get => _insightsSummary;
            private set => SetProperty(ref _insightsSummary, value);
        }

        public bool HasInsights
        {
            get => _hasInsights;
            private set => SetProperty(ref _hasInsights, value);
        }

        private string _profileMatchWhy = string.Empty;

        public string ProfileMatchWhy
        {
            get => _profileMatchWhy;
            private set => SetProperty(ref _profileMatchWhy, value);
        }

        public ICommand ExportDiagnosticsCommand { get; }
        private SmartRule? _selectedRule;

        public SmartRule? SelectedRule
        {
            get => _selectedRule;
            set
            {
                if (!SetProperty(ref _selectedRule, value)) return;
                OnPropertyChanged(nameof(HasSelectedRule));
            }
        }

        public bool HasRules => Rules.Count > 0;
        public bool HasSelectedRule => SelectedRule != null;
        private string _ruleStatusText = string.Empty;

        public string RuleStatusText
        {
            get => _ruleStatusText;
            private set => SetProperty(ref _ruleStatusText, value);
        }

        /// <summary>
        /// Global pause for all automatic optimization (watcher + smart rules).
        /// Observation continues; mutations stop. Persisted as user intent.
        /// </summary>
        public bool AutomationPaused
        {
            get => AppSettings.AutomationPaused;
            set
            {
                if (AppSettings.AutomationPaused == value) return;
                AppSettings.AutomationPaused = value;
                _settingsService.SaveSettings(AppSettings);
                OnPropertyChanged(nameof(AutomationPaused));
                OnPropertyChanged(nameof(ActivityStatus));
                RefreshRuleStatus();
                AddLog(value
                    ? (IsPolishLanguage ? "Automatyczna optymalizacja wstrzymana (pauza). Obserwacja trwa." : "Automatic optimization paused. Observation continues.")
                    : (IsPolishLanguage ? "Automatyczna optymalizacja wznowiona." : "Automatic optimization resumed."));
            }
        }
        public ObservableCollection<CoreInfo> Cores { get; } = new();
        public ObservableCollection<string> ActionLogs { get; } = new();
        public ObservableCollection<ProcessProfile> SavedProfiles { get; } = new();
        public ObservableCollection<string> AvailablePriorities { get; } = new();
        public ObservableCollection<CorePreset> CorePresets { get; } = new();
        public ObservableCollection<ProfileConflict> ProfileConflicts { get; } = new();
        public ObservableCollection<AppliedOperation> RecentOperations { get; } = new();

        public ObservableCollection<OptimizationMode> AvailableOptimizationModes { get; } = new()
        {
            OptimizationMode.Affinity,
            OptimizationMode.CpuSets
        };

        /// <summary>Sidebar mini-console status, derived from real app state (never animated).</summary>
        public ActivityLevel ActivityStatus =>
            _hasStartupConflict ? ActivityLevel.Error
            : (IsSafeMode || AppSettings.AutomationPaused) ? ActivityLevel.Attention
            : ActivityLevel.Active;

        /// <summary>Most recent log line for the mini console (full history stays in Diagnostics).</summary>
        public string ActivitySummary => ActionLogs.Count == 0
            ? (IsPolishLanguage ? "Brak aktywności." : "No activity yet.")
            : ActionLogs[ActionLogs.Count - 1];

        private bool _processListLoading = true;
        /// <summary>True until the first process scan populates the list (loading hint, no fake empty state).</summary>
        public bool IsProcessListLoading
        {
            get => _processListLoading;
            private set => SetProperty(ref _processListLoading, value);
        }

        private bool _hasStartupConflict;

        public List<string> Languages { get; } = new() { "English", "Polski" };

        public ICommand SwitchTabCommand { get; }
        public ICommand ApplySettingsCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand UpdateProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand DisableSmtCommand { get; }
        public ICommand DisableECoresCommand { get; }
        public ICommand ToggleSettingCommand { get; }
        public ICommand RestartAsAdministratorCommand { get; }
        public ICommand RefreshStartupStatusCommand { get; }
        /// <summary>Re-applies the desired startup registration (plan → execute → verify).</summary>
        public ICommand RepairStartupCommand { get; }
        public string? ExternalLinkErrorText
        {
            get => _externalLinkErrorText;
            private set => SetProperty(ref _externalLinkErrorText, value);
        }

        public ICommand OpenWizardCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand FactoryResetCommand { get; }
        public ICommand CheckForUpdatesNowCommand { get; }

        private string? _updateStatusVersion;
        private UpdateStatusKind _updateStatus;
        public string? UpdateStatusText => _updateStatus switch
        {
            UpdateStatusKind.Checking => IsPolishLanguage ? "Sprawdzanie aktualizacji…" : "Checking for updates…",
            UpdateStatusKind.UpToDate => IsPolishLanguage ? "Program aktualny." : "App is up to date.",
            UpdateStatusKind.Failed => IsPolishLanguage ? "Nie udało się sprawdzić aktualizacji. Sprawdź połączenie i spróbuj ponownie." : "Could not check for updates. Check your connection and try again.",
            UpdateStatusKind.NoInstaller => IsPolishLanguage ? "Nowa wersja nie ma jeszcze opublikowanego instalatora." : "The new version has no published installer yet.",
            UpdateStatusKind.Available => string.Format(IsPolishLanguage ? "Dostępna aktualizacja: v{0}" : "Update available: v{0}", _updateStatusVersion ?? string.Empty),
            _ => null
        };

        private void SetUpdateStatus(UpdateStatusKind kind, string? version = null)
        {
            _updateStatus = kind;
            _updateStatusVersion = version;
            OnPropertyChanged(nameof(UpdateStatusText));
        }
        public ICommand OpenAuthorCommand { get; }
        public ICommand OpenRepositoryCommand { get; }
        public ICommand OpenWebsiteCommand { get; }
        public ICommand OpenStudioCommand { get; }
        public ICommand OpenSupportCommand { get; }
        public ICommand OpenAboutCommand { get; }

        /// <summary>
        /// Raised when the footer "About" row is invoked. The view switches to
        /// Settings and scrolls to the existing About section (no new window,
        /// no browser).
        /// </summary>
        public event EventHandler? AboutRequested;

        private string _dataResetStatusText = string.Empty;
        private string _factoryResetConfirmationText = string.Empty;
        private string? _externalLinkErrorText;
        public ICommand ApplyCorePresetCommand { get; }
        public ICommand DuplicateProfileCommand { get; }
        public ICommand ExportProfilesCommand { get; }
        public ICommand ImportProfilesCommand { get; }
        public ICommand CaptureLiveSettingsCommand { get; }
        public ICommand RestoreSettingsCommand { get; }
        public ICommand CopyPidCommand { get; }
        public ICommand CopyExePathCommand { get; }
        public ICommand OpenFileLocationCommand { get; }
        public ICommand PreviewChangesCommand { get; }
        public ICommand RefreshDetailsCommand { get; }
        public ICommand CreateProfileFromInstanceCommand { get; }
        public ICommand ApplyToInstanceCommand { get; }
        public ICommand NewProfileCommand { get; }
        public ICommand EditProfileCommand { get; }
        public ICommand ToggleProfileEnabledCommand { get; }
        public ICommand NewRuleCommand { get; }
        public ICommand EditRuleCommand { get; }
        public ICommand DeleteRuleCommand { get; }
        public ICommand ToggleRuleEnabledCommand { get; }
        public ICommand ClearRuleOverrideCommand { get; }

        public string AppVersion { get; } = FormatDisplayVersion(Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "2.1.0");

        private static string FormatDisplayVersion(string informationalVersion)
        {
            // InformationalVersion carries build metadata ("2.0.0+sha"); the UI
            // and tray show the clean version, the updater parses it strictly.
            string v = (informationalVersion ?? string.Empty).Trim().TrimStart('v', 'V');
            int plus = v.IndexOf('+');
            return (plus >= 0 ? v.Substring(0, plus) : v).Trim();
        }

        /// <summary>Compact footer title (sidebar is 230 DIP; full name lives in the tooltip + About).</summary>
        public string FooterTitle => $"PCO {AppVersion}";

        public AppSettings AppSettings
        {
            get => _appSettings;
            set => SetProperty(ref _appSettings, value);
        }

        public HardwareMetrics FullMetrics
        {
            get => _fullMetrics;
            set => SetProperty(ref _fullMetrics, value);
        }

        public double MonCpuTemp { get => _monCpuTemp; set => SetProperty(ref _monCpuTemp, value); }
        public double MonGpuTemp { get => _monGpuTemp; set => SetProperty(ref _monGpuTemp, value); }
        public double MonGpuLoad { get => _monGpuLoad; set => SetProperty(ref _monGpuLoad, value); }
        public double MonRamUsage { get => _monRamUsage; set => SetProperty(ref _monRamUsage, value); }

        public string ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }
        public Visibility ViewProcesses { get => _viewProcesses; set => SetProperty(ref _viewProcesses, value); }
        public Visibility ViewProfiles { get => _viewProfiles; set => SetProperty(ref _viewProfiles, value); }
        public Visibility ViewCpu { get => _viewCpu; set => SetProperty(ref _viewCpu, value); }
        public Visibility ViewHardwareMonitor { get => _viewHardwareMonitor; set => SetProperty(ref _viewHardwareMonitor, value); }
        public Visibility ViewSettings { get => _viewSettings; set => SetProperty(ref _viewSettings, value); }
        public Visibility ViewDiagnostics { get => _viewDiagnostics; set => SetProperty(ref _viewDiagnostics, value); }
        public Visibility ViewSmartRules { get => _viewSmartRules; set => SetProperty(ref _viewSmartRules, value); }

        public bool IsRightPanelVisible => ActiveTab is "Processes" or "Profiles" or "Cpu";

        public string WatcherStatusText
        {
            get => _watcherStatusText;
            private set => SetProperty(ref _watcherStatusText, value);
        }

        public string ConflictSummaryText
        {
            get => _conflictSummaryText;
            private set => SetProperty(ref _conflictSummaryText, value);
        }

        public bool HasProfileConflicts => ProfileConflicts.Count > 0;

        public bool IsHardwareMonitorEnabled
        {
            get => AppSettings.HardwareMonitorEnabled;
            set
            {
                if (AppSettings.HardwareMonitorEnabled == value) return;

                AppSettings.HardwareMonitorEnabled = value;
                OnPropertyChanged();
                _settingsService.SaveSettings(AppSettings);

                if (value)
                {
                    StartHardwareMonitorIfVisible();
                }
                else
                {
                    StopHardwareMonitor(closeSensors: true, resetMetrics: true);
                }
            }
        }

        public bool IsHardwareMonitorRunning => _hwService?.IsInitialized == true && _hwTimer.IsEnabled;

        public bool HasECores => Cores.Any(c => c.IsECore);

        public OptimizationMode SelectedOptimizationMode
        {
            get => _selectedOptimizationMode;
            set => SetProperty(ref _selectedOptimizationMode, NormalizeOptimizationMode(value));
        }

        public string SelectedPriority
        {
            get => _selectedPriority;
            set
            {
                // The combo box only edits the pending value. The actual priority change
                // happens via Apply ("Set Affinity") or profile save/update, never by
                // merely browsing the drop-down. This avoids accidental mutation of
                // every running instance of the selected process.
                string translatedValue = TranslatePriority(value);
                SetProperty(ref _selectedPriority, translatedValue);
            }
        }

        public ProcessDetails? SelectedProcessDetails
        {
            get => _selectedProcessDetails;
            private set
            {
                if (SetProperty(ref _selectedProcessDetails, value))
                    OnPropertyChanged(nameof(IsSystemTarget));
            }
        }

        public string ProcessFilterText
        {
            get => _processFilterText;
            set
            {
                if (!SetProperty(ref _processFilterText, value ?? string.Empty)) return;
                RefreshProcessFilter();
            }
        }

        public bool ProcessOnlyOptimized
        {
            get => _processOnlyOptimized;
            set
            {
                if (!SetProperty(ref _processOnlyOptimized, value)) return;
                RefreshProcessFilter();
            }
        }

        public bool ProcessOnlyWithProfile
        {
            get => _processOnlyWithProfile;
            set
            {
                if (!SetProperty(ref _processOnlyWithProfile, value)) return;
                RefreshProcessFilter();
            }
        }

        public ProcessViewMode ProcessViewMode
        {
            get => _processViewMode;
            set
            {
                if (!SetProperty(ref _processViewMode, value)) return;
                // Grouped <-> Instances switches the row identity space:
                // drop the old selection so no stale target survives.
                SelectedProcess = null;
                SelectedInstanceRow = null;
                OnPropertyChanged(nameof(IsInstanceView));
                RebuildInstanceRows();
            }
        }

        public bool IsInstanceView => _processViewMode == ProcessViewMode.Instances;

        /// <summary>Two-way toggle for the Grouped/Instances switch (XAML CheckBox/RadioButton).</summary>
        public bool ShowInstances
        {
            get => IsInstanceView;
            set => ProcessViewMode = value ? ProcessViewMode.Instances : ProcessViewMode.Grouped;
        }

        public ProcessInstanceRow? SelectedInstanceRow
        {
            get => _selectedInstanceRow;
            set
            {
                if (!SetProperty(ref _selectedInstanceRow, value)) return;
                _insightsSampler.Reset();
                if (value != null)
                {
                    SelectedProcessDetails = BuildProcessDetails(value.Name);
                    UpdateProfileWhy();
                }
                else
                {
                    SelectedProcessDetails = null;
                    UpdateProfileWhy();
                }
                OnPropertyChanged(nameof(HasProcessTarget));
                OnPropertyChanged(nameof(TargetScopeLabel));
            }
        }

        public string ProfileFilterText
        {
            get => _profileFilterText;
            set
            {
                if (!SetProperty(ref _profileFilterText, value ?? string.Empty)) return;
                RefreshSavedProfilesView();
            }
        }

        public string? TopologyLimitation
        {
            get => _topologyLimitation;
            private set => SetProperty(ref _topologyLimitation, value);
        }

        /// <summary>True when a live process/instance row is selected (right CPU panel may show).</summary>
        public bool HasProcessTarget => SelectedProcess != null || SelectedInstanceRow != null;

        /// <summary>True when the current target runs from the Windows directory (caution shown, manual only).</summary>
        public bool IsSystemTarget =>
            ProcessService.IsSystemComponentPath(SelectedProcessDetails?.ExecutablePath);

        /// <summary>Explicit mutation scope label: group with instance count or single PID.</summary>
        private static string PolishInstanceNoun(int n)
        {
            if (n == 1) return "instancja";
            int lastTwo = n % 100;
            int last = n % 10;
            if (last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14)) return "instancje";
            return "instancji";
        }

        public string TargetScopeLabel
        {
            get
            {
                if (SelectedInstanceRow != null)
                {
                    return IsPolishLanguage
                        ? $"Instancja — PID {SelectedInstanceRow.ProcessId}"
                        : $"Instance — PID {SelectedInstanceRow.ProcessId}";
                }
                if (SelectedProcess != null && SelectedProcessDetails != null)
                {
                    int n = SelectedProcessDetails.InstanceCount;
                    return IsPolishLanguage
                        ? $"Grupa procesów — {n} {PolishInstanceNoun(n)}"
                        : $"Process group — {n} {(n == 1 ? "instance" : "instances")}";
                }
                if (SelectedSavedProfile != null)
                {
                    return IsPolishLanguage
                        ? $"Profil: {SelectedSavedProfile.ProcessName}"
                        : $"Profile: {SelectedSavedProfile.ProcessName}";
                }
                return string.Empty;
            }
        }

        /// <summary>Right-panel header follows the target kind (process vs profile).</summary>
        public string TargetPanelTitle => ActiveTab == "Profiles"
            ? (IsPolishLanguage ? "Wybrany profil" : "Selected profile")
            : (IsPolishLanguage ? "Wybrany proces" : "Selected process");

        public ICommand CloseProcessTargetCommand { get; }

        /// <summary>True when at least one saved profile exists.</summary>
        public bool HasSavedProfiles => SavedProfiles.Count > 0;

        /// <summary>True when a saved profile is selected (right CPU panel may show).</summary>
        public bool HasSavedProfileTarget => SelectedSavedProfile != null;

        public int LogicalProcessorCount => Cores.Count;
        public int PhysicalCoreCount => Cores.Count(c => !c.IsThread);
        public int SmtThreadCount => Cores.Count(c => c.IsThread);
        public int PerformanceCoreCount => Cores.Count(c => !c.IsECore && !c.IsThread && !c.IsClassificationUnknown);
        public int EfficiencyCoreCount => Cores.Count(c => c.IsECore);
        public bool TopologyIsUnknown => Cores.Count > 0 && Cores.All(c => c.IsClassificationUnknown);
        public string AffinityRangeText => Cores.Count == 0 ? "—" : $"0–{Cores.Count - 1}";

        /// <summary>Preview-only preset selection for the informational CPU tab (never mutates).</summary>
        public string? PreviewPresetId
        {
            get => _previewPresetId;
            private set => SetProperty(ref _previewPresetId, value);
        }
        private string? _previewPresetId;

        public System.Collections.Generic.IReadOnlyList<int> PreviewLogicalProcessors { get; private set; } = System.Array.Empty<int>();

        public ICommand PreviewCorePresetCommand { get; private set; } = null!;

        private bool _narrowLayout;
        public bool IsNarrowLayout
        {
            get => _narrowLayout;
            private set => SetProperty(ref _narrowLayout, value);
        }

        /// <summary>Called from the view on window resize (narrow = overlay drawer mode).</summary>
        public void SetNarrowLayout(bool narrow) => IsNarrowLayout = narrow;

        /// <summary>Active Settings category (pure navigation state, never persisted).</summary>
        public string ActiveSettingsCategory
        {
            get => _activeSettingsCategory;
            set => SetProperty(ref _activeSettingsCategory, string.IsNullOrWhiteSpace(value) ? "General" : value);
        }
        private string _activeSettingsCategory = "General";

        /// <summary>Compact mask summary for the core selector ("Selected: 6 / 8 logical processors").</summary>
        public string SelectedCoreSummary
        {
            get
            {
                int total = Cores.Count;
                int selected = 0;
                foreach (var c in Cores) if (c.IsChecked) selected++;
                return IsPolishLanguage
                    ? $"Wybrano: {selected} z {total} procesorów logicznych"
                    : $"Selected: {selected} / {total} logical processors";
            }
        }

        public bool IsCoreMaskEmpty
        {
            get
            {
                foreach (var c in Cores) if (c.IsChecked) return false;
                return true;
            }
        }

        public ProcessItem? SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (!SetProperty(ref _selectedProcess, value)) return;
                if (value == null)
                {
                    // Stale target invalidated (process exited / row removed / user cleared):
                    // never keep a mutation context without a live selection.
                    SelectedProcessDetails = null;
                    UpdateProfileWhy();
                    OnPropertyChanged(nameof(HasProcessTarget));
                    OnPropertyChanged(nameof(TargetScopeLabel));
                    return;
                }
                _insightsSampler.Reset();
                SelectedProcessDetails = BuildProcessDetails(value.Name);
                UpdateProfileWhy();
                OnPropertyChanged(nameof(HasProcessTarget));
                OnPropertyChanged(nameof(TargetScopeLabel));

                // Same precedence as the engine: enabled first, path-bound (matching
                // this EXE) before name-only, newest wins.
                var savedProfile = ResolveProfileForUi(value.Name, value.ExecutablePath);
                if (savedProfile != null)
                {
                    string effectivePriority = NormalizePriority(savedProfile.Priority);
                    _selectedPriority = TranslatePriority(effectivePriority);
                    OnPropertyChanged(nameof(SelectedPriority));

                    SelectedOptimizationMode = NormalizeOptimizationMode(savedProfile.OptimizationMode);
                    // Priority-only profiles carry mask 0: show the live cores
                    // instead of unchecking everything (which blocked Apply/Save).
                    if (savedProfile.ApplyCoreOptimization && savedProfile.AffinityMask != 0)
                        ApplyMaskToCoreSelection(savedProfile.AffinityMask);
                    else
                        UpdateCoreSelectionFromAffinity(value.Name);
                    return;
                }

                _selectedPriority = value.Priority;
                OnPropertyChanged(nameof(SelectedPriority));
                SelectedOptimizationMode = OptimizationMode.Affinity;
                UpdateCoreSelectionFromAffinity(value.Name);
                OnPropertyChanged(nameof(HasProcessTarget));
                OnPropertyChanged(nameof(TargetScopeLabel));
            }
        }

        public ProcessProfile? SelectedSavedProfile
        {
            get => _selectedSavedProfile;
            set
            {
                if (!SetProperty(ref _selectedSavedProfile, value))
                {
                    OnPropertyChanged(nameof(HasSavedProfileTarget));
                    return;
                }
                if (value == null)
                {
                    OnPropertyChanged(nameof(HasSavedProfileTarget));
                    return;
                }

                value.Priority = NormalizePriority(value.Priority, allowRealtime: true);
                string effectivePriority = NormalizePriority(value.Priority);
                value.DisplayPriority = TranslatePriority(effectivePriority);
                value.OptimizationMode = NormalizeOptimizationMode(value.OptimizationMode);

                _selectedPriority = TranslatePriority(effectivePriority);
                OnPropertyChanged(nameof(SelectedPriority));

                SelectedOptimizationMode = value.OptimizationMode;
                if (value.ApplyCoreOptimization && value.AffinityMask != 0)
                    ApplyMaskToCoreSelection(value.AffinityMask);
                else
                    SetAllCores(true);
                OnPropertyChanged(nameof(HasSavedProfileTarget));
            }
        }

        public bool IsPolishLanguage
        {
            get => AppSettings.Language == "pl";
            set
            {
                string code = value ? "pl" : "en";
                if (AppSettings.Language == code) return;

                AppSettings.Language = code;
                ApplyLanguage(code);
                SaveAndApplySettings(restartAsAdminIfNeeded: false);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedLanguageName));
                OnPropertyChanged(nameof(CpuVendorText));
            }
        }

        public string SelectedLanguageName
        {
            get => AppSettings.Language == "pl" ? "Polski" : "English";
            set => IsPolishLanguage = value == "Polski";
        }

        public bool NeedsFirstRunWizard { get; private set; }

        private void ResolveOnboardingState(bool hadSettings, bool hadProfiles)
        {
            var (needsWizard, newMarker, save) = OnboardingResolver.Resolve(hadSettings, hadProfiles, _appSettings.OnboardingCompletedVersion);
            NeedsFirstRunWizard = needsWizard;
            if (save && newMarker.HasValue)
            {
                _appSettings.OnboardingCompletedVersion = newMarker.Value;
                _settingsService.SaveSettings(_appSettings);
            }
        }

        public void ReloadSettingsFromDisk()
        {
            _appSettings = _settingsService.LoadSettings();
            ConfigureLoggerFromSettings();
            NeedsFirstRunWizard = false;
            ApplyLanguage(_appSettings.Language);
            RefreshSavedProfilesView();
            OnPropertyChanged(nameof(SelectedLanguageName));
            OnPropertyChanged(nameof(CpuVendorText));
            OnPropertyChanged(nameof(AppSettings));
            OnPropertyChanged(nameof(SelectedStartupWindowMode));

            // The first-run wizard writes startup choices straight to disk. They
            // were never registered with Windows (only the status was refreshed),
            // so "Start with Windows" looked enabled but nothing launched at logon.
            string signature = StartupSignature(_appSettings);
            if (!string.Equals(signature, _lastStartupSignature, StringComparison.Ordinal))
            {
                _lastStartupSignature = signature;
                TriggerStartupApply();
            }
            else
            {
                _ = ReconcileStartupAsync();
            }
        }

        public string CpuVendor => _cachedCpuVendor ??= _hardwareService.GetCpuVendor();

        private string? _cachedCpuModel;

        /// <summary>CPU model for the informational overview (cached; Unknown when unreadable).</summary>
        public string CpuModelText => _cachedCpuModel ??= _hardwareService.GetCpuModel();

        public string CpuVendorText
        {
            get
            {
                var vendor = CpuVendor;
                return vendor == "AMD"
                    ? IsPolishLanguage ? "Wyłącz SMT" : "Disable SMT"
                    : IsPolishLanguage ? "Wyłącz HT" : "Disable HT";
            }
        }

        public IReadOnlyList<StartupWindowMode> StartupWindowModes { get; } = new[]
        {
            StartupWindowMode.Normal,
            StartupWindowMode.Minimized,
            StartupWindowMode.Tray
        };

        public StartupWindowMode SelectedStartupWindowMode
        {
            get => AppSettings.StartupWindowMode;
            set
            {
                if (AppSettings.StartupWindowMode == value) return;
                AppSettings.StartupWindowMode = value;
                OnPropertyChanged();
                _settingsService.SaveSettings(AppSettings);
                TriggerStartupApply();
            }
        }

        public bool IsStartupBusy
        {
            get => _startupBusy;
            private set => SetProperty(ref _startupBusy, value);
        }

        public string StartupStatusText
        {
            get => _startupStatusText;
            private set => SetProperty(ref _startupStatusText, value);
        }

        public string StartupDetailText
        {
            get => _startupDetailText;
            private set => SetProperty(ref _startupDetailText, value);
        }

        public bool IsRunningAsAdministrator => _isElevated;

        public string CurrentPrivilegesText => _isElevated
            ? "Administrator"
            : (IsPolishLanguage ? "Użytkownik standardowy" : "Standard user");

        public string DataResetStatusText
        {
            get => _dataResetStatusText;
            private set => SetProperty(ref _dataResetStatusText, value);
        }

        public string FactoryResetConfirmationText
        {
            get => _factoryResetConfirmationText;
            set
            {
                if (!SetProperty(ref _factoryResetConfirmationText, value ?? string.Empty)) return;
                OnPropertyChanged(nameof(IsFactoryResetArmed));
            }
        }

        public bool IsFactoryResetArmed =>
            !_resetInProgress && string.Equals(_factoryResetConfirmationText?.Trim(), "RESET", StringComparison.Ordinal);

        private bool _resetInProgress;

        private Task RestoreDefaultSettingsAsync() => Task.Run(() => RestoreDefaultSettingsCore());

        private void RestoreDefaultSettingsCore()
        {
            try
            {
                var defaults = UserDataResetService.BuildDefaultSettings(AppSettings);

                // Startup preferences reset to off: reconcile the real registration
                // first; on failure keep the previous startup trio instead of
                // pretending autostart is gone.
                var off = new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false);
                var reconcile = _startupCoordinator
                    .ApplyLatestAsync(off).GetAwaiter().GetResult();
                if (reconcile.Success)
                {
                    defaults.StartWithWindows = false;
                    defaults.StartupWindowMode = StartupWindowMode.Normal;
                    defaults.StartupRunElevated = false;
                    DataResetStatusText = IsPolishLanguage
                        ? "Przywrócono ustawienia domyślne (profile nietknięte)."
                        : "Default settings restored (profiles untouched).";
                }
                else
                {
                    defaults.StartWithWindows = AppSettings.StartWithWindows;
                    defaults.StartupWindowMode = AppSettings.StartupWindowMode;
                    defaults.StartupRunElevated = AppSettings.StartupRunElevated;
                    DataResetStatusText = (IsPolishLanguage
                        ? "Ustawienia przywrócono, ale autostartu nie udało się usunąć: "
                        : "Settings restored, but startup could not be removed: ")
                        + (reconcile.Error ?? reconcile.FinalEvaluation.State.ToString());
                }

                defaults.StartupSettingsVersion = Math.Max(AppSettings.StartupSettingsVersion, 1);
                _settingsService.SaveSettings(defaults);
                OnPropertyChanged(nameof(SelectedLanguageName));
                OnPropertyChanged(nameof(CpuVendorText));
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    AppSettings = defaults;
                    RefreshAvailablePriorities();
                    RefreshDisplayedPriorities();
                    _lastStartupSignature = StartupSignature(AppSettings);
                    _ = RefreshStartupStatusAsync();
                });
                AddLog(DataResetStatusText);
            }
            catch (Exception ex)
            {
                DataResetStatusText = ex.Message;
            }
        }

        private void OpenExternalLink(PcoExternalLink link)
        {
            ExternalLinkErrorText = null;
            if (!Services.ExternalLinks.TryOpen(link))
            {
                ExternalLinkErrorText = WpfApplication.Current?.TryFindResource("StrLinkOpenFailed") as string
                    ?? (IsPolishLanguage
                        ? "Nie udało się otworzyć strony w przeglądarce."
                        : "Could not open the link in your browser.");
            }
        }

        private void NavigateToAbout()
        {
            SwitchTab("Settings");
            ActiveSettingsCategory = "About";
            AboutRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenFirstRunWizard()
        {
            var window = new Views.FirstRunWindow
            {
                Owner = WpfApplication.Current.MainWindow
            };
            window.ShowDialog();
            ReloadSettingsFromDisk();
        }

        private Task RunFactoryResetAsync() => Task.Run(async () => await RunFactoryResetCore().ConfigureAwait(false));

        private async Task RunFactoryResetCore()
        {
            if (!IsFactoryResetArmed || _resetInProgress) return;
            _resetInProgress = true;
            OnPropertyChanged(nameof(IsFactoryResetArmed));

            try
            {
                // Quiesce the engine: no scans, no applies, no hardware polling.
                _refreshTimer.Stop();
                var idleDeadline = DateTime.UtcNow.AddSeconds(5);
                while (_isRefreshing && DateTime.UtcNow < idleDeadline)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }

                if (_isRefreshing)
                {
                    SetResetStatus(IsPolishLanguage
                        ? "Reset zablokowany: operacja w tle nadal trwa. Spróbuj ponownie."
                        : "Reset blocked: a background operation is still running. Retry.");
                    return;
                }

                var service = UserDataResetService.ForCurrentUser();
                DataResetResult result = await service.FactoryResetAsync(
                    isBusy: () => _isRefreshing,
                    prepareForWipeAsync: () =>
                    {
                        StopHardwareMonitor(closeSensors: true, resetMetrics: true);
                        _operationHistory.Clear();
                        _sessionRestore.Clear();
                        _optimizationService.ClearAllCache();
                        _optimizationService.CleanupStaleCache(new HashSet<ProcessInstanceKey>());
                        // P0-1: drop rule state before the wipe so no rule can fire
                        // against half-wiped data, whatever the wipe outcome is.
                        WpfApplication.Current.Dispatcher.Invoke(() =>
                        {
                            _rules = new List<SmartRule>();
                            _rulesSnapshot = new List<SmartRule>();
                            Rules.Clear();
                            RecentRuleDecisions.Clear();
                            SelectedRule = null;
                        });
                        _ruleEngine.Clear();
                        return Task.CompletedTask;
                    },
                    removeStartupAsync: async () =>
                    {
                        var apply = await _startupCoordinator
                            .ApplyLatestAsync(new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false))
                            .ConfigureAwait(false);
                        if (apply.Success) return null;
                        return apply.Error ?? apply.FinalEvaluation.State.ToString();
                    }).ConfigureAwait(false);

                if (result.Blocked)
                {
                    SetResetStatus(IsPolishLanguage ? "Reset zablokowany: trwająca operacja." : "Reset blocked: operation in flight.");
                    return;
                }

                TruncateActiveLog(result.ActiveLogPath);

                // Drop all in-memory user state; nothing is re-read from disk.
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    _profiles = new List<ProcessProfile>();
                    SavedProfiles.Clear();
                    _rules = new List<SmartRule>();
                    _rulesSnapshot = new List<SmartRule>();
                    Rules.Clear();
                    RecentRuleDecisions.Clear();
                    SelectedRule = null;
                    Processes.Clear();
                    RecentOperations.Clear();
                    ProfileConflicts.Clear();
                    OnPropertyChanged(nameof(HasProfileConflicts));
                    ConflictSummaryText = string.Empty;
                    SelectedSavedProfile = null;
                    SelectedProcess = null;
                    FactoryResetConfirmationText = string.Empty;
                });

                if (result.Success && string.IsNullOrWhiteSpace(result.StartupRemovalError))
                {
                    SetResetStatus(IsPolishLanguage
                        ? $"Usunięto dane ({result.DeletedFiles.Count} plików). Aplikacja zostanie zamknięta — ponowne uruchomienie pokaże kreator."
                        : $"Erased {result.DeletedFiles.Count} file(s). The app will close — relaunch shows the wizard.");
                    AddLog(DataResetStatusText);
                    await Task.Delay(1200).ConfigureAwait(false);
                    WpfApplication.Current.Dispatcher.Invoke(() => WpfApplication.Current.Shutdown());
                    return;
                }

                if (result.Success)
                {
                    SetResetStatus((IsPolishLanguage
                        ? "Dane usunięte, ale autostart pozostał: "
                        : "Data erased, but startup remains: ") + result.StartupRemovalError);
                    // App keeps running: re-sync rule memory with (possibly partial) disk.
                    ReloadRules();
                    RefreshRuleStatus();
                }
                else
                {
                    SetResetStatus((IsPolishLanguage
                        ? "Reset niepełny: "
                        : "Incomplete reset: ") + string.Join("; ", result.Errors.Take(3)));
                    ReloadRules();
                    RefreshRuleStatus();
                }

                AddLog(DataResetStatusText);
            }
            catch (Exception ex)
            {
                SetResetStatus(ex.Message);
            }
            finally
            {
                _resetInProgress = false;
                OnPropertyChanged(nameof(IsFactoryResetArmed));
                if (!_disposed && !_refreshTimer.IsEnabled)
                {
                    _refreshTimer.Start();
                }
            }
        }

        private void SetResetStatus(string message)
        {
            DataResetStatusText = message;
        }

        private void TruncateActiveLog(string? activeLogPath)
        {
            try
            {
                string current = AppPaths.ResolveUserLogFilePath(AppSettings.LogFilePath);
                string target = activeLogPath ?? current;
                if (!File.Exists(target)) return;

                // Move the writer to a temp path, truncate, move back.
                string tempLog = Path.Combine(Path.GetTempPath(), $"pco_log_{Guid.NewGuid():N}.log");
                LoggerService.Shared.Configure(AppSettings.LogEnabled, LogLevel.FromValue(AppSettings.LogLevelValue), tempLog, AppSettings.EnableConsoleOutput, AppSettings.LogSourceName);
                try { File.WriteAllText(target, $"[{DateTime.Now:HH:mm:ss}] Factory reset completed; previous log truncated.{Environment.NewLine}"); }
                catch { }
                LoggerService.Shared.Configure(AppSettings.LogEnabled, LogLevel.FromValue(AppSettings.LogLevelValue), AppSettings.LogFilePath, AppSettings.EnableConsoleOutput, AppSettings.LogSourceName);
                try { if (File.Exists(tempLog)) File.Delete(tempLog); } catch { }
            }
            catch
            {
                // Log truncation is best-effort; the audit line above already landed.
            }
        }

        public MainViewModel()
        {
            Services.StartupProfiler.Mark("VM ctor start");
            bool hadSettings = _settingsService.HadExistingSettings();
            bool hadProfiles = File.Exists(AppPaths.GetUserDataFilePath("profiles.json"));
            _appSettings = _settingsService.LoadSettings();
            ConfigureLoggerFromSettings();
            ResolveOnboardingState(hadSettings, hadProfiles);

            _processScannerService = new ProcessScannerService(_processService);
            _optimizationService = new OptimizationService(_processService, () => _cpuSetMap, _sessionRestore);
            _profileWatcher = new ProfileWatcherService(
                _processScannerService,
                _optimizationService,
                _operationHistory,
                () => _profiles.ToList(),
                () => AppSettings.AllowRealtimePriority,
                () => _rulesSnapshot
                    .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.ProcessName))
                    .Select(r => ProfileService.NormalizeProcessName(r.ProcessName))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
            _realtimeCacheFlag = _appSettings.AllowRealtimePriority;
            _lastStartupSignature = StartupSignature(_appSettings);
            _startupCoordinator = new StartupApplyCoordinator(new StartupConfigurationExecutor(_startupService));
            _isElevated = _startupService.IsRunAsAdmin();
            OnPropertyChanged(nameof(IsRunningAsAdministrator));
            OnPropertyChanged(nameof(CurrentPrivilegesText));
            // Self-heal: settings may say "start with Windows" while the real
            // registration is missing or stale (EXE moved/updated, entry disabled
            // in Startup apps, choice made in the first-run wizard).
            _ = ReconcileStartupAsync();

            _cpuSetMap = _hardwareService.GetLogicalCoreToCpuSetIdMap();
            foreach (var core in _hardwareService.GetCoreTopology())
            {
                Cores.Add(core);
            }
            Services.StartupProfiler.Mark("VM ctor: WMI topology done");
            OnPropertyChanged(nameof(HasECores));
            foreach (var core in Cores)
            {
                core.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Models.CoreInfo.IsChecked))
                    {
                        OnPropertyChanged(nameof(SelectedCoreSummary));
                        OnPropertyChanged(nameof(IsCoreMaskEmpty));
                    }
                };
            }
            // Warm the CPU model name off the UI thread (WMI); UI updates when ready.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var _ = CpuModelText;
                    WpfApplication.Current?.Dispatcher.Invoke(() => OnPropertyChanged(nameof(CpuModelText)));
                }
                catch { }
            });
            OnPropertyChanged(nameof(LogicalProcessorCount));
            OnPropertyChanged(nameof(PhysicalCoreCount));
            OnPropertyChanged(nameof(SmtThreadCount));
            OnPropertyChanged(nameof(PerformanceCoreCount));
            OnPropertyChanged(nameof(EfficiencyCoreCount));
            OnPropertyChanged(nameof(TopologyIsUnknown));
            OnPropertyChanged(nameof(AffinityRangeText));
            RefreshCorePresets();
            RefreshTopologyLimitation();

            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            RefreshProfileConflicts();
            ReloadRules();
            _profileWatcher.ScanCompleted += (s, scan) =>
            {
                // Watcher scans are TARGETED subsets: absence proves nothing,
                // so the engine retains presence state (P0-3).
                try { EvaluateAndApplyRules(scan.Groups, "background rule engine", Services.RuleScanKind.Targeted); }
                catch (Exception ex) { LoggerService.Instance.Debug($"Background rule hook failed: {ex.Message}"); }
            };
            RefreshRecentOperations();
            LogProfileWatcherStartupSummary();
            Services.StartupProfiler.Mark("VM ctor: profiles+rules loaded");
            // Event-driven UI flags (no timers).
            ActionLogs.CollectionChanged += (s, e) => OnPropertyChanged(nameof(ActivitySummary));
            SavedProfiles.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasSavedProfiles));
            Rules.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasRules));
                if (SelectedRule != null && !Rules.Contains(SelectedRule)) SelectedRule = null;
            };

            _hwTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppSettings.HardwareRefreshSeconds) };
            _hwTimer.Tick += (s, e) => UpdateHardwareMetrics();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppSettings.ProcessListRefreshSeconds) };
            _refreshTimer.Tick += async (s, e) => await RefreshStatisticsAsync();

            SwitchTabCommand = new RelayCommand(tab => SwitchTab(tab?.ToString()));
            CloseProcessTargetCommand = new RelayCommand(_ =>
            {
                SelectedProcess = null;
                SelectedInstanceRow = null;
            });
            ApplySettingsCommand = new RelayCommand(_ => ApplyOptimization());
            SaveProfileCommand = new RelayCommand(_ => SaveCurrentAsProfile());
            UpdateProfileCommand = new RelayCommand(_ => UpdateSelectedProfile());
            DeleteProfileCommand = new RelayCommand(_ => DeleteSelectedProfile());
            SelectAllCommand = new RelayCommand(_ => SetAllCores(true));
            ClearAllCommand = new RelayCommand(_ => SetAllCores(false));
            DisableSmtCommand = new RelayCommand(_ => DisableSmtThreads());
            DisableECoresCommand = new RelayCommand(_ => DisableEfficiencyCores());
            ToggleSettingCommand = new RelayCommand(_ => SaveAndApplySettings(restartAsAdminIfNeeded: false));
            RestartAsAdministratorCommand = new RelayCommand(_ => RestartElevated());
            RefreshStartupStatusCommand = new RelayCommand(_ => { _ = RefreshStartupStatusAsync(); });
            RepairStartupCommand = new RelayCommand(_ => TriggerStartupApply());
            OpenWizardCommand = new RelayCommand(_ => OpenFirstRunWizard());
            RestoreDefaultsCommand = new RelayCommand(_ => { _ = RestoreDefaultSettingsAsync(); });
            FactoryResetCommand = new RelayCommand(_ => { _ = RunFactoryResetAsync(); });
            CheckForUpdatesNowCommand = new RelayCommand(_ => { _ = CheckForUpdatesNowAsync(); });
            OpenAuthorCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Author));
            OpenRepositoryCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Repository));
            OpenWebsiteCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Website));
            OpenStudioCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Studio));
            OpenSupportCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Support));
            OpenAboutCommand = new RelayCommand(_ => NavigateToAbout());
            ApplyCorePresetCommand = new RelayCommand(preset => ApplyCorePreset(preset?.ToString()));
            PreviewCorePresetCommand = new RelayCommand(preset => PreviewCorePreset(preset?.ToString()));
            DuplicateProfileCommand = new RelayCommand(_ => DuplicateSelectedProfile());
            ExportProfilesCommand = new RelayCommand(_ => ExportProfilesToFile());
            ImportProfilesCommand = new RelayCommand(_ => ImportProfilesFromFile());
            CaptureLiveSettingsCommand = new RelayCommand(_ => CaptureLiveSettings());
            RestoreSettingsCommand = new RelayCommand(_ => RestoreSessionSettings());
            CopyPidCommand = new RelayCommand(_ => CopySelectedPid());
            CopyExePathCommand = new RelayCommand(_ => CopySelectedExePath());
            OpenFileLocationCommand = new RelayCommand(_ => OpenSelectedFileLocation());
            PreviewChangesCommand = new RelayCommand(_ => PreviewSelectedChanges());
            RefreshDetailsCommand = new RelayCommand(_ => RefreshSelectedDetails());
            CreateProfileFromInstanceCommand = new RelayCommand(_ => CreateProfileFromSelectedInstance());
            ApplyToInstanceCommand = new RelayCommand(_ => ApplyToSelectedInstance());
            NewProfileCommand = new RelayCommand(_ => CreateNewProfile());
            EditProfileCommand = new RelayCommand(_ => EditSelectedProfile());
            ToggleProfileEnabledCommand = new RelayCommand(_ => ToggleSelectedProfileEnabled());
            NewRuleCommand = new RelayCommand(_ => CreateNewRule());
            EditRuleCommand = new RelayCommand(_ => EditSelectedRule());
            DeleteRuleCommand = new RelayCommand(_ => DeleteSelectedRule());
            ToggleRuleEnabledCommand = new RelayCommand(_ => ToggleSelectedRuleEnabled());
            ClearRuleOverrideCommand = new RelayCommand(_ => ClearSelectedRuleOverride());
            ExportDiagnosticsCommand = new RelayCommand(_ => ExportDiagnosticsPackage());

            _updateSession = new UpdateCheckSession(AppVersion);
            ApplyLanguage(_appSettings.Language);
            Services.StartupProfiler.Mark("VM ctor end");
            _refreshTimer.Start();
            // Kick the first scan immediately instead of waiting a full interval:
            // the scan runs async and the list fills as soon as data is ready.
            _ = RefreshStatisticsAsync();
            AddLog(L($"System initialized. App Version v{AppVersion}", $"System uruchomiony. Wersja aplikacji v{AppVersion}"));
            if (AppSettings.CheckForUpdates && !ProcessCoreOptimizer.WPF.App.StartupOptions.DisableUpdateCheck)
            {
                _ = CheckForUpdatesAsync();
            }
        }

        private bool IsProcessListUiActive()
        {
            var window = WpfApplication.Current.MainWindow;
            return ActiveTab == "Processes"
                && window?.IsVisible == true
                && window.WindowState != WindowState.Minimized;
        }

        private bool IsCoreMapUiActive()
        {
            var window = WpfApplication.Current.MainWindow;
            return ActiveTab == "Cpu"
                && window?.IsVisible == true
                && window.WindowState != WindowState.Minimized;
        }

        private async Task RefreshStatisticsAsync()
        {
            if (_isRefreshing || _disposed) return;
            _isRefreshing = true;
            Services.StartupProfiler.Mark("refresh tick start");

            try
            {
                // The very first pass always takes a full UI scan so the list fills
                // immediately even if the window is not visible yet (data only;
                // charts still wait for a visible Processes tab).
                bool fullUiScan = !_firstRefreshDone || IsProcessListUiActive();
                _refreshTimer.Interval = TimeSpan.FromSeconds(fullUiScan ? AppSettings.ProcessListRefreshSeconds : AppSettings.ProfileWatcherSeconds);

                // The CPU & Topology tab shows live per-core load: keep sampling it
                // (cheap PDH read, off the UI thread) instead of freezing the tiles.
                bool coreMapVisible = !fullUiScan && IsCoreMapUiActive();
                if (!fullUiScan && !coreMapVisible)
                {
                    _hardwareService.ReleaseCpuLoadCounters();
                }
                else if (coreMapVisible)
                {
                    _ = UpdateCoreLoadsAsync();
                }

                // Snapshot the profile list: the UI thread may add/remove profiles concurrently.
                List<ProcessProfile> profilesSnapshot = _profiles.ToList();

                if (!fullUiScan)
                {
                    // Background watcher: single authoritative pipeline with reentrancy,
                    // cancellation and shutdown guards (no UI touches inside).
                    // Safe mode: observe only, never mutate.
                    // Global pause: same — observation continues, mutations stop.
                    if (IsSafeMode)
                    {
                        LogSafeModeSkipOnce();
                        return;
                    }

                    if (AppSettings.AutomationPaused)
                    {
                        LogPausedSkipOnce();
                        return;
                    }

                    OptimizationBatchResult? batch = await _profileWatcher
                        .RunOnceAsync("background profile watcher", force: false)
                        .ConfigureAwait(true);
                    if (batch != null)
                    {
                        CleanupOptimizationDiagnosticLogCache();
                        LogOptimizationBatch(batch, "background profile watcher");
                    }

                    return;
                }

                Services.StartupProfiler.Mark("scan bg start");
                ProcessScanResult scan = await _processScannerService.ScanUserProcessesAsync();
                Services.StartupProfiler.Mark($"scan bg end groups={scan.Groups.Count}");
                _optimizationService.CleanupStaleCache(scan.ActiveInstances);
                CleanupOptimizationDiagnosticLogCache();
                bool paused = AppSettings.AutomationPaused;
                OptimizationBatchResult uiBatch = (IsSafeMode || paused)
                    ? new OptimizationBatchResult()
                    : _optimizationService.ApplyProfilesForSnapshots(profilesSnapshot, scan.Groups, AppSettings.AllowRealtimePriority, force: false);
                if (IsSafeMode) LogSafeModeSkipOnce();
                if (paused) LogPausedSkipOnce();
                _operationHistory.RecordBatch(uiBatch, "process list refresh");
                LogOptimizationBatch(uiBatch, "process list refresh");
                if (!paused) EvaluateAndApplyRules(scan.Groups, "process list refresh");
                Services.StartupProfiler.Mark("engine+rules done");

                _ = UpdateCoreLoadsAsync();
                Services.StartupProfiler.Mark("core loads dispatched");
                AddMissingProcessRows(scan.Groups);
                Services.StartupProfiler.Mark("add-missing rows done");
                UpdateProcessRows(scan.Groups);
                Services.StartupProfiler.Mark($"update rows done procs={Processes.Count}");
                _lastUiScan = scan;
                if (IsProcessListLoading) IsProcessListLoading = false;
                if (IsInstanceView) RebuildInstanceRows();
                if (IsProcessListUiActive())
                    UpdateInsights(SelectedInstanceRow?.ProcessId ?? SelectedProcessDetails?.RepresentativePid);
            }
            catch (Exception ex)
            {
                AddLog(L($"Refresh failed: {ex.Message}", $"Odświeżanie nie powiodło się: {ex.Message}"));
            }
            finally
            {
                _isRefreshing = false;
                _firstRefreshDone = true;
            }
        }

        private async Task UpdateCoreLoadsAsync()
        {
            // First calls warm up PDH counters (seconds): never on the UI thread.
            try
            {
                var coreLoads = await Task.Run(() => _hardwareService.GetCurrentLoads()).ConfigureAwait(true);
                for (int i = 0; i < Cores.Count && i < coreLoads.Count; i++)
                {
                    var sample = coreLoads[i];
                    Cores[i].LoadAvailable = sample.UtilizationPercent.HasValue;
                    Cores[i].LoadUsage = sample.UtilizationPercent ?? 0;
                    Cores[i].UtilityPercent = sample.UtilityPercent ?? double.NaN;
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Debug($"Core loads update failed: {ex.Message}");
            }
            Services.StartupProfiler.Mark("core loads applied");
        }

        private void AddMissingProcessRows(IReadOnlyList<ProcessGroupSnapshot> groups)
        {
            RefreshProcessRows(groups);
        }

        /// <summary>
        /// Single Dispatcher hop per refresh: dictionary lookups instead of O(n^2)
        /// scans, one deferred CollectionView refresh instead of per-change resorts.
        /// Row objects are updated in place so selection survives valid refreshes.
        /// </summary>
        private void RefreshProcessRows(IReadOnlyList<ProcessGroupSnapshot> groups)
        {
            var byName = new Dictionary<string, ProcessGroupSnapshot>(groups.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups) byName[g.Name] = g;
            var enabledProfiles = new Dictionary<string, ProcessProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _profiles)
            {
                if (p.IsEnabled && !enabledProfiles.ContainsKey(p.ProcessName)) enabledProfiles[p.ProcessName] = p;
            }

            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                // Pause live re-sorting while rows mutate (restored below);
                // the grid keeps the user's column arrows, just no per-change resort.
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Processes);
                var sortBackup = view?.SortDescriptions.ToList();
                view?.SortDescriptions.Clear();
                try
                {
                    for (int i = Processes.Count - 1; i >= 0; i--)
                    {
                        var item = Processes[i];
                        if (!byName.TryGetValue(item.Name, out var snapshot) ||
                            !MatchesProcessFilter(item.Name, item.ExecutablePath, item.IsOptimized))
                        {
                            Processes.RemoveAt(i);
                            continue;
                        }

                        ApplySnapshotToRow(item, snapshot, enabledProfiles);
                    }

                    var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var existing in Processes) existingNames.Add(existing.Name);

                    foreach (var group in groups)
                    {
                        bool isOptimized = enabledProfiles.ContainsKey(group.Name);
                        if (!MatchesProcessFilter(group.Name, group.ExecutablePath, isOptimized: isOptimized)) continue;
                        if (existingNames.Contains(group.Name)) continue;

                        enabledProfiles.TryGetValue(group.Name, out var profile);
                        string rawPrio = profile?.Priority ?? group.Priority.ToString();
                        string tag = profile != null ? GetModeTag(profile.OptimizationMode) : string.Empty;

                        Processes.Add(new ProcessItem
                        {
                            Id = group.FirstProcessId,
                            Name = group.Name,
                            ExecutablePath = group.ExecutablePath,
                            PriorityRank = PriorityService.Rank(profile?.Priority ?? group.Priority.ToString()),
                            InstanceCount = group.InstanceCount,
                            Priority = TranslatePriority(rawPrio),
                            IsOptimized = profile != null,
                            ModeTag = tag,
                            RamUsageMB = $"{group.TotalMemoryBytes / 1024 / 1024} MB",
                            CpuUsage = $"{Math.Round(group.CpuUsagePercent, 1)}%",
                            CpuUsageValue = group.CpuUsagePercent,
                            MemoryBytes = group.TotalMemoryBytes
                        });
                    }
                }
                finally
                {
                    if (view != null && sortBackup != null)
                    {
                        foreach (var sort in sortBackup) view.SortDescriptions.Add(sort);
                    }
                }
            });
        }

        private void ApplySnapshotToRow(ProcessItem item, ProcessGroupSnapshot snapshot, Dictionary<string, ProcessProfile> enabledProfiles)
        {
            enabledProfiles.TryGetValue(item.Name, out var profile);
            string currentTag = profile != null ? GetModeTag(profile.OptimizationMode) : string.Empty;

            item.Id = snapshot.FirstProcessId;
            item.InstanceCount = snapshot.InstanceCount;
            item.RamUsageMB = $"{snapshot.TotalMemoryBytes / 1024 / 1024} MB";
            item.CpuUsage = $"{Math.Round(snapshot.CpuUsagePercent, 1)}%";
            item.CpuUsageValue = snapshot.CpuUsagePercent;
            item.MemoryBytes = snapshot.TotalMemoryBytes;
            item.ExecutablePath = snapshot.ExecutablePath;
            item.PriorityRank = PriorityService.Rank(profile?.Priority ?? snapshot.Priority.ToString());
            item.Priority = TranslatePriority(profile?.Priority ?? snapshot.Priority.ToString());
            item.IsOptimized = profile != null;
            item.ModeTag = currentTag;
        }

        private void UpdateProcessRows(IReadOnlyList<ProcessGroupSnapshot> groups)
        {
            RefreshProcessRows(groups);
        }

        private void UpdateHardwareMetrics()
        {
            if (!AppSettings.HardwareMonitorEnabled || ActiveTab != "Hardware" || WpfApplication.Current.MainWindow?.IsVisible != true)
            {
                return;
            }

            try
            {
                var service = EnsureHardwareMonitorService();
                var metrics = service.GetAllMetrics();
                FullMetrics = metrics;
                MonCpuTemp = Math.Round(metrics.CpuTemp, 1);
                MonGpuTemp = Math.Round(metrics.GpuTemp, 1);
                MonGpuLoad = Math.Round(metrics.GpuLoad, 1);
                MonRamUsage = Math.Round(metrics.RamUsagePct, 1);
            }
            catch (Exception ex)
            {
                AddLog(L($"Hardware monitor failed: {ex.Message}", $"Monitor sprzętu nie działa: {ex.Message}"));
                StopHardwareMonitor(closeSensors: true, resetMetrics: false);
            }
        }

        private HardwareMonitorService EnsureHardwareMonitorService()
        {
            _hwService ??= new HardwareMonitorService(AppSettings.EnableStorageSensors);
            _hwService.Configure(AppSettings.EnableStorageSensors);
            return _hwService;
        }

        private void StartHardwareMonitorIfVisible()
        {
            if (ActiveTab != "Hardware" || !AppSettings.HardwareMonitorEnabled)
            {
                return;
            }

            var service = EnsureHardwareMonitorService();
            service.Start();
            _hwTimer.Interval = TimeSpan.FromSeconds(AppSettings.HardwareRefreshSeconds);
            if (!_hwTimer.IsEnabled)
            {
                _hwTimer.Start();
            }

            OnPropertyChanged(nameof(IsHardwareMonitorRunning));
            UpdateHardwareMetrics();
        }

        private void StopHardwareMonitor(bool closeSensors, bool resetMetrics)
        {
            _hwTimer.Stop();
            _hwService?.Stop(closeSensors);

            if (closeSensors)
            {
                _hwService?.Dispose();
                _hwService = null;
            }


            if (resetMetrics)
            {
                FullMetrics = new HardwareMetrics();
                MonCpuTemp = 0;
                MonGpuTemp = 0;
                MonGpuLoad = 0;
                MonRamUsage = 0;
            }

            OnPropertyChanged(nameof(IsHardwareMonitorRunning));
        }

        private void SwitchTab(string? tabName)
        {
            if (string.IsNullOrEmpty(tabName)) return;

            ActiveTab = tabName;
            OnPropertyChanged(nameof(TargetPanelTitle));
            OnPropertyChanged(nameof(TargetScopeLabel));
            ViewProcesses = tabName == "Processes" ? Visibility.Visible : Visibility.Collapsed;
            ViewProfiles = tabName == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
            ViewCpu = tabName == "Cpu" ? Visibility.Visible : Visibility.Collapsed;
            ViewHardwareMonitor = tabName == "Hardware" ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings = tabName == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            ViewDiagnostics = tabName == "Diagnostics" ? Visibility.Visible : Visibility.Collapsed;
            ViewSmartRules = tabName == "Rules" ? Visibility.Visible : Visibility.Collapsed;
            OnPropertyChanged(nameof(IsRightPanelVisible));

            SelectedProcess = null;
            SelectedInstanceRow = null;
            SelectedSavedProfile = null;
            SelectedRule = null;

            if (tabName == "Hardware")
            {
                StartHardwareMonitorIfVisible();
            }
            else
            {
                StopHardwareMonitor(closeSensors: true, resetMetrics: false);
            }

            if (tabName != "Processes" && tabName != "Cpu")
            {
                _hardwareService.ReleaseCpuLoadCounters();
            }
        }

        private void ApplyOptimization()
        {
            if (SelectedProcess == null) return;

            long mask = BuildSelectedCoreMask();
            if (mask == 0)
            {
                AddLog(L("No CPU cores selected. Optimization was not applied.", "Nie wybrano rdzeni CPU. Optymalizacja nie została zastosowana."));
                return;
            }

            string priority = NormalizePriority(_selectedPriority);
            if (priority == "RealTime" && !ConfirmRealTimePriority()) return;

            var mode = NormalizeOptimizationMode(SelectedOptimizationMode);
            var temporaryProfile = new ProcessProfile
            {
                ProcessName = SelectedProcess.Name,
                AffinityMask = mask,
                Priority = priority,
                OptimizationMode = mode,
                IsEnabled = true,
                ApplyPriority = true,
                ApplyCoreOptimization = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // An explicit click must not be skipped by an earlier suspension
            // (e.g. a session restore a minute ago).
            _optimizationService.ClearSuspensionsForProcess(SelectedProcess.Name);
            int affected = ApplyProfileToRunningProcesses(temporaryProfile, force: true);
            SelectedProcess.IsOptimized = affected > 0;
            SelectedProcess.ModeTag = affected > 0 ? GetModeTag(mode) : string.Empty;
            SelectedProcess.Priority = TranslatePriority(priority);

            AddLog(L($"Applied '{SelectedProcess.Name}' optimization: {GetModeTag(mode)}, {CountSelectedCores()} cores, priority {TranslatePriority(priority)} ({affected} process instance(s)).", $"Zastosowano optymalizację '{SelectedProcess.Name}': {GetModeTag(mode)}, rdzenie: {CountSelectedCores()}, priorytet {TranslatePriority(priority)} (instancje procesu: {affected})."));

            // A one-off manual apply used to be reverted by the watcher within
            // seconds whenever an enabled saved profile for the same process
            // differed. Keep the manual choice for the running instances; the
            // saved profile applies again to the next launch (or on Save/Update).
            string manualSignature = OptimizationService.BuildProfileSignature(temporaryProfile, priority, mode, AppSettings.AllowRealtimePriority);
            bool conflictsWithProfile = _profiles.Any(p => p.IsEnabled
                && string.Equals(ProfileService.NormalizeProcessName(p.ProcessName), ProfileService.NormalizeProcessName(SelectedProcess.Name), StringComparison.OrdinalIgnoreCase)
                && OptimizationService.BuildProfileSignature(p, NormalizePriority(p.Priority), NormalizeOptimizationMode(p.OptimizationMode), AppSettings.AllowRealtimePriority) != manualSignature);
            if (affected > 0 && conflictsWithProfile)
            {
                _optimizationService.SuspendLiveInstances(SelectedProcess.Name, TimeSpan.FromHours(12), "manual apply overrides saved profile");
                AddLog(IsPolishLanguage
                    ? $"'{SelectedProcess.Name}' ma zapisany profil z innymi ustawieniami. Ręczne ustawienia zostają dla działających instancji; użyj „Zapisz/Aktualizuj profil”, aby je zapamiętać."
                    : $"'{SelectedProcess.Name}' has a saved profile with different settings. The manual settings stay for running instances; use Save/Update profile to keep them.");
                RefreshWatcherStatus();
            }
        }

        private void SaveCurrentAsProfile()
        {
            if (SelectedProcess == null) return;

            long mask = BuildSelectedCoreMask();
            if (mask == 0)
            {
                AddLog(L("Profile was not saved because no CPU cores are selected.", "Profil nie został zapisany, bo nie wybrano rdzeni CPU."));
                return;
            }

            string rawPriority = NormalizePriority(_selectedPriority);
            if (rawPriority == "RealTime" && !ConfirmRealTimePriority()) return;

            var mode = NormalizeOptimizationMode(SelectedOptimizationMode);

            var newProfile = new ProcessProfile
            {
                ProcessName = SelectedProcess.Name,
                AffinityMask = mask,
                Priority = rawPriority,
                OptimizationMode = mode,
                IsEnabled = true,
                ApplyPriority = true,
                ApplyCoreOptimization = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // P0: replace only the name-only slot; path-bound siblings survive.
            _profiles = ProfileService.ReplaceNameOnlyProfile(_profiles, newProfile);
            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();

            _optimizationService.ClearSuspensionsForProcess(newProfile.ProcessName);
            ClearAppliedProfileCache(newProfile.ProcessName);
            int affected = ApplyProfileToRunningProcesses(newProfile, force: true);
            SelectedProcess.IsOptimized = true;
            SelectedProcess.ModeTag = GetModeTag(mode);
            SelectedProcess.Priority = TranslatePriority(rawPriority);

            AddLog(L($"Saved and applied profile for '{SelectedProcess.Name}': {GetModeTag(mode)}, priority {TranslatePriority(rawPriority)} ({affected} process instance(s)).", $"Zapisano i zastosowano profil '{SelectedProcess.Name}': {GetModeTag(mode)}, priorytet {TranslatePriority(rawPriority)} (instancje procesu: {affected})."));
        }

        private void UpdateSelectedProfile()
        {
            if (SelectedSavedProfile == null) return;

            long mask = BuildSelectedCoreMask();
            if (mask == 0)
            {
                AddLog(L("Profile was not updated because no CPU cores are selected.", "Profil nie został zaktualizowany, bo nie wybrano rdzeni CPU."));
                return;
            }

            string processName = SelectedSavedProfile.ProcessName;
            string rawPriority = NormalizePriority(_selectedPriority);
            if (rawPriority == "RealTime" && !ConfirmRealTimePriority()) return;

            var mode = NormalizeOptimizationMode(SelectedOptimizationMode);

            // Write back by stable Id into the full list. Rebuilding from the
            // filtered SavedProfiles view used to delete every profile hidden by
            // the active filter.
            var target = ProfileService.FindById(_profiles, SelectedSavedProfile.Id) ?? SelectedSavedProfile;
            // A priority-only profile stays priority-only unless the user really
            // narrowed the cores (all cores selected = no core restriction).
            if (target.ApplyCoreOptimization || mask != ProcessService.KnownProcessorMask(_cpuSetMap))
            {
                target.AffinityMask = mask;
                target.ApplyCoreOptimization = true;
            }
            target.Priority = rawPriority;
            target.DisplayPriority = TranslatePriority(rawPriority);
            target.OptimizationMode = mode;
            target.IsEnabled = true;
            target.UpdatedAt = DateTime.UtcNow;
            if (!_profiles.Contains(target)) _profiles.Add(target);

            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            ClearAppliedProfileCache(processName);
            _optimizationService.ClearSuspensionsForProcess(processName);

            // P0: re-resolve by stable Id — several path-bound profiles may share one name.
            var profileToApply = ProfileService.FindById(_profiles, SelectedSavedProfile.Id)
                ?? _profiles.FirstOrDefault(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            int affected = profileToApply != null ? ApplyProfileToRunningProcesses(profileToApply, force: true) : 0;

            var activeProcess = Processes.FirstOrDefault(p => p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (activeProcess != null)
            {
                activeProcess.IsOptimized = true;
                activeProcess.ModeTag = GetModeTag(mode);
                activeProcess.Priority = TranslatePriority(rawPriority);
            }

            AddLog(L($"Updated and applied profile for '{processName}': {GetModeTag(mode)}, priority {TranslatePriority(rawPriority)} ({affected} process instance(s)).", $"Zaktualizowano i zastosowano profil '{processName}': {GetModeTag(mode)}, priorytet {TranslatePriority(rawPriority)} (instancje procesu: {affected})."));
        }

        private void DeleteSelectedProfile()
        {
            if (SelectedSavedProfile == null) return;

            string name = SelectedSavedProfile.ProcessName;
            string deletedId = SelectedSavedProfile.Id;
            // P0: delete exactly this profile by stable Id from the source list
            // (never rebuild from the filtered SavedProfiles view — that would
            // drop profiles hidden by the active filter). Siblings survive.
            _profiles = ProfileService.RemoveById(_profiles, deletedId);
            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            ClearAppliedProfileCache(name);

            var activeProcess = Processes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (activeProcess != null)
            {
                activeProcess.IsOptimized = false;
                activeProcess.ModeTag = string.Empty;
            }

            AddLog(L($"Deleted profile: '{name}'", $"Usunięto profil: '{name}'"));
            SelectedSavedProfile = null;
        }

        private void RestoreSessionSettings()
        {
            if (SelectedProcess == null) return;
            if (IsSafeMode)
            {
                AddLog(L("Safe mode: session restore blocked, OS mutations disabled (--safe-mode).", "Tryb bezpieczny: przywracanie sesji zablokowane, zmiany w systemie wyłączone (--safe-mode)."));
                return;
            }
            var result = _sessionRestore.RestoreForProcess(SelectedProcess.Name, _processService, _cpuSetMap);
            // The watcher must not immediately re-apply the profile against the
            // user's intent: suspend automatic application for these instances.
            // Suspensions expire on their own; nothing persists.
            _optimizationService.SuspendLiveInstances(SelectedProcess.Name, TimeSpan.FromMinutes(5), "manual session restore");
            ClearAppliedProfileCache(SelectedProcess.Name);
            AddLog(L($"Session restore for '{SelectedProcess.Name}': {result.Message} Automatic re-apply suspended for 5 minutes.", $"Przywrócenie sesji '{SelectedProcess.Name}': {result.Message} Automatyczne ponowne stosowanie wstrzymane na 5 minut."));
            RefreshWatcherStatus();
        }

        private void UpdateCoreSelectionFromAffinity(string processName)
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcessesByName(ProfileService.NormalizeProcessName(processName));
                var first = processes.FirstOrDefault();
                if (first == null) return;

                var selection = _processService.GetCurrentCoreSelection(first.Id, _cpuSetMap);
                if (selection.Success && selection.Mask != 0)
                {
                    ApplyMaskToCoreSelection(selection.Mask);
                }
                else
                {
                    try
                    {
                        ApplyMaskToCoreSelection((long)first.ProcessorAffinity);
                    }
                    catch
                    {
                        // Leave the current checkbox state untouched when the OS refuses the read.
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog(L($"Could not read current affinity for '{processName}': {ex.Message}", $"Nie udało się odczytać bieżącej affinity '{processName}': {ex.Message}"));
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        private void ApplyMaskToCoreSelection(long mask)
        {
            for (int i = 0; i < Cores.Count; i++)
            {
                int logicalIndex = Cores[i].Index;
                Cores[i].IsChecked = logicalIndex >= 0 && logicalIndex < 64 && (mask & (1L << logicalIndex)) != 0;
            }
        }

        private long BuildSelectedCoreMask()
        {
            long mask = 0;
            foreach (var core in Cores)
            {
                int logicalIndex = core.Index;
                if (core.IsChecked && logicalIndex >= 0 && logicalIndex < 64)
                {
                    mask |= (1L << logicalIndex);
                }
            }
            return mask;
        }

        private int CountSelectedCores() => Cores.Count(c => c.IsChecked);

        private bool IsSafeMode => ProcessCoreOptimizer.WPF.App.StartupOptions.SafeMode;
        private bool _safeModeNoticeShown;

        private void LogSafeModeSkipOnce()
        {
            if (_safeModeNoticeShown) return;
            _safeModeNoticeShown = true;
            LoggerService.Instance.Info("Safe mode active: OS mutations disabled (--safe-mode).");
            AddLog(L("Safe mode: observing only, OS mutations disabled (--safe-mode).", "Tryb bezpieczny: tylko obserwacja, zmiany w systemie wyłączone (--safe-mode)."));
        }

        private bool _pausedNoticeShown;

        private void LogPausedSkipOnce()
        {
            if (_pausedNoticeShown) return;
            _pausedNoticeShown = true;
            AddLog(IsPolishLanguage
                ? "Pauza: obserwacja bez mutacji (AutomationPaused)."
                : "Paused: observing only, no mutations (AutomationPaused).");
        }

        private void RefreshRuleStatus()
        {
            int enabled = _rulesSnapshot.Count(r => r.IsEnabled);
            string status = AppSettings.AutomationPaused
                ? (IsPolishLanguage ? $"Pauza — reguły wstrzymane ({enabled} włączonych)." : $"Paused — rules suspended ({enabled} enabled).")
                : (IsPolishLanguage ? $"Reguły: {enabled} włączonych. {_ruleEngine.LastSummary}" : $"Rules: {enabled} enabled. {_ruleEngine.LastSummary}");
            var recent = _ruleEngine.RecentDecisions;
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                RuleStatusText = status;
                RecentRuleDecisions.Clear();
                foreach (var d in recent.TakeLast(15).Reverse())
                    RecentRuleDecisions.Add(d);
            });
        }

        /// <summary>
        /// Samples the selected PID on the existing refresh tick (no extra timer).
        /// Charts carry the last known value forward for continuity; the summary
        /// text always states the true readability of the latest point.
        /// </summary>
        private void UpdateProfileWhy()
        {
            string? name = SelectedInstanceRow?.Name ?? SelectedProcess?.Name;
            if (string.IsNullOrWhiteSpace(name)) return;
            string? path = SelectedInstanceRow?.ExecutablePath
                ?? SelectedProcess?.ExecutablePath
                ?? SelectedProcessDetails?.ExecutablePath;
            ProfileMatchWhy = Services.ProfileMatchExplainer.Explain(name, path, _profiles, IsPolishLanguage);
        }

        private void ExportDiagnosticsPackage()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "ZIP archive (*.zip)|*.zip",
                    FileName = $"PCO-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return;

                int logCount = ActionLogs.TakeLast(100).ToList().Count;
                string scope = IsPolishLanguage
                    ? $"Pakiet zawiera: {_profiles.Count} profili (ścieżki zamaskowane), {_rulesSnapshot.Count} reguł (nazwy jawne), aktywność reguł, status watchera, {logCount} linii logu (ścieżki/maile zamaskowane). Bez nazwy maszyny i notatek. ZIP NIE jest wysyłany nigdzie — przejrzyj go przed udostępnieniem. Zapisać?"
                    : $"Package contains: {_profiles.Count} profiles (paths masked), {_rulesSnapshot.Count} rules (names as-is), rule activity, watcher status, {logCount} log lines (paths/emails masked). No machine name, no notes. The ZIP is NOT uploaded anywhere — review it before sharing. Write?";
                var confirm = WpfMessageBox.Show(
                    scope,
                    IsPolishLanguage ? "Eksport diagnostyki — zakres (częściowa anonimizacja)" : "Diagnostics export — scope (partial anonymization)",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                string summary = Services.DiagnosticsExportService.BuildSummaryText(
                    AppVersion,
                    _profiles,
                    _rulesSnapshot,
                    profileId =>
                    {
                        var p = Services.ProfileService.FindById(_profiles, profileId);
                        return p != null ? $"{p.DisplayName} ({(p.IsEnabled ? "enabled" : "disabled")})" : null;
                    },
                    _ruleEngine.RecentDecisions,
                    WatcherStatusText,
                    ActionLogs.TakeLast(100).ToList());
                Services.DiagnosticsExportService.WriteZip(dialog.FileName, summary);
                AddLog(IsPolishLanguage
                    ? $"Zapisano pakiet diagnostyczny (zanonimizowany): {dialog.FileName}"
                    : $"Saved anonymized diagnostics package: {dialog.FileName}");
            }
            catch (Exception ex)
            {
                AddLog(L($"Diagnostics export failed: {ex.Message}", $"Eksport diagnostyki nie powiódł się: {ex.Message}"));
            }
        }

        private void UpdateInsights(int? pid)
        {
            if (pid == null || pid == 0)
            {
                HasInsights = false;
                return;
            }

            var reading = _insightsSampler.Sample(pid.Value);
            var history = _insightsSampler.History.TakeLast(60).ToList();

            double lastCpu = 0;
            var cpu = new List<double>(history.Count);
            foreach (var h in history)
            {
                if (h.CpuPercent.HasValue) lastCpu = h.CpuPercent.Value;
                cpu.Add(lastCpu);
            }

            long lastWs = 0, lastPriv = 0;
            var ws = new List<double>(history.Count);
            var priv = new List<double>(history.Count);
            foreach (var h in history)
            {
                if (h.WorkingSetBytes.HasValue) lastWs = h.WorkingSetBytes.Value;
                if (h.PrivateBytes.HasValue) lastPriv = h.PrivateBytes.Value;
                ws.Add(lastWs / 1024.0 / 1024.0);
                priv.Add(lastPriv / 1024.0 / 1024.0);
            }

            double cpuMax = Math.Max(cpu.DefaultIfEmpty(0).Max(), 1);
            double memMax = Math.Max(new[] { ws.DefaultIfEmpty(0).Max(), priv.DefaultIfEmpty(0).Max() }.Max(), 1);

            CpuHistoryPoints = Converters.HistoryToPointsConverter.ConvertToPoints(cpu, cpuMax);
            MemWsHistoryPoints = Converters.HistoryToPointsConverter.ConvertToPoints(ws, memMax);
            MemPrivateHistoryPoints = Converters.HistoryToPointsConverter.ConvertToPoints(priv, memMax);

            string cpuText = reading.CpuPercent.HasValue
                ? $"{reading.CpuPercent.Value:F1}%"
                : (IsPolishLanguage ? "niedostępne" : "unavailable");
            string memText = reading.Readable && reading.WorkingSetBytes.HasValue
                ? $"WS {reading.WorkingSetBytes.Value / 1024 / 1024} MB · {(IsPolishLanguage ? "pryw." : "priv.")} {(reading.PrivateBytes ?? 0) / 1024 / 1024} MB"
                : (IsPolishLanguage ? "niedostępne" : "unavailable");
            InsightsSummary = $"CPU {cpuText} · {memText}";
            HasInsights = history.Count > 0;
        }

        private void ThrottledRuleLog(SmartRule rule, string message, int minutes = 5)
        {
            DateTime now = DateTime.UtcNow;
            if (_lastRuleLogUtc.TryGetValue(rule.Id, out DateTime last) && now - last < TimeSpan.FromMinutes(minutes))
                return;
            _lastRuleLogUtc[rule.Id] = now;
            AddLog(message);
        }

        private void EvaluateAndApplyRules(IReadOnlyList<ProcessGroupSnapshot> groups, string source, Services.RuleScanKind scanKind = Services.RuleScanKind.Full)
        {
            try
            {
                if (IsSafeMode || AppSettings.AutomationPaused || _resetInProgress) return;
                var snapshot = _rulesSnapshot;
                if (snapshot.Count == 0 || !snapshot.Any(r => r.IsEnabled)) return;

                int? fg = null;
                if (snapshot.Any(r => r.IsEnabled && r.Trigger == RuleTrigger.OnForeground))
                {
                    try { fg = ForegroundService.GetForegroundPid(); } catch { fg = null; }
                }

                var decisions = _ruleEngine.Evaluate(groups, fg, DateTime.UtcNow, snapshot, automationPaused: false, scanKind);
                foreach (var d in decisions.Where(x => x.Ready))
                    ApplyRuleDecision(d, source);
                RefreshRuleStatus();
            }
            catch (Exception ex)
            {
                AddLog(L($"Rules evaluation failed: {ex.Message}", $"Ocena reguł nie powiodła się: {ex.Message}"));
            }
        }

        private void ApplyRuleDecision(RuleDecision d, string source)
        {
            var rule = SmartRuleService.FindById(_rulesSnapshot, d.RuleId);
            if (rule == null || !rule.IsEnabled) return;
            DateTime now = DateTime.UtcNow;
            try
            {
                if (rule.Action == RuleAction.ApplyProfile)
                {
                    var profile = ProfileService.FindById(_profiles, rule.ProfileId);
                    if (profile == null || !profile.IsEnabled)
                    {
                        // Misconfiguration, not a transient failure: do not hot-loop.
                        _ruleEngine.ReportAttempt(rule.Id, d.Key, now, Services.RuleAttemptOutcome.FailedPermanent);
                        ThrottledRuleLog(rule, IsPolishLanguage
                            ? $"Reguła '{rule.Name}': brak włączonego profilu — pominięto ({d.Reason})."
                            : $"Rule '{rule.Name}': no enabled profile — skipped ({d.Reason}).");
                        return;
                    }

                    var batch = _optimizationService.ApplyProfileToInstances(
                        profile, d.ProcessName, new[] { d.Key }, AppSettings.AllowRealtimePriority, force: true, automatic: true);
                    var first = batch.Results.FirstOrDefault();
                    if (first == null)
                    {
                        _ruleEngine.ReportAttempt(rule.Id, d.Key, now, Services.RuleAttemptOutcome.FailedRetryable);
                        ThrottledRuleLog(rule, $"Rule '{rule.Name}': no result for PID {d.Key.ProcessId}; will retry.", minutes: 1);
                        return;
                    }

                    switch (first.Status)
                    {
                        case OperationStatus.FullyApplied:
                            _ruleEngine.MarkApplied(rule.Id, d.Key, now);
                            AddLog(L($"Rule '{rule.Name}' applied '{profile.DisplayName}' to PID {d.Key.ProcessId} via {source} ({d.Reason}).", $"Reguła '{rule.Name}' zastosowała '{profile.DisplayName}' do PID {d.Key.ProcessId} ({source}, {d.Reason})."));
                            break;
                        case OperationStatus.PartiallyApplied:
                            // Half done; the remainder rides the watcher (partial results
                            // are never cached). Cooldown starts so the rule does not
                            // re-apply the successful half.
                            _ruleEngine.MarkApplied(rule.Id, d.Key, now);
                            AddLog(L($"Rule '{rule.Name}' partially applied '{profile.DisplayName}' to PID {d.Key.ProcessId} via {source} ({d.Reason}): {first.Message}. Remainder via watcher.", $"Reguła '{rule.Name}' częściowo zastosowała '{profile.DisplayName}' do PID {d.Key.ProcessId} ({source}, {d.Reason}): {first.Message}. Resztę dokończy watcher."));
                            break;
                        case OperationStatus.Failed:
                            _ruleEngine.ReportAttempt(rule.Id, d.Key, now,
                                first.Status.IsRetryable() ? Services.RuleAttemptOutcome.FailedRetryable : Services.RuleAttemptOutcome.FailedPermanent);
                            AddLog(L($"Rule '{rule.Name}' failed on PID {d.Key.ProcessId} via {source} ({d.Reason}): {first.Message}.", $"Reguła '{rule.Name}' nie powiodła się dla PID {d.Key.ProcessId} ({source}, {d.Reason}): {first.Message}."));
                            break;
                        default:
                            // Skips (identity mismatch, no permission, unsupported, suspended...):
                            // never present as applied; never hot-looped.
                            _ruleEngine.ReportAttempt(rule.Id, d.Key, now, Services.RuleAttemptOutcome.FailedPermanent);
                            AddLog(L($"Rule '{rule.Name}' skipped PID {d.Key.ProcessId} via {source} ({d.Reason}): {first.Status} — {first.Message}.", $"Reguła '{rule.Name}' pominęła PID {d.Key.ProcessId} ({source}, {d.Reason}): {first.Status} — {first.Message}."));
                            break;
                    }
                }
                else
                {
                    var result = _sessionRestore.RestoreForProcess(d.ProcessName, _processService, _cpuSetMap, new[] { d.Key });
                    if (result.Restored > 0 || result.PartiallyRestored > 0)
                    {
                        _ruleEngine.MarkApplied(rule.Id, d.Key, now);
                        // P1-1: suspend only this instance, not its same-name siblings.
                        _optimizationService.SuspendInstance(d.Key, TimeSpan.FromMinutes(5), "smart rule session restore");
                        _optimizationService.ClearProfileCacheForKey(d.Key);
                        AddLog(L($"Rule '{rule.Name}' restored session for PID {d.Key.ProcessId} via {source} ({d.Reason}): {result.Message}", $"Reguła '{rule.Name}' przywróciła sesję PID {d.Key.ProcessId} ({source}, {d.Reason}): {result.Message}"));
                    }
                    else if (result.Failed > 0)
                    {
                        _ruleEngine.ReportAttempt(rule.Id, d.Key, now, Services.RuleAttemptOutcome.FailedRetryable);
                        AddLog(L($"Rule '{rule.Name}' restore failed for PID {d.Key.ProcessId} via {source} ({d.Reason}): {result.Message}", $"Reguła '{rule.Name}' nie przywróciła sesji PID {d.Key.ProcessId} ({source}, {d.Reason}): {result.Message}"));
                    }
                    else
                    {
                        _ruleEngine.ReportAttempt(rule.Id, d.Key, now, Services.RuleAttemptOutcome.FailedPermanent);
                        AddLog(L($"Rule '{rule.Name}' restore skipped for PID {d.Key.ProcessId} via {source} ({d.Reason}): {result.Message}", $"Reguła '{rule.Name}' pominęła przywracanie PID {d.Key.ProcessId} ({source}, {d.Reason}): {result.Message}"));
                    }
                }
            }
            catch (Exception ex)
            {
                _ruleEngine.ReportAttempt(rule.Id, d.Key, now, Services.RuleAttemptOutcome.FailedRetryable);
                ThrottledRuleLog(rule, $"Rule '{rule.Name}' failed: {ex.Message}", minutes: 1);
            }

            RefreshRuleStatus();
        }

        private int ApplyProfileToRunningProcesses(ProcessProfile profile, bool force)
        {
            if (IsSafeMode)
            {
                AddLog(L("Safe mode: manual apply blocked, OS mutations disabled (--safe-mode).", "Tryb bezpieczny: ręczne stosowanie zablokowane, zmiany w systemie wyłączone (--safe-mode)."));
                return 0;
            }

            var result = _optimizationService.ApplyProfileToRunningProcesses(profile, AppSettings.AllowRealtimePriority, force);
            _operationHistory.RecordBatch(result, force ? "manual profile action" : "background profile watcher");
            LogOptimizationBatch(result, force ? "manual profile action" : "background profile watcher");
            return result.Successful;
        }

        /// <summary>
        /// Picks the profile the engine would use for a process row: enabled
        /// before disabled, a path-bound profile matching the EXE before
        /// name-only, newest first.
        /// </summary>
        private ProcessProfile? ResolveProfileForUi(string processName, string? executablePath)
        {
            string name = ProfileService.NormalizeProcessName(processName);
            return _profiles
                .Where(p => string.Equals(ProfileService.NormalizeProcessName(p.ProcessName), name, StringComparison.OrdinalIgnoreCase))
                .Where(p => string.IsNullOrWhiteSpace(p.ExecutablePath)
                    || string.IsNullOrWhiteSpace(executablePath)
                    || ProfileService.MatchesIdentity(p, processName, executablePath))
                .OrderByDescending(p => p.IsEnabled)
                .ThenByDescending(p => !string.IsNullOrWhiteSpace(p.ExecutablePath) && !string.IsNullOrWhiteSpace(executablePath))
                .ThenByDescending(p => p.UpdatedAt)
                .FirstOrDefault();
        }

        private void ClearAppliedProfileCache(string processName)
        {
            _optimizationService.ClearProfileCacheForProcess(processName);
        }

        private OptimizationMode NormalizeOptimizationMode(OptimizationMode mode)
        {
#pragma warning disable CS0618
            return mode == OptimizationMode.Exclusive ? OptimizationMode.Affinity : mode;
#pragma warning restore CS0618
        }

        private string GetModeTag(OptimizationMode mode)
        {
            return NormalizeOptimizationMode(mode) == OptimizationMode.CpuSets ? "CPU Sets" : "Affinity";
        }

        private void SetAllCores(bool isChecked)
        {
            foreach (var core in Cores)
            {
                core.IsChecked = isChecked;
            }
        }

        private void DisableSmtThreads()
        {
            foreach (var core in Cores.Where(c => c.IsThread))
            {
                core.IsChecked = false;
            }
        }

        private void DisableEfficiencyCores()
        {
            foreach (var core in Cores.Where(c => c.IsECore))
            {
                core.IsChecked = false;
            }
        }

        private void RefreshCorePresets()
        {
            CorePresets.Clear();
            var dtos = Cores.Select(c => new CoreInfoDto
            {
                Index = c.Index,
                CoreIndex = c.CoreIndex,
                IsThread = c.IsThread,
                IsECore = c.IsECore,
                IsClassificationUnknown = c.IsClassificationUnknown
            }).ToList();

            foreach (var preset in Services.CorePresets.Build(dtos, IsPolishLanguage))
            {
                CorePresets.Add(preset);
            }
        }

        private void RefreshTopologyLimitation()
        {
            try
            {
                var report = _hardwareService.GetTopologyReport(Cores.ToList());
                TopologyLimitation = report.LimitedToGroup0 ? report.LimitationMessage : null;
            }
            catch
            {
                TopologyLimitation = null;
            }
        }

        private void ApplyCorePreset(string? presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId)) return;
            var preset = CorePresets.FirstOrDefault(p => p.Id == presetId);
            if (preset == null || !preset.IsAvailable)
            {
                AddLog(L($"Core preset '{presetId}' is not available on this CPU.", $"Preset rdzeni '{presetId}' nie jest dostępny na tym CPU."));
                return;
            }

            var wanted = new HashSet<int>(preset.LogicalProcessors);
            foreach (var core in Cores)
            {
                core.IsChecked = wanted.Contains(core.Index);
            }

            AddLog(L($"Core preset '{preset.DisplayName}' applied: {wanted.Count} logical processor(s) selected.", $"Zastosowano preset '{preset.DisplayName}': wybrane procesory logiczne: {wanted.Count}."));
        }

        /// <summary>
        /// Informational-tab preset preview: highlights logical processors and logs
        /// the range. Never touches the editor working selection (Cores[].IsChecked)
        /// and never mutates the OS. Clicking the active preview again clears it.
        /// </summary>
        private void PreviewCorePreset(string? presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId)) return;
            if (PreviewPresetId == presetId)
            {
                PreviewPresetId = null;
                PreviewLogicalProcessors = System.Array.Empty<int>();
                OnPropertyChanged(nameof(PreviewLogicalProcessors));
                OnPropertyChanged(nameof(PreviewPresetSummary));
                return;
            }

            var preset = CorePresets.FirstOrDefault(p => p.Id == presetId);
            if (preset == null || !preset.IsAvailable)
            {
                AddLog(L($"Core preset '{presetId}' is not available on this CPU.", $"Preset rdzeni '{presetId}' nie jest dostępny na tym CPU."));
                return;
            }

            PreviewPresetId = presetId;
            PreviewLogicalProcessors = preset.LogicalProcessors.ToList();
            OnPropertyChanged(nameof(PreviewLogicalProcessors));
            OnPropertyChanged(nameof(PreviewPresetSummary));
            AddLog(L($"Preset preview '{preset.DisplayName}': {PreviewLogicalProcessors.Count} logical processor(s) highlighted (no changes applied).", $"Podgląd presetu '{preset.DisplayName}': podświetlone procesory logiczne: {PreviewLogicalProcessors.Count} (bez zmian)."));
        }

        public string PreviewPresetSummary
        {
            get
            {
                if (PreviewPresetId == null) return string.Empty;
                var preset = CorePresets.FirstOrDefault(p => p.Id == PreviewPresetId);
                if (preset == null) return string.Empty;
                return IsPolishLanguage
                    ? $"Podgląd: {preset.DisplayName} — {PreviewLogicalProcessors.Count} procesorów"
                    : $"Preview: {preset.DisplayName} — {PreviewLogicalProcessors.Count} processors";
            }
        }

        private void DuplicateSelectedProfile()
        {
            if (SelectedSavedProfile == null) return;
            var copy = ProfileService.DuplicateProfile(SelectedSavedProfile);
            _profiles.Add(copy);
            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            RefreshProfileConflicts();
            SelectedSavedProfile = SavedProfiles.FirstOrDefault(p => p.Id == copy.Id);
            AddLog(L($"Duplicated profile '{copy.ProcessName}' (starts disabled).", $"Zduplikowano profil '{copy.ProcessName}' (domyślnie wyłączony)."));
        }

        private void ExportProfilesToFile()
        {
            try
            {
                using var dialog = new System.Windows.Forms.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = "processcoreoptimizer-profiles.json",
                    Title = "Export profiles"
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                ProfileService.ExportProfiles(dialog.FileName, _profiles);
                AddLog(L($"Exported {_profiles.Count} profile(s) to '{dialog.FileName}'.", $"Wyeksportowano profile ({_profiles.Count}) do '{dialog.FileName}'."));
            }
            catch (Exception ex)
            {
                AddLog(L($"Profile export failed: {ex.Message}", $"Eksport profili nie powiódł się: {ex.Message}"));
            }
        }

        private void ImportProfilesFromFile()
        {
            try
            {
                using var dialog = new System.Windows.Forms.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    Title = "Import profiles"
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var result = _profileService.ImportProfiles(dialog.FileName);
                _profiles = _profileService.LoadProfiles();
                RefreshSavedProfilesView();
                RefreshProfileConflicts();
                ClearAllAppliedProfileCache();

                if (result.Success) AddLog(L($"Imported {result.Imported} profile(s).", $"Zaimportowano profile: {result.Imported}."));
                else AddLog(L($"Profile import: {result.Imported} imported, {result.SkippedInvalid} invalid. {string.Join("; ", result.Errors)}", $"Import profili: zaimportowano {result.Imported}, nieprawidłowe: {result.SkippedInvalid}. {string.Join("; ", result.Errors)}"));
            }
            catch (Exception ex)
            {
                AddLog(L($"Profile import failed: {ex.Message}", $"Import profili nie powiódł się: {ex.Message}"));
            }
        }

        private void CopySelectedPid()
        {
            int? pid = SelectedInstanceRow?.ProcessId ?? SelectedProcessDetails?.RepresentativePid;
            if (pid == null || pid == 0)
            {
                AddLog(L("Copy PID: no process selected.", "Kopiuj PID: nie wybrano procesu."));
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(pid.Value.ToString());
                AddLog(L($"Copied PID {pid.Value} to clipboard.", $"Skopiowano PID {pid.Value} do schowka."));
            }
            catch (Exception ex)
            {
                AddLog(L($"Copy PID failed: {ex.Message}", $"Kopiowanie PID nie powiodło się: {ex.Message}"));
            }
        }

        private void CopySelectedExePath()
        {
            string? path = SelectedInstanceRow?.ExecutablePath ?? SelectedProcessDetails?.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AddLog(L("Copy EXE path: unavailable for the selected process (OS refused the read).", "Kopiuj ścieżkę EXE: niedostępne dla wybranego procesu (system odmówił odczytu)."));
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(path);
                AddLog(L($"Copied EXE path to clipboard: {path}", $"Skopiowano ścieżkę EXE do schowka: {path}"));
            }
            catch (Exception ex)
            {
                AddLog(L($"Copy EXE path failed: {ex.Message}", $"Kopiowanie ścieżki EXE nie powiodło się: {ex.Message}"));
            }
        }

        private void OpenSelectedFileLocation()
        {
            string? path = SelectedInstanceRow?.ExecutablePath ?? SelectedProcessDetails?.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AddLog(L("Open file location: unavailable for the selected process (OS refused the read).", "Otwórz lokalizację pliku: niedostępne dla wybranego procesu (system odmówił odczytu)."));
                return;
            }

            // Observation only: reveals the local folder, never mutates the process.
            try
            {
                string target = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
                AddLog(L($"Opened file location: {path}", $"Otwarto lokalizację pliku: {path}"));
            }
            catch (Exception ex)
            {
                AddLog(L($"Open file location failed: {ex.Message}", $"Otwieranie lokalizacji pliku nie powiodło się: {ex.Message}"));
            }
        }

        private void RefreshSelectedDetails()
        {
            // Observation only: re-reads live details for the current selection.
            string? name = SelectedInstanceRow?.Name ?? SelectedProcess?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                AddLog(L("Details: no process selected.", "Szczegóły: nie wybrano procesu."));
                return;
            }

            SelectedProcessDetails = BuildProcessDetails(name);
            AddLog(L($"Details refreshed for '{name}' (see panel below).", $"Odświeżono szczegóły '{name}' (panel poniżej)."));
        }

        private void PreviewSelectedChanges()
        {
            // Read-only by design: Compare never mutates, it only reads current state.
            try
            {
                ProcessProfile? profile = null;
                int pid = 0;
                if (SelectedInstanceRow != null)
                {
                    pid = SelectedInstanceRow.ProcessId;
                    profile = _profiles.FirstOrDefault(p => p.IsEnabled
                        && ProfileService.MatchesIdentity(p, SelectedInstanceRow.Name, SelectedInstanceRow.ExecutablePath));
                }
                else if (SelectedProcess != null)
                {
                    pid = SelectedProcess.Id;
                    profile = _profiles.FirstOrDefault(p => p.IsEnabled && p.ProcessName.Equals(SelectedProcess.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (profile == null)
                {
                    AddLog(L("Preview: no enabled profile matches the selection. Create one first.", "Podgląd: żaden włączony profil nie pasuje do zaznaczenia. Najpierw utwórz profil."));
                    return;
                }

                var preview = ProfilePreviewService.Compare(profile, pid, _cpuSetMap, _processService, AppSettings.AllowRealtimePriority);
                AddLog(L($"Preview '{profile.DisplayName}' vs PID {pid}: {preview.Summary}", $"Podgląd '{profile.DisplayName}' dla PID {pid}: {preview.Summary}"));
                foreach (var row in preview.Rows)
                    AddLog($"  {row.Action}: requested {row.Requested}, current {row.Current} — {(row.Matches ? "already matches" : "differs")}. {row.Detail}");
            }
            catch (Exception ex)
            {
                AddLog(L($"Preview failed: {ex.Message}", $"Podgląd nie powiódł się: {ex.Message}"));
            }
        }

        private void CreateProfileFromSelectedInstance()
        {
            var row = SelectedInstanceRow;
            if (row == null)
            {
                AddLog(L("Create profile: select a single instance (Instances view) first.", "Utwórz profil: najpierw wybierz pojedynczą instancję (widok Instancje)."));
                return;
            }

            try
            {
                var draft = ProfilePreviewService.CaptureFromLive(row.ProcessId, _cpuSetMap, _processService);
                if (draft == null)
                {
                    AddLog(L($"Create profile failed: the OS refused to read PID {row.ProcessId}.", $"Tworzenie profilu nie powiodło się: system odmówił odczytu PID {row.ProcessId}."));
                    return;
                }

                draft.ExecutablePath = row.ExecutablePath ?? draft.ExecutablePath;
                draft.IsEnabled = false;
                draft.Notes = $"Draft from PID {row.ProcessId} ({row.Name}). Review before enabling.";
                _profiles = ProfileService.ReplacePathBoundOrAdd(_profiles, draft);
                _profileService.SaveProfiles(_profiles);
                _profiles = _profileService.LoadProfiles();
                RefreshSavedProfilesView();
                RefreshProfileConflicts();
                AddLog(L($"Created disabled profile draft from PID {row.ProcessId} ('{row.Name}').", $"Utworzono wyłączony szkic profilu z PID {row.ProcessId} ('{row.Name}')."));
            }
            catch (Exception ex)
            {
                AddLog(L($"Create profile failed: {ex.Message}", $"Tworzenie profilu nie powiodło się: {ex.Message}"));
            }
        }

        private void ApplyToSelectedInstance()
        {
            // P0: an explicit instance selection applies to that PID only — never widens.
            var row = SelectedInstanceRow;
            if (row == null) return;
            if (IsSafeMode)
            {
                AddLog(L("Safe mode: apply blocked, OS mutations disabled (--safe-mode).", "Tryb bezpieczny: stosowanie zablokowane, zmiany w systemie wyłączone (--safe-mode)."));
                return;
            }

            try
            {
                var profile = _profiles
                    .Where(p => p.IsEnabled && ProfileService.MatchesIdentity(p, row.Name, row.ExecutablePath))
                    .OrderByDescending(p => !string.IsNullOrWhiteSpace(p.ExecutablePath))
                    .ThenByDescending(p => p.UpdatedAt)
                    .FirstOrDefault();
                if (profile == null)
                {
                    AddLog(L($"Apply: no enabled profile matches PID {row.ProcessId} ('{row.Name}').", $"Zastosuj: żaden włączony profil nie pasuje do PID {row.ProcessId} ('{row.Name}')."));
                    return;
                }

                var batch = _optimizationService.ApplyProfileToInstances(
                    profile, row.Name, new[] { row.Key }, AppSettings.AllowRealtimePriority, force: true);
                var first = batch.Results.FirstOrDefault();
                row.LastStatus = first?.Status.ToString() ?? "Skipped";
                AddLog(L($"Applied '{profile.DisplayName}' to PID {row.ProcessId} only: {row.LastStatus} ({batch.Successful}/{batch.Total} applied).", $"Zastosowano '{profile.DisplayName}' tylko do PID {row.ProcessId}: {row.LastStatus} (zastosowano {batch.Successful}/{batch.Total})."));
                RefreshWatcherStatus();
            }
            catch (Exception ex)
            {
                AddLog(L($"Apply to instance failed: {ex.Message}", $"Stosowanie do instancji nie powiodło się: {ex.Message}"));
            }
        }

        private void CreateNewProfile()
        {
            var draft = new ProcessProfile
            {
                ProcessName = SelectedProcess?.Name ?? SelectedInstanceRow?.Name ?? string.Empty,
                ExecutablePath = SelectedInstanceRow?.ExecutablePath,
                // All visible logical processors (0xFF was out of range on CPUs
                // with fewer than 8 threads and failed validation at apply time).
                AffinityMask = ProcessService.KnownProcessorMask(_cpuSetMap),
                Priority = "Normal",
                OptimizationMode = OptimizationMode.Affinity,
                ApplyPriority = true,
                ApplyCoreOptimization = true,
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (!ShowProfileEditor(draft, isNew: true)) return;

            _profiles = ProfileService.ReplacePathBoundOrAdd(_profiles, draft);
            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            RefreshProfileConflicts();
            AddLog(IsPolishLanguage
                ? $"Utworzono profil '{draft.ProcessName}' (wyłączony — włącz go świadomie)."
                : $"Created profile '{draft.ProcessName}' (disabled — enable it deliberately).");
        }

        private void EditSelectedProfile()
        {
            var selected = SelectedSavedProfile;
            if (selected == null)
            {
                AddLog(IsPolishLanguage ? "Edycja: najpierw wybierz profil." : "Edit: select a profile first.");
                return;
            }

            // Edit a clone: cancel leaves the saved profile untouched.
            var draft = new ProcessProfile
            {
                Id = selected.Id,
                SchemaVersion = selected.SchemaVersion,
                ProcessName = selected.ProcessName,
                DisplayName = selected.DisplayName,
                ExecutablePath = selected.ExecutablePath,
                AffinityMask = selected.AffinityMask,
                Priority = selected.Priority,
                OptimizationMode = selected.OptimizationMode,
                ApplyPriority = selected.ApplyPriority,
                ApplyCoreOptimization = selected.ApplyCoreOptimization,
                EcoQos = selected.EcoQos,
                IsEnabled = selected.IsEnabled,
                Notes = selected.Notes,
                CreatedAt = selected.CreatedAt,
                UpdatedAt = selected.UpdatedAt
            };

            if (!ShowProfileEditor(draft, isNew: false)) return;

            // P0: write back by stable Id, never by name.
            var existing = ProfileService.FindById(_profiles, draft.Id);
            if (existing == null)
            {
                AddLog(IsPolishLanguage ? "Edycja: profil już nie istnieje." : "Edit: profile no longer exists.");
                return;
            }

            existing.ExecutablePath = draft.ExecutablePath;
            existing.AffinityMask = draft.AffinityMask;
            existing.Priority = draft.Priority;
            existing.DisplayPriority = TranslatePriority(NormalizePriority(draft.Priority));
            existing.OptimizationMode = draft.OptimizationMode;
            existing.ApplyPriority = draft.ApplyPriority;
            existing.ApplyCoreOptimization = draft.ApplyCoreOptimization;
            existing.EcoQos = draft.EcoQos;
            existing.IsEnabled = draft.IsEnabled;
            existing.Notes = draft.Notes;
            existing.UpdatedAt = DateTime.UtcNow;

            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            RefreshProfileConflicts();
            ClearAppliedProfileCache(existing.ProcessName);
            _optimizationService.ClearSuspensionsForProcess(existing.ProcessName);
            AddLog(IsPolishLanguage
                ? $"Zaktualizowano profil '{existing.ProcessName}'."
                : $"Updated profile '{existing.ProcessName}'.");
        }

        private void ToggleSelectedProfileEnabled()
        {
            var selected = SelectedSavedProfile;
            if (selected == null) return;
            var existing = ProfileService.FindById(_profiles, selected.Id);
            if (existing == null) return;
            existing.IsEnabled = !existing.IsEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            RefreshProfileConflicts();
            if (!existing.IsEnabled) ClearAppliedProfileCache(existing.ProcessName);
            AddLog(IsPolishLanguage
                ? $"Profil '{existing.ProcessName}': {(existing.IsEnabled ? "włączony" : "wyłączony")}."
                : $"Profile '{existing.ProcessName}': {(existing.IsEnabled ? "enabled" : "disabled")}.");
        }

        private bool ShowProfileEditor(ProcessProfile draft, bool isNew)
        {
            try
            {
                var owner = WpfApplication.Current.MainWindow as Views.MainWindow;
                var window = new Views.ProfileEditorWindow(
                    draft, isNew, AppSettings.AllowRealtimePriority, IsPolishLanguage, _profiles);
                if (owner != null) window.Owner = owner;
                return window.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                AddLog(L($"Profile editor failed: {ex.Message}", $"Błąd edytora profilu: {ex.Message}"));
                return false;
            }
        }

        private void CreateNewRule()
        {
            var draft = new SmartRule
            {
                ProcessName = SelectedProcess?.Name ?? SelectedInstanceRow?.Name ?? string.Empty,
                ExecutablePath = SelectedInstanceRow?.ExecutablePath,
                Trigger = RuleTrigger.OnLaunch,
                Action = RuleAction.ApplyProfile,
                DelaySeconds = 0,
                CooldownSeconds = 30,
                Priority = 100,
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (!ShowRuleEditor(draft, isNew: true)) return;

            _rules.Add(draft);
            if (SaveRules())
                AddLog(IsPolishLanguage
                    ? $"Utworzono regułę '{draft.Name}' (wyłączona — włącz ją świadomie)."
                    : $"Created rule '{draft.Name}' (disabled — enable it deliberately).");
        }

        private void EditSelectedRule()
        {
            var selected = SelectedRule;
            if (selected == null)
            {
                AddLog(IsPolishLanguage ? "Edycja: najpierw wybierz regułę." : "Edit: select a rule first.");
                return;
            }

            var draft = new SmartRule
            {
                Id = selected.Id,
                Name = selected.Name,
                ProcessName = selected.ProcessName,
                ExecutablePath = selected.ExecutablePath,
                Trigger = selected.Trigger,
                Action = selected.Action,
                ProfileId = selected.ProfileId,
                DelaySeconds = selected.DelaySeconds,
                CooldownSeconds = selected.CooldownSeconds,
                Priority = selected.Priority,
                IsEnabled = selected.IsEnabled,
                Notes = selected.Notes,
                CreatedAt = selected.CreatedAt,
                UpdatedAt = selected.UpdatedAt
            };

            if (!ShowRuleEditor(draft, isNew: false)) return;

            var existing = SmartRuleService.FindById(_rules, draft.Id);
            if (existing == null)
            {
                AddLog(IsPolishLanguage ? "Edycja: reguła już nie istnieje." : "Edit: rule no longer exists.");
                return;
            }

            existing.Name = draft.Name;
            existing.ProcessName = draft.ProcessName;
            existing.ExecutablePath = draft.ExecutablePath;
            existing.Trigger = draft.Trigger;
            existing.Action = draft.Action;
            existing.ProfileId = draft.ProfileId;
            existing.DelaySeconds = draft.DelaySeconds;
            existing.CooldownSeconds = draft.CooldownSeconds;
            existing.Priority = draft.Priority;
            existing.IsEnabled = draft.IsEnabled;
            existing.Notes = draft.Notes;
            existing.UpdatedAt = DateTime.UtcNow;
            if (SaveRules())
                AddLog(IsPolishLanguage ? $"Zaktualizowano regułę '{existing.Name}'." : $"Updated rule '{existing.Name}'.");
        }

        private void DeleteSelectedRule()
        {
            var selected = SelectedRule;
            if (selected == null) return;
            _rules = SmartRuleService.RemoveById(_rules, selected.Id);
            _ruleEngine.ClearOverride(selected.Id);
            SelectedRule = null;
            if (SaveRules())
                AddLog(IsPolishLanguage ? $"Usunięto regułę '{selected.Name}'." : $"Deleted rule '{selected.Name}'.");
        }

        private void ToggleSelectedRuleEnabled()
        {
            var selected = SelectedRule;
            if (selected == null) return;
            var existing = SmartRuleService.FindById(_rules, selected.Id);
            if (existing == null) return;
            existing.IsEnabled = !existing.IsEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            if (SaveRules())
                AddLog(IsPolishLanguage
                    ? $"Reguła '{existing.Name}': {(existing.IsEnabled ? "włączona" : "wyłączona")}."
                    : $"Rule '{existing.Name}': {(existing.IsEnabled ? "enabled" : "disabled")}.");
        }

        private void ClearSelectedRuleOverride()
        {
            var selected = SelectedRule;
            if (selected == null) return;
            _ruleEngine.ClearOverride(selected.Id);
            AddLog(IsPolishLanguage
                ? $"Wyczyszczono obejście reguły '{selected.Name}'."
                : $"Cleared override for rule '{selected.Name}'.");
        }

        private bool SaveRules()
        {
            if (!_ruleService.TrySaveRules(_rules, out string? error))
            {
                AddLog(IsPolishLanguage
                    ? $"Reguły NIE zostały zapisane: {error}"
                    : $"Rules NOT saved: {error}");
                RefreshRuleStatus();
                return false;
            }

            _rules = _ruleService.LoadRules();
            _rulesSnapshot = _rules.ToList();
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                Rules.Clear();
                foreach (var rule in _rules.OrderBy(r => r.Priority).ThenBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase))
                    Rules.Add(rule);
            });
            RefreshRuleStatus();
            return true;
        }

        private bool ShowRuleEditor(SmartRule draft, bool isNew)
        {
            try
            {
                var owner = WpfApplication.Current.MainWindow as Views.MainWindow;
                var window = new Views.RuleEditorWindow(draft, isNew, _profiles, IsPolishLanguage);
                if (owner != null) window.Owner = owner;
                return window.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                AddLog(L($"Rule editor failed: {ex.Message}", $"Błąd edytora reguł: {ex.Message}"));
                return false;
            }
        }

        private void CaptureLiveSettings()
        {
            if (SelectedProcess == null) return;
            try
            {
                Process[] candidates = Process.GetProcessesByName(ProfileService.NormalizeProcessName(SelectedProcess.Name));
                try
                {
                    var live = candidates.FirstOrDefault();
                    if (live == null)
                    {
                        AddLog(L($"Capture failed: '{SelectedProcess.Name}' is no longer running.", $"Przechwycenie nie powiodło się: '{SelectedProcess.Name}' już nie działa."));
                        return;
                    }

                    var draft = ProfilePreviewService.CaptureFromLive(live.Id, _cpuSetMap, _processService);
                    if (draft == null)
                    {
                        AddLog(L($"Capture failed: the OS refused to read '{SelectedProcess.Name}'.", $"Przechwycenie nie powiodło się: system odmówił odczytu '{SelectedProcess.Name}'."));
                        return;
                    }

                    _profiles.RemoveAll(x => x.ProcessName.Equals(draft.ProcessName, StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(x.ExecutablePath) && string.IsNullOrWhiteSpace(draft.ExecutablePath));
                    _profiles.Add(draft);
                    _profileService.SaveProfiles(_profiles);
                    _profiles = _profileService.LoadProfiles();
                    RefreshSavedProfilesView();
                    RefreshProfileConflicts();
                    AddLog(L($"Captured live settings of '{draft.ProcessName}' as a new disabled profile.", $"Przechwycono bieżące ustawienia '{draft.ProcessName}' jako nowy wyłączony profil."));
                }
                finally
                {
                    foreach (var p in candidates) p.Dispose();
                }
            }
            catch (Exception ex)
            {
                AddLog(L($"Capture failed: {ex.Message}", $"Przechwycenie nie powiodło się: {ex.Message}"));
            }
        }

        private void ReloadRules()
        {
            _rules = _ruleService.LoadRules();
            _rulesSnapshot = _rules.ToList();
            Rules.Clear();
            foreach (var rule in _rules.OrderBy(r => r.Priority).ThenBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase))
                Rules.Add(rule);
            RefreshRuleStatus();
        }

        private void RefreshProfileConflicts()
        {
            ProfileConflicts.Clear();
            foreach (var conflict in Services.ProfileConflicts.FindConflicts(_profiles))
            {
                ProfileConflicts.Add(conflict);
            }

            OnPropertyChanged(nameof(HasProfileConflicts));
            ConflictSummaryText = ProfileConflicts.Count == 0
                ? string.Empty
                : IsPolishLanguage
                    ? $"{ProfileConflicts.Count} konflikt(ów) profili: {string.Join(", ", ProfileConflicts.Select(c => c.ProcessName))}. Wygrywa najnowszy / path-bound; sprawdź w Profilach."
                    : $"{ProfileConflicts.Count} profile conflict(s): {string.Join(", ", ProfileConflicts.Select(c => c.ProcessName))}. Newest/path-bound wins; review in Profiles.";
        }

        private void RefreshWatcherStatus()
        {
            var last = _profileWatcher.LastRun;
            if (last.AtUtc == default)
            {
                WatcherStatusText = IsPolishLanguage ? "Watcher bezczynny." : "Watcher idle.";
                return;
            }

            string time = last.AtUtc.ToLocalTime().ToString("HH:mm:ss");
            string source = LocalizeSource(last.Source);
            WatcherStatusText = (IsPolishLanguage
                ? $"Ostatni przebieg {time} ({source}): {last.FullyApplied}/{last.Total} zastosowano, {last.Failed} błędów, {last.Skipped} pominięto w {last.Duration.TotalMilliseconds:F0} ms."
                : $"Last pass {time} ({source}): {last.FullyApplied}/{last.Total} applied, {last.Failed} failed, {last.Skipped} skipped in {last.Duration.TotalMilliseconds:F0} ms.")
                + (string.IsNullOrWhiteSpace(last.Error) ? string.Empty : (IsPolishLanguage ? $" Błąd: {last.Error}" : $" Error: {last.Error}"));
        }

        private void RefreshRecentOperations()
        {
            RecentOperations.Clear();
            foreach (var entry in _operationHistory.Recent(50))
            {
                RecentOperations.Add(entry);
            }
        }

        private bool MatchesProcessFilter(string name, string? executablePath = null, bool isOptimized = false)
        {
            if (_processOnlyOptimized && !isOptimized) return false;
            if (_processOnlyWithProfile && !isOptimized) return false;
            if (string.IsNullOrWhiteSpace(_processFilterText)) return true;
            string filter = _processFilterText.Trim();
            return name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (executablePath?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
        }

        private bool MatchesProfileFilter(ProcessProfile profile)
        {
            if (string.IsNullOrWhiteSpace(_profileFilterText)) return true;
            string filter = _profileFilterText.Trim();
            return profile.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (profile.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
                || (profile.ExecutablePath?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
        }

        private void RefreshProcessFilter()
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                for (int i = Processes.Count - 1; i >= 0; i--)
                {
                    var item = Processes[i];
                    if (!MatchesProcessFilter(item.Name, item.ExecutablePath, item.IsOptimized)) Processes.RemoveAt(i);
                }

                RebuildInstanceRows();
            });
        }

        /// <summary>
        /// Rebuilds the flat instance list from the last UI scan (local op, no rescan).
        /// Honors the same filter text/flags as the grouped view.
        /// </summary>
        private void RebuildInstanceRows()
        {
            var scan = _lastUiScan;
            if (scan == null) return;

            var rows = ProcessRowBuilder.BuildInstanceRows(
                scan.InstanceDetails,
                _profiles,
                translatePriority: raw => TranslatePriority(NormalizePriority(raw)));

            InstanceRows.Clear();
            foreach (var row in rows.Where(r =>
                MatchesProcessFilter(r.Name, r.ExecutablePath, !string.IsNullOrEmpty(r.ProfileName))))
            {
                InstanceRows.Add(row);
            }
        }

        private ProcessDetails BuildProcessDetails(string processName)
        {
            var details = new ProcessDetails { Name = processName };
            try
            {
                string normalized = ProfileService.NormalizeProcessName(processName);
                Process[] candidates = Process.GetProcessesByName(normalized);
                try
                {
                    var live = candidates.FirstOrDefault(p => { try { return !p.HasExited; } catch { return false; } });
                    if (live == null) return details;

                    details.RepresentativePid = live.Id;
                    details.InstanceCount = candidates.Length;
                    try { details.StartTimeUtc = live.StartTime.ToUniversalTime(); } catch { }
                    details.ExecutablePath = ProcessIdentityValidator.TryGetProcessPath(live);
                    try { details.Priority = live.PriorityClass.ToString(); } catch { }

                    var selection = _processService.GetCurrentCoreSelection(live.Id, _cpuSetMap);
                    if (selection.Success)
                    {
                        details.AffinityMask = selection.Mask;
                        details.AffinitySource = selection.Source;
                    }

                    var profile = _profiles.FirstOrDefault(p => p.IsEnabled && p.ProcessName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                    details.AssignedProfileName = profile?.ProcessName;
                    var last = _operationHistory.Recent(200).FirstOrDefault(e => e.ProcessName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                    if (last != null)
                    {
                        details.LastStatus = last.Status;
                        details.LastMessage = last.Message;
                    }
                }
                finally
                {
                    foreach (var p in candidates) p.Dispose();
                }
            }
            catch
            {
                // Best-effort diagnostics: never break selection.
            }

            return details;
        }

        private void CleanupOptimizationDiagnosticLogCache()
        {
            if (_lastOptimizationDiagnosticLogUtc.Count == 0) return;

            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
            foreach (var key in _lastOptimizationDiagnosticLogUtc
                         .Where(pair => pair.Value < cutoff)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _lastOptimizationDiagnosticLogUtc.Remove(key);
            }

            // Bound the cache: throttle keys no longer include PID, but a fleet of
            // distinct process names could still grow it. Keep the newest 500.
            const int maxEntries = 500;
            if (_lastOptimizationDiagnosticLogUtc.Count > maxEntries)
            {
                foreach (var key in _lastOptimizationDiagnosticLogUtc
                             .OrderBy(pair => pair.Value)
                             .Take(_lastOptimizationDiagnosticLogUtc.Count - maxEntries)
                             .Select(pair => pair.Key)
                             .ToList())
                {
                    _lastOptimizationDiagnosticLogUtc.Remove(key);
                }
            }
        }

        private void LogProfileWatcherStartupSummary()
        {
            var enabledProfiles = _profiles
                .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ProcessName))
                .Select(p => p.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string fileMessage = enabledProfiles.Count == 0
                ? "Background profile watcher active. No enabled profiles configured."
                : $"Background profile watcher active for {enabledProfiles.Count} enabled profile(s): {string.Join(", ", enabledProfiles)}";

            LoggerService.Instance.Info(fileMessage);
            AddLog(enabledProfiles.Count == 0
                ? "Background profile watcher active. No enabled profiles configured."
                : $"Background profile watcher active: {enabledProfiles.Count} enabled profile(s).");
        }

        private void LogOptimizationBatch(OptimizationBatchResult batch, string source)
        {
            RefreshWatcherStatus();
            RefreshRecentOperations();
            if (batch.Total == 0) return;

            foreach (var result in batch.Results.Where(r => r.Status.IsSuccess()))
            {
                string message = BuildOptimizationLogMessage(result, source, success: true);
                LoggerService.Instance.Info(message);
                AddLog(IsPolishLanguage ? BuildOptimizationLogMessagePl(result, source, success: true) : message);
            }

            foreach (var result in batch.Results.Where(IsActionableOptimizationFailure))
            {
                if (!ShouldLogOptimizationDiagnostic(result, TimeSpan.FromSeconds(60))) continue;

                string message = BuildOptimizationLogMessage(result, source, success: false);
                LoggerService.Instance.Warn(message);
                AddLog(IsPolishLanguage ? BuildOptimizationLogMessagePl(result, source, success: false) : message);
            }
        }

        private static bool IsActionableOptimizationFailure(OptimizationResult result)
        {
            // Skips are routine watcher outcomes, not failures worth surfacing.
            // Partial application IS actionable: it tells the user which half failed.
            return result.Status is OperationStatus.Failed
                or OperationStatus.PartiallyApplied
                or OperationStatus.SkippedNoPermission
                or OperationStatus.Unsupported;
        }

        private bool ShouldLogOptimizationDiagnostic(OptimizationResult result, TimeSpan throttleWindow)
        {
            // Key excludes PID so a rapidly restarting process cannot spam the log
            // with a fresh key per instance.
            string key = $"{result.ProcessName}|{result.Mode}|{result.Priority}|{result.Message}|admin={result.RequiresAdmin}";
            DateTime now = DateTime.UtcNow;

            if (_lastOptimizationDiagnosticLogUtc.TryGetValue(key, out DateTime lastLogged) && now - lastLogged < throttleWindow)
            {
                return false;
            }

            _lastOptimizationDiagnosticLogUtc[key] = now;
            return true;
        }

        private string BuildOptimizationLogMessage(OptimizationResult result, string source, bool success)
        {
            string profileName = string.IsNullOrWhiteSpace(result.ProcessName) ? "unknown" : result.ProcessName;
            string coreStatus = result.CoreApplied ? "core=applied" : "core=not-applied";
            string priorityStatus = result.PriorityApplied ? "priority=applied" : "priority=not-applied";
            string adminHint = result.RequiresAdmin ? " Requires administrator rights or a protected process denied access." : string.Empty;

            if (success)
            {
                string qualifier = result.Status == OperationStatus.PartiallyApplied ? " (partial: one half failed, will retry)" : string.Empty;
                return $"Applied profile '{profileName}' via {source}: PID={result.ProcessId}, mode={GetModeTag(result.Mode)}, priority={TranslatePriority(result.Priority)}, {coreStatus}, {priorityStatus}{qualifier}.";
            }

            return $"Failed to apply profile '{profileName}' via {source}: PID={result.ProcessId}, mode={GetModeTag(result.Mode)}, priority={TranslatePriority(result.Priority)}, status={result.Status}, reason={result.Message}.{adminHint}";
        }

        /// <summary>UI label for an internal pass source (file logs keep the English tag).</summary>
        private string LocalizeSource(string? source)
        {
            if (!IsPolishLanguage || string.IsNullOrEmpty(source)) return source ?? string.Empty;
            return source switch
            {
                "background profile watcher" => "watcher profili w tle",
                "manual profile action" => "ręczna akcja profilu",
                _ => source
            };
        }

        /// <summary>Polish UI variant of <see cref="BuildOptimizationLogMessage"/>; the file log stays English.</summary>
        private string BuildOptimizationLogMessagePl(OptimizationResult result, string source, bool success)
        {
            string profileName = string.IsNullOrWhiteSpace(result.ProcessName) ? "nieznany" : result.ProcessName;
            string sourcePl = LocalizeSource(source);
            string coreStatus = result.CoreApplied ? "rdzenie=zastosowane" : "rdzenie=niezastosowane";
            string priorityStatus = result.PriorityApplied ? "priorytet=zastosowany" : "priorytet=niezastosowany";
            string adminHint = result.RequiresAdmin ? " Wymaga uprawnień administratora albo chroniony proces odmówił dostępu." : string.Empty;

            if (success)
            {
                string qualifier = result.Status == OperationStatus.PartiallyApplied ? " (częściowo: jedna część się nie powiodła, ponowię)" : string.Empty;
                return $"Zastosowano profil '{profileName}' ({sourcePl}): PID={result.ProcessId}, tryb={GetModeTag(result.Mode)}, priorytet={TranslatePriority(result.Priority)}, {coreStatus}, {priorityStatus}{qualifier}.";
            }

            return $"Nie udało się zastosować profilu '{profileName}' ({sourcePl}): PID={result.ProcessId}, tryb={GetModeTag(result.Mode)}, priorytet={TranslatePriority(result.Priority)}, status={result.Status}, powód={result.Message}.{adminHint}";
        }

        /// <summary>Picks the UI-language variant of a user-facing log line.</summary>
        private string L(string en, string pl) => IsPolishLanguage ? pl : en;

        private void AddLog(string message)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                ActionLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (ActionLogs.Count > 100) ActionLogs.RemoveAt(0);
            });
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                await Task.Delay(2000, _updateCts.Token).ConfigureAwait(false);
                if (NeedsFirstRunWizard) return;
                var update = await _updateSession.TryRunAutomaticCheckAsync(
                    AppSettings.CheckForUpdates && !ProcessCoreOptimizer.WPF.App.StartupOptions.DisableUpdateCheck,
                    _updateCts.Token).ConfigureAwait(false);
                if (update == null) return;
                if (UpdateCheckSession.ShouldPresentUpdateDialog(update))
                {
                    AddLog(L($"Update available: v{update.LatestVersion}", $"Dostępna aktualizacja: v{update.LatestVersion}"));
                    PresentOrDeferUpdate(update.LatestVersion, update.ReleaseUrl);
                }
                else if (string.IsNullOrWhiteSpace(update.Error))
                {
                    AddLog(L("App is up to date.", "Program jest aktualny."));
                }
                // Automatic errors stay silent; the manual check reports them.
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LoggerService.Instance.Debug($"Update check failed: {ex.Message}");
            }
        }

        private void PresentOrDeferUpdate(string latestVersion, string releaseUrl)
        {
            var window = WpfApplication.Current?.MainWindow;
            bool visible = window?.IsVisible == true;
            bool minimized = window?.WindowState == WindowState.Minimized;
            if (UpdateCheckSession.ShouldDeferUpdateDialog(visible, minimized, NeedsFirstRunWizard, _updateDialogShown))
            {
                _pendingUpdateVersion = latestVersion;
                SetUpdateStatus(UpdateStatusKind.Available, latestVersion);
                return;
            }
            ShowUpdateDialog(latestVersion, releaseUrl);
        }

        /// <summary>Called when the main window is foregrounded: presents a deferred update once.</summary>
        public void TryPresentPendingUpdate()
        {
            if (string.IsNullOrWhiteSpace(_pendingUpdateVersion) || _updateDialogShown) return;
            var window = WpfApplication.Current?.MainWindow;
            bool visible = window?.IsVisible == true;
            bool minimized = window?.WindowState == WindowState.Minimized;
            if (UpdateCheckSession.ShouldDeferUpdateDialog(visible, minimized, NeedsFirstRunWizard, false)) return;
            string version = _pendingUpdateVersion;
            _pendingUpdateVersion = null;
            ShowUpdateDialog(version, $"https://github.com/9Erza/ProcessCoreOptimizer/releases/tag/v{version.TrimStart('v', 'V')}");
        }

        private void ShowUpdateDialog(string latestVersion, string releaseUrl)
        {
            _updateDialogShown = true;
            _pendingUpdateVersion = null;
            SetUpdateStatus(UpdateStatusKind.Available, latestVersion);
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                Views.UpdateAvailableDialog.Present(
                    WpfApplication.Current.MainWindow as Views.MainWindow,
                    latestVersion, AppVersion, releaseUrl, IsPolishLanguage);
            });
        }

        private async Task CheckForUpdatesNowAsync()
        {
            SetUpdateStatus(UpdateStatusKind.Checking);
            try
            {
                var update = await _updateService.CheckForUpdatesAsync(AppVersion, _updateCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(update.Error))
                {
                    SetUpdateStatus(UpdateStatusKind.Failed);
                    return;
                }
                if (update.IsUpdateAvailable)
                {
                    AddLog(L($"Update available: v{update.LatestVersion}", $"Dostępna aktualizacja: v{update.LatestVersion}"));
                    ShowUpdateDialog(update.LatestVersion, update.ReleaseUrl);
                    return;
                }
                if (update.Note != null)
                {
                    SetUpdateStatus(UpdateStatusKind.NoInstaller, update.LatestVersion);
                    return;
                }
                SetUpdateStatus(UpdateStatusKind.UpToDate);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LoggerService.Instance.Debug($"Manual update check failed: {ex.Message}");
                SetUpdateStatus(UpdateStatusKind.Failed);
            }
        }
        private void ApplyLanguage(string langCode)
        {
            _localizationService.ApplyLanguage(langCode);
            OnPropertyChanged(nameof(ActivitySummary));
            OnPropertyChanged(nameof(ActivityStatus));
            OnPropertyChanged(nameof(TargetScopeLabel));
            OnPropertyChanged(nameof(TargetPanelTitle));
            OnPropertyChanged(nameof(SelectedCoreSummary));
            if (Cores.Count > 0) RefreshCorePresets();
            OnPropertyChanged(nameof(PreviewPresetSummary));
            OnPropertyChanged(nameof(UpdateStatusText));
            RefreshRecentOperations();
            RefreshAvailablePriorities();
            RefreshDisplayedPriorities();
            RefreshProfileConflicts();
            RefreshWatcherStatus();
            RefreshRuleStatus();
            UpdateProfileWhy();
            OnPropertyChanged(nameof(CpuVendorText));
            OnPropertyChanged(nameof(CurrentPrivilegesText));
            _ = RefreshStartupStatusAsync();
            try
            {
                (WpfApplication.Current.MainWindow as Views.MainWindow)?.ApplyTrayLanguage();
            }
            catch { }
        }

        private void RefreshAvailablePriorities()
        {
            AvailablePriorities.Clear();
            foreach (string priority in PriorityService.GetDisplayPriorities(AppSettings.Language, AppSettings.AllowRealtimePriority))
            {
                AvailablePriorities.Add(priority);
            }
        }

        private void RefreshDisplayedPriorities()
        {
            foreach (var process in Processes)
            {
                process.Priority = TranslatePriority(process.Priority);
            }

            RefreshSavedProfilesView();

            string currentRawSelected = NormalizePriority(_selectedPriority);
            _selectedPriority = TranslatePriority(currentRawSelected);
            OnPropertyChanged(nameof(SelectedPriority));
        }

        private void RefreshSavedProfilesView()
        {
            _profiles = ProfileService.SanitizeProfiles(_profiles).ToList();

            foreach (var profile in _profiles)
            {
                profile.Priority = NormalizePriority(profile.Priority, allowRealtime: true);
                profile.OptimizationMode = NormalizeOptimizationMode(profile.OptimizationMode);
                profile.DisplayPriority = TranslatePriority(NormalizePriority(profile.Priority));
            }

            SavedProfiles.Clear();
            foreach (var profile in _profiles
                         .Where(MatchesProfileFilter)
                         .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                SavedProfiles.Add(profile);
            }

            RefreshProfileConflicts();
            UpdateProfileWhy();
        }

        private string TranslatePriority(string? rawEnum)
        {
            return PriorityService.Translate(rawEnum, AppSettings.Language);
        }

        private string NormalizePriority(string? priority, bool? allowRealtime = null)
        {
            return PriorityService.Normalize(priority, allowRealtime ?? AppSettings.AllowRealtimePriority);
        }

        private void SaveAndApplySettings(bool restartAsAdminIfNeeded)
        {
            _ = restartAsAdminIfNeeded;
            bool realtimeBefore = _realtimeCacheFlag;
            bool realtimeNow = AppSettings.AllowRealtimePriority;

            ConfigureLoggerFromSettings();
            _hwService?.Configure(AppSettings.EnableStorageSensors);
            _settingsService.SaveSettings(AppSettings);

            // Startup registration is reconciled by the coordinator (plan → execute
            // → verify), only when startup-related settings actually changed.
            string startupSignature = StartupSignature(AppSettings);
            if (!string.Equals(startupSignature, _lastStartupSignature, StringComparison.Ordinal))
            {
                _lastStartupSignature = startupSignature;
                TriggerStartupApply();
            }
            OnPropertyChanged(nameof(IsHardwareMonitorEnabled));
            RefreshAvailablePriorities();
            RefreshDisplayedPriorities();

            // Profile signatures embed the RT flag. Only the RT toggle invalidates
            // the applied-profile cache; language/log/storage toggles must not force
            // a redundant re-apply of every profile.
            if (realtimeBefore != realtimeNow)
            {
                _realtimeCacheFlag = realtimeNow;
                ClearAllAppliedProfileCache();
            }

            if (ActiveTab == "Hardware")
            {
                if (AppSettings.HardwareMonitorEnabled) StartHardwareMonitorIfVisible();
                else StopHardwareMonitor(closeSensors: true, resetMetrics: true);
            }
        }

        private void TriggerStartupApply()
        {
            _ = ApplyStartupAsync();
        }

        private async Task ApplyStartupAsync()
        {
            // No early return while busy: the coordinator serializes requests and
            // converges on the newest desired state. Dropping a request here used
            // to lose quick follow-up changes (e.g. enable + elevated) forever,
            // because the settings signature had already been consumed.
            _startupApplyInFlight++;
            IsStartupBusy = true;
            try
            {
                var result = await _startupCoordinator.ApplyLatestAsync(AppSettings.DesiredStartup).ConfigureAwait(true);
                UpdateStartupStatus(result.FinalEvaluation, result.Error ?? (result.WasElevationCancelled ? StartupUacCancelledText() : null));
                if (!string.IsNullOrWhiteSpace(result.Error) || result.WasElevationCancelled)
                {
                    AddLog(L($"Startup apply: {result.Error ?? StartupUacCancelledText()}", $"Autostart: {result.Error ?? StartupUacCancelledText()}"));
                }
                else if (result.Success)
                {
                    AddLog(L($"Startup configured: {result.FinalEvaluation.State}.", $"Autostart skonfigurowany: {result.FinalEvaluation.State}."));
                }
            }
            catch (Exception ex)
            {
                UpdateStartupStatus(null, ex.Message);
            }
            finally
            {
                _startupApplyInFlight--;
                IsStartupBusy = _startupApplyInFlight > 0;
            }
        }

        /// <summary>
        /// Reads the real registration and repairs it silently when that needs no
        /// UAC prompt (registry entry, or any change while already elevated).
        /// Repairs needing elevation are only reported, never prompted at logon.
        /// </summary>
        private async Task ReconcileStartupAsync()
        {
            try
            {
                var desired = AppSettings.DesiredStartup;
                var actual = await _startupService.ReadActualAsync(desired).ConfigureAwait(true);
                var evaluation = StartupConfigurationPlanner.Evaluate(desired, actual);
                UpdateStartupStatus(evaluation, null);

                if (IsSafeMode) return;
                if (!StartupConfigurationPlanner.CanAutoRepair(desired, evaluation, _isElevated)) return;

                AddLog(L($"Startup registration out of sync ({string.Join(", ", evaluation.Reasons)}); repairing.", $"Wpis autostartu niezgodny ({string.Join(", ", evaluation.Reasons)}); naprawiam."));
                await ApplyStartupAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                UpdateStartupStatus(null, ex.Message);
            }
        }

        private async Task RefreshStartupStatusAsync()
        {
            try
            {
                var desired = AppSettings.DesiredStartup;
                var actual = await _startupService.ReadActualAsync(desired).ConfigureAwait(true);
                var evaluation = StartupConfigurationPlanner.Evaluate(desired, actual);
                UpdateStartupStatus(evaluation, null);
            }
            catch (Exception ex)
            {
                UpdateStartupStatus(null, ex.Message);
            }
        }

        private void UpdateStartupStatus(StartupConfigurationEvaluation? evaluation, string? error)
        {
            if (evaluation == null)
            {
                StartupStatusText = StartupStateText(StartupState.Broken);
                StartupDetailText = error ?? string.Empty;
                _hasStartupConflict = true;
                OnPropertyChanged(nameof(ActivityStatus));
                return;
            }

            _hasStartupConflict = evaluation.State == StartupState.Conflict;
            OnPropertyChanged(nameof(ActivityStatus));
            StartupStatusText = StartupStateText(evaluation.State);
            var parts = evaluation.Reasons.Select(StartupReasonText).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (!string.IsNullOrWhiteSpace(error)) parts.Add(error);
            if (evaluation.State == StartupState.Registry || evaluation.State == StartupState.ElevatedTask || evaluation.State == StartupState.Disabled)
            {
                if (parts.Count == 0) parts.Add(StartupHealthyText());
            }

            StartupDetailText = string.Join(" ", parts);
        }

        private void RestartElevated()
        {
            string[] forward = StartupHelperCommand.ForwardedRestartArguments(
                Environment.GetCommandLineArgs().Skip(1).ToArray());
            // Preserve the configured window behavior across the restart.
            string modeArg = AppSettings.StartupWindowMode switch
            {
                StartupWindowMode.Minimized => "--minimized",
                StartupWindowMode.Tray => "--tray",
                _ => string.Empty
            };
            var args = forward.Concat(string.IsNullOrEmpty(modeArg) ? Enumerable.Empty<string>() : new[] { modeArg })
                .Distinct().ToArray();

            var outcome = App.RequestElevatedRestart(_startupService, args);
            AddLog(outcome switch
            {
                RestartElevatedResult.Started => "Restarting with administrator rights...",
                RestartElevatedResult.Cancelled => "Administrator restart cancelled (UAC dismissed). Still running with current privileges.",
                _ => "Administrator restart failed to start."
            });
        }

        private void ConfigureLoggerFromSettings()
        {
            LoggerService.Shared.Configure(
                AppSettings.LogEnabled,
                LogLevel.FromValue(AppSettings.LogLevelValue),
                AppSettings.LogFilePath,
                AppSettings.EnableConsoleOutput,
                AppSettings.LogSourceName);
        }

        private void ClearAllAppliedProfileCache()
        {
            _optimizationService.ClearAllCache();
        }

        private static string StartupSignature(AppSettings settings) =>
            $"{settings.StartWithWindows}|{settings.StartupWindowMode}|{settings.StartupRunElevated}";

        private string StartupStateText(StartupState state) => state switch
        {
            StartupState.Disabled => IsPolishLanguage ? "Autostart: wyłączony" : "Startup: disabled",
            StartupState.Registry => IsPolishLanguage ? "Autostart: zwykły (rejestr)" : "Startup: normal (registry)",
            StartupState.ElevatedTask => IsPolishLanguage ? "Autostart: elevated (harmonogram)" : "Startup: elevated (scheduler)",
            StartupState.Conflict => IsPolishLanguage ? "Autostart: KONFLIKT — wymaga naprawy" : "Startup: CONFLICT — repair needed",
            _ => IsPolishLanguage ? "Autostart: wymaga uwagi" : "Startup: needs attention"
        };

        private string StartupReasonText(StartupReason reason) => reason switch
        {
            StartupReason.MissingRegistry => IsPolishLanguage ? "Brak wpisu rejestru." : "Registry entry missing.",
            StartupReason.MissingScheduledTask => IsPolishLanguage ? "Brak zadania harmonogramu." : "Scheduled task missing.",
            StartupReason.WrongExecutable => IsPolishLanguage ? "Wpis wskazuje inny plik EXE." : "Entry points to a different EXE.",
            StartupReason.WrongArguments => IsPolishLanguage ? "Niezgodne argumenty startu." : "Startup arguments differ.",
            StartupReason.ExecutableMissing => IsPolishLanguage ? "Plik EXE nie istnieje." : "Executable file is missing.",
            StartupReason.TaskDisabled => IsPolishLanguage ? "Zadanie jest wyłączone." : "Task is disabled.",
            StartupReason.MissingLogonTrigger => IsPolishLanguage ? "Brak wyzwalacza logowania." : "Logon trigger missing.",
            StartupReason.MissingElevatedRunLevel => IsPolishLanguage ? "Brak poziomu HighestAvailable." : "HighestAvailable run level missing.",
            StartupReason.UnexpectedRegistryEntry => IsPolishLanguage ? "Nieoczekiwany wpis rejestru." : "Unexpected registry entry.",
            StartupReason.UnexpectedScheduledTask => IsPolishLanguage ? "Nieoczekiwane zadanie harmonogramu." : "Unexpected scheduled task.",
            StartupReason.ReadFailed => IsPolishLanguage ? "Odczyt stanu niemożliwy." : "State could not be read.",
            StartupReason.DisabledInStartupApps => IsPolishLanguage
                ? "Wpis wyłączony w Menedżerze zadań → Aplikacje autostartu (Windows go pomija)."
                : "Entry is disabled in Task Manager → Startup apps (Windows skips it).",
            _ => string.Empty
        };

        private string StartupHealthyText() =>
            IsPolishLanguage ? "Stan zgodny z konfiguracją." : "State matches configuration.";

        private string StartupUacCancelledText() =>
            IsPolishLanguage ? "Monit UAC anulowany — zachowano poprzedni stan." : "UAC prompt cancelled — previous state kept.";

        private bool ConfirmRealTimePriority()
        {
            if (!AppSettings.AllowRealtimePriority) return false;

            var result = WpfMessageBox.Show(
                AppSettings.Language == "pl"
                    ? "Priorytet RealTime może spowodować przycięcia systemu lub problemy z responsywnością. Kontynuować?"
                    : "RealTime priority can make the system unresponsive. Continue?",
                AppSettings.Language == "pl" ? "Ostrzeżenie" : "Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _updateCts.Cancel(); } catch { }
            try { _updateCts.Dispose(); } catch { }

            _refreshTimer.Stop();
            try { _profileWatcher.Dispose(); } catch { }
            StopHardwareMonitor(closeSensors: true, resetMetrics: false);
            _hardwareService.Dispose();
            _settingsService.Dispose();
        }
    }
}
