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
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProcessCoreOptimizer.WPF.ViewModels
{
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

        private readonly DispatcherTimer _hwTimer;
        private readonly DispatcherTimer _refreshTimer;

        private AppSettings _appSettings;
        private Dictionary<int, uint> _cpuSetMap = new();
        private List<ProcessProfile> _profiles = new();
        private readonly Dictionary<string, DateTime> _lastOptimizationDiagnosticLogUtc = new();
        private string? _cachedCpuVendor;
        private bool _realtimeCacheFlag;
        private string _lastStartupSignature = string.Empty;
        private readonly StartupService _startupService = new();
        private readonly StartupApplyCoordinator _startupCoordinator;
        private string _startupStatusText = string.Empty;
        private string _startupDetailText = string.Empty;
        private bool _startupBusy;
        private bool _isElevated;

        private bool _isRefreshing;
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
        private SmartRule? _selectedRule;

        public SmartRule? SelectedRule
        {
            get => _selectedRule;
            set => SetProperty(ref _selectedRule, value);
        }
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
        public string? ExternalLinkErrorText
        {
            get => _externalLinkErrorText;
            private set => SetProperty(ref _externalLinkErrorText, value);
        }

        public ICommand OpenWizardCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand FactoryResetCommand { get; }
        public ICommand OpenAuthorCommand { get; }
        public ICommand OpenRepositoryCommand { get; }
        public ICommand OpenSupportCommand { get; }

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
            .InformationalVersion ?? "2.0.0");

        private static string FormatDisplayVersion(string informationalVersion)
        {
            // InformationalVersion carries build metadata ("2.0.0+sha"); the UI
            // and tray show the clean version, the updater parses it strictly.
            string v = (informationalVersion ?? string.Empty).Trim().TrimStart('v', 'V');
            int plus = v.IndexOf('+');
            return (plus >= 0 ? v.Substring(0, plus) : v).Trim();
        }

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
            private set => SetProperty(ref _selectedProcessDetails, value);
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
            set => SetProperty(ref _selectedInstanceRow, value);
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

        public ProcessItem? SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (!SetProperty(ref _selectedProcess, value) || value == null) return;
                SelectedProcessDetails = BuildProcessDetails(value.Name);

                var savedProfile = _profiles.FirstOrDefault(p => p.ProcessName.Equals(value.Name, StringComparison.OrdinalIgnoreCase));
                if (savedProfile != null)
                {
                    string effectivePriority = NormalizePriority(savedProfile.Priority);
                    _selectedPriority = TranslatePriority(effectivePriority);
                    OnPropertyChanged(nameof(SelectedPriority));

                    SelectedOptimizationMode = savedProfile.OptimizationMode;
                    ApplyMaskToCoreSelection(savedProfile.AffinityMask);
                    return;
                }

                _selectedPriority = value.Priority;
                OnPropertyChanged(nameof(SelectedPriority));
                SelectedOptimizationMode = OptimizationMode.Affinity;
                UpdateCoreSelectionFromAffinity(value.Name);
            }
        }

        public ProcessProfile? SelectedSavedProfile
        {
            get => _selectedSavedProfile;
            set
            {
                if (!SetProperty(ref _selectedSavedProfile, value) || value == null) return;

                value.Priority = NormalizePriority(value.Priority, allowRealtime: true);
                string effectivePriority = NormalizePriority(value.Priority);
                value.DisplayPriority = TranslatePriority(effectivePriority);
                value.OptimizationMode = NormalizeOptimizationMode(value.OptimizationMode);

                _selectedPriority = TranslatePriority(effectivePriority);
                OnPropertyChanged(nameof(SelectedPriority));

                SelectedOptimizationMode = value.OptimizationMode;
                ApplyMaskToCoreSelection(value.AffinityMask);
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
            _ = RefreshStartupStatusAsync();
        }

        public string CpuVendor => _cachedCpuVendor ??= _hardwareService.GetCpuVendor();

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
                }
                else
                {
                    SetResetStatus((IsPolishLanguage
                        ? "Reset niepełny: "
                        : "Incomplete reset: ") + string.Join("; ", result.Errors.Take(3)));
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
                () => AppSettings.AllowRealtimePriority);
            _realtimeCacheFlag = _appSettings.AllowRealtimePriority;
            _lastStartupSignature = StartupSignature(_appSettings);
            _startupCoordinator = new StartupApplyCoordinator(new StartupConfigurationExecutor(_startupService));
            _isElevated = _startupService.IsRunAsAdmin();
            OnPropertyChanged(nameof(IsRunningAsAdministrator));
            OnPropertyChanged(nameof(CurrentPrivilegesText));
            _ = RefreshStartupStatusAsync();

            _cpuSetMap = _hardwareService.GetLogicalCoreToCpuSetIdMap();
            foreach (var core in _hardwareService.GetCoreTopology())
            {
                Cores.Add(core);
            }
            OnPropertyChanged(nameof(HasECores));
            RefreshCorePresets();
            RefreshTopologyLimitation();

            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            RefreshProfileConflicts();
            ReloadRules();
            _profileWatcher.ScanCompleted += (s, scan) =>
            {
                try { EvaluateAndApplyRules(scan.Groups, "background rule engine"); }
                catch (Exception ex) { LoggerService.Instance.Debug($"Background rule hook failed: {ex.Message}"); }
            };
            RefreshRecentOperations();
            LogProfileWatcherStartupSummary();

            _hwTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppSettings.HardwareRefreshSeconds) };
            _hwTimer.Tick += (s, e) => UpdateHardwareMetrics();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppSettings.ProcessListRefreshSeconds) };
            _refreshTimer.Tick += async (s, e) => await RefreshStatisticsAsync();

            SwitchTabCommand = new RelayCommand(tab => SwitchTab(tab?.ToString()));
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
            OpenWizardCommand = new RelayCommand(_ => OpenFirstRunWizard());
            RestoreDefaultsCommand = new RelayCommand(_ => { _ = RestoreDefaultSettingsAsync(); });
            FactoryResetCommand = new RelayCommand(_ => { _ = RunFactoryResetAsync(); });
            OpenAuthorCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Author));
            OpenRepositoryCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Repository));
            OpenSupportCommand = new RelayCommand(_ => OpenExternalLink(PcoExternalLink.Support));
            ApplyCorePresetCommand = new RelayCommand(preset => ApplyCorePreset(preset?.ToString()));
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

            ApplyLanguage(_appSettings.Language);
            _refreshTimer.Start();
            AddLog($"System initialized. App Version v{AppVersion}");
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

        private async Task RefreshStatisticsAsync()
        {
            if (_isRefreshing || _disposed) return;
            _isRefreshing = true;

            try
            {
                bool fullUiScan = IsProcessListUiActive();
                _refreshTimer.Interval = TimeSpan.FromSeconds(fullUiScan ? AppSettings.ProcessListRefreshSeconds : AppSettings.ProfileWatcherSeconds);

                if (!fullUiScan)
                {
                    _hardwareService.ReleaseCpuLoadCounters();
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

                ProcessScanResult scan = await _processScannerService.ScanUserProcessesAsync();
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

                UpdateCoreLoads();
                AddMissingProcessRows(scan.Groups);
                UpdateProcessRows(scan.Groups);
                _lastUiScan = scan;
                if (IsInstanceView) RebuildInstanceRows();
            }
            catch (Exception ex)
            {
                AddLog($"Refresh failed: {ex.Message}");
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void UpdateCoreLoads()
        {
            var coreLoads = _hardwareService.GetCurrentLoads();
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < Cores.Count && i < coreLoads.Count; i++)
                {
                    Cores[i].LoadUsage = coreLoads[i];
                }
            });
        }

        private void AddMissingProcessRows(IReadOnlyList<ProcessGroupSnapshot> groups)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                foreach (var group in groups)
                {
                    if (!MatchesProcessFilter(group.Name, group.ExecutablePath, isOptimized: _profiles.Any(x => x.IsEnabled && x.ProcessName.Equals(group.Name, StringComparison.OrdinalIgnoreCase)))) continue;
                    if (Processes.Any(x => x.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase))) continue;

                    var profile = _profiles.FirstOrDefault(x => x.IsEnabled && x.ProcessName.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
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
            });
        }

        private void UpdateProcessRows(IReadOnlyList<ProcessGroupSnapshot> groups)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                for (int i = Processes.Count - 1; i >= 0; i--)
                {
                    var item = Processes[i];
                    var snapshot = groups.FirstOrDefault(p => p.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));

                    if (snapshot == null)
                    {
                        Processes.RemoveAt(i);
                        continue;
                    }

                    var profile = _profiles.FirstOrDefault(x => x.IsEnabled && x.ProcessName.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
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
            });
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
                AddLog($"Hardware monitor failed: {ex.Message}");
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
            ViewProcesses = tabName == "Processes" ? Visibility.Visible : Visibility.Collapsed;
            ViewProfiles = tabName == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
            ViewCpu = tabName == "Cpu" ? Visibility.Visible : Visibility.Collapsed;
            ViewHardwareMonitor = tabName == "Hardware" ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings = tabName == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            ViewDiagnostics = tabName == "Diagnostics" ? Visibility.Visible : Visibility.Collapsed;
            ViewSmartRules = tabName == "Rules" ? Visibility.Visible : Visibility.Collapsed;
            OnPropertyChanged(nameof(IsRightPanelVisible));

            SelectedProcess = null;
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

            if (tabName != "Processes")
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
                AddLog("No CPU cores selected. Optimization was not applied.");
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

            int affected = ApplyProfileToRunningProcesses(temporaryProfile, force: true);
            SelectedProcess.IsOptimized = affected > 0;
            SelectedProcess.ModeTag = affected > 0 ? GetModeTag(mode) : string.Empty;
            SelectedProcess.Priority = TranslatePriority(priority);

            AddLog($"Applied '{SelectedProcess.Name}' optimization: {GetModeTag(mode)}, {CountSelectedCores()} cores, priority {TranslatePriority(priority)} ({affected} process instance(s)).");
        }

        private void SaveCurrentAsProfile()
        {
            if (SelectedProcess == null) return;

            long mask = BuildSelectedCoreMask();
            if (mask == 0)
            {
                AddLog("Profile was not saved because no CPU cores are selected.");
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

            int affected = ApplyProfileToRunningProcesses(newProfile, force: true);
            SelectedProcess.IsOptimized = true;
            SelectedProcess.ModeTag = GetModeTag(mode);
            SelectedProcess.Priority = TranslatePriority(rawPriority);

            AddLog($"Saved and applied profile for '{SelectedProcess.Name}': {GetModeTag(mode)}, priority {TranslatePriority(rawPriority)} ({affected} process instance(s)).");
        }

        private void UpdateSelectedProfile()
        {
            if (SelectedSavedProfile == null) return;

            long mask = BuildSelectedCoreMask();
            if (mask == 0)
            {
                AddLog("Profile was not updated because no CPU cores are selected.");
                return;
            }

            string processName = SelectedSavedProfile.ProcessName;
            string rawPriority = NormalizePriority(_selectedPriority);
            if (rawPriority == "RealTime" && !ConfirmRealTimePriority()) return;

            var mode = NormalizeOptimizationMode(SelectedOptimizationMode);

            SelectedSavedProfile.AffinityMask = mask;
            SelectedSavedProfile.Priority = rawPriority;
            SelectedSavedProfile.DisplayPriority = TranslatePriority(rawPriority);
            SelectedSavedProfile.OptimizationMode = mode;
            SelectedSavedProfile.IsEnabled = true;
            SelectedSavedProfile.UpdatedAt = DateTime.UtcNow;

            _profiles = SavedProfiles.ToList();
            _profileService.SaveProfiles(_profiles);
            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
            ClearAppliedProfileCache(processName);

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

            AddLog($"Updated and applied profile for '{processName}': {GetModeTag(mode)}, priority {TranslatePriority(rawPriority)} ({affected} process instance(s)).");
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

            AddLog($"Deleted profile: '{name}'");
            SelectedSavedProfile = null;
        }

        private void RestoreSessionSettings()
        {
            if (SelectedProcess == null) return;
            if (IsSafeMode)
            {
                AddLog("Safe mode: session restore blocked, OS mutations disabled (--safe-mode).");
                return;
            }
            var result = _sessionRestore.RestoreForProcess(SelectedProcess.Name, _processService, _cpuSetMap);
            // The watcher must not immediately re-apply the profile against the
            // user's intent: suspend automatic application for these instances.
            // Suspensions expire on their own; nothing persists.
            _optimizationService.SuspendLiveInstances(SelectedProcess.Name, TimeSpan.FromMinutes(5), "manual session restore");
            ClearAppliedProfileCache(SelectedProcess.Name);
            AddLog($"Session restore for '{SelectedProcess.Name}': {result.Message} Automatic re-apply suspended for 5 minutes.");
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
                AddLog($"Could not read current affinity for '{processName}': {ex.Message}");
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
            AddLog("Safe mode: observing only, OS mutations disabled (--safe-mode).");
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

        private void ThrottledRuleLog(SmartRule rule, string message, int minutes = 5)
        {
            DateTime now = DateTime.UtcNow;
            if (_lastRuleLogUtc.TryGetValue(rule.Id, out DateTime last) && now - last < TimeSpan.FromMinutes(minutes))
                return;
            _lastRuleLogUtc[rule.Id] = now;
            AddLog(message);
        }

        private void EvaluateAndApplyRules(IReadOnlyList<ProcessGroupSnapshot> groups, string source)
        {
            try
            {
                if (IsSafeMode || AppSettings.AutomationPaused) return;
                var snapshot = _rulesSnapshot;
                if (snapshot.Count == 0 || !snapshot.Any(r => r.IsEnabled)) return;

                int? fg = null;
                if (snapshot.Any(r => r.IsEnabled && r.Trigger == RuleTrigger.OnForeground))
                {
                    try { fg = ForegroundService.GetForegroundPid(); } catch { fg = null; }
                }

                var decisions = _ruleEngine.Evaluate(groups, fg, DateTime.UtcNow, snapshot, automationPaused: false);
                foreach (var d in decisions.Where(x => x.Ready))
                    ApplyRuleDecision(d, source);
                RefreshRuleStatus();
            }
            catch (Exception ex)
            {
                AddLog($"Rules evaluation failed: {ex.Message}");
            }
        }

        private void ApplyRuleDecision(RuleDecision d, string source)
        {
            var rule = SmartRuleService.FindById(_rulesSnapshot, d.RuleId);
            if (rule == null || !rule.IsEnabled) return;
            try
            {
                if (rule.Action == RuleAction.ApplyProfile)
                {
                    var profile = ProfileService.FindById(_profiles, rule.ProfileId);
                    if (profile == null || !profile.IsEnabled)
                    {
                        ThrottledRuleLog(rule, IsPolishLanguage
                            ? $"Reguła '{rule.Name}': brak włączonego profilu — pominięto ({d.Reason})."
                            : $"Rule '{rule.Name}': no enabled profile — skipped ({d.Reason}).");
                        return;
                    }

                    var batch = _optimizationService.ApplyProfileToInstances(
                        profile, d.ProcessName, new[] { d.Key }, AppSettings.AllowRealtimePriority, force: true);
                    var first = batch.Results.FirstOrDefault();
                    _ruleEngine.MarkApplied(rule.Id, d.Key, DateTime.UtcNow);
                    AddLog($"Rule '{rule.Name}' → '{profile.DisplayName}' on PID {d.Key.ProcessId} via {source} ({d.Reason}): {first?.Status.ToString() ?? "Skipped"}.");
                }
                else
                {
                    var result = _sessionRestore.RestoreForProcess(d.ProcessName, _processService, _cpuSetMap, new[] { d.Key });
                    _optimizationService.SuspendLiveInstances(d.ProcessName, TimeSpan.FromMinutes(5), "smart rule session restore");
                    ClearAppliedProfileCache(d.ProcessName);
                    _ruleEngine.MarkApplied(rule.Id, d.Key, DateTime.UtcNow);
                    AddLog($"Rule '{rule.Name}' restored session for PID {d.Key.ProcessId} via {source} ({d.Reason}): {result.Message}");
                }
            }
            catch (Exception ex)
            {
                ThrottledRuleLog(rule, $"Rule '{rule.Name}' failed: {ex.Message}", minutes: 1);
            }

            RefreshRuleStatus();
        }

        private int ApplyProfileToRunningProcesses(ProcessProfile profile, bool force)
        {
            if (IsSafeMode)
            {
                AddLog("Safe mode: manual apply blocked, OS mutations disabled (--safe-mode).");
                return 0;
            }

            var result = _optimizationService.ApplyProfileToRunningProcesses(profile, AppSettings.AllowRealtimePriority, force);
            _operationHistory.RecordBatch(result, force ? "manual profile action" : "background profile watcher");
            LogOptimizationBatch(result, force ? "manual profile action" : "background profile watcher");
            return result.Successful;
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

            foreach (var preset in Services.CorePresets.Build(dtos))
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
                AddLog($"Core preset '{presetId}' is not available on this CPU.");
                return;
            }

            var wanted = new HashSet<int>(preset.LogicalProcessors);
            foreach (var core in Cores)
            {
                core.IsChecked = wanted.Contains(core.Index);
            }

            AddLog($"Core preset '{preset.DisplayName}' applied: {wanted.Count} logical processor(s) selected.");
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
            AddLog($"Duplicated profile '{copy.ProcessName}' (starts disabled).");
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
                AddLog($"Exported {_profiles.Count} profile(s) to '{dialog.FileName}'.");
            }
            catch (Exception ex)
            {
                AddLog($"Profile export failed: {ex.Message}");
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

                if (result.Success) AddLog($"Imported {result.Imported} profile(s).");
                else AddLog($"Profile import: {result.Imported} imported, {result.SkippedInvalid} invalid. {string.Join("; ", result.Errors)}");
            }
            catch (Exception ex)
            {
                AddLog($"Profile import failed: {ex.Message}");
            }
        }

        private void CopySelectedPid()
        {
            int? pid = SelectedInstanceRow?.ProcessId ?? SelectedProcessDetails?.RepresentativePid;
            if (pid == null || pid == 0)
            {
                AddLog("Copy PID: no process selected.");
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(pid.Value.ToString());
                AddLog($"Copied PID {pid.Value} to clipboard.");
            }
            catch (Exception ex)
            {
                AddLog($"Copy PID failed: {ex.Message}");
            }
        }

        private void CopySelectedExePath()
        {
            string? path = SelectedInstanceRow?.ExecutablePath ?? SelectedProcessDetails?.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AddLog("Copy EXE path: unavailable for the selected process (OS refused the read).");
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(path);
                AddLog($"Copied EXE path to clipboard: {path}");
            }
            catch (Exception ex)
            {
                AddLog($"Copy EXE path failed: {ex.Message}");
            }
        }

        private void OpenSelectedFileLocation()
        {
            string? path = SelectedInstanceRow?.ExecutablePath ?? SelectedProcessDetails?.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AddLog("Open file location: unavailable for the selected process (OS refused the read).");
                return;
            }

            // Observation only: reveals the local folder, never mutates the process.
            try
            {
                string target = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
                AddLog($"Opened file location: {path}");
            }
            catch (Exception ex)
            {
                AddLog($"Open file location failed: {ex.Message}");
            }
        }

        private void RefreshSelectedDetails()
        {
            // Observation only: re-reads live details for the current selection.
            string? name = SelectedInstanceRow?.Name ?? SelectedProcess?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                AddLog("Details: no process selected.");
                return;
            }

            SelectedProcessDetails = BuildProcessDetails(name);
            AddLog($"Details refreshed for '{name}' (see panel below).");
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
                    AddLog("Preview: no enabled profile matches the selection. Create one first.");
                    return;
                }

                var preview = ProfilePreviewService.Compare(profile, pid, _cpuSetMap, _processService, AppSettings.AllowRealtimePriority);
                AddLog($"Preview '{profile.DisplayName}' vs PID {pid}: {preview.Summary}");
                foreach (var row in preview.Rows)
                    AddLog($"  {row.Action}: requested {row.Requested}, current {row.Current} — {(row.Matches ? "already matches" : "differs")}. {row.Detail}");
            }
            catch (Exception ex)
            {
                AddLog($"Preview failed: {ex.Message}");
            }
        }

        private void CreateProfileFromSelectedInstance()
        {
            var row = SelectedInstanceRow;
            if (row == null)
            {
                AddLog("Create profile: select a single instance (Instances view) first.");
                return;
            }

            try
            {
                var draft = ProfilePreviewService.CaptureFromLive(row.ProcessId, _cpuSetMap, _processService);
                if (draft == null)
                {
                    AddLog($"Create profile failed: the OS refused to read PID {row.ProcessId}.");
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
                AddLog($"Created disabled profile draft from PID {row.ProcessId} ('{row.Name}').");
            }
            catch (Exception ex)
            {
                AddLog($"Create profile failed: {ex.Message}");
            }
        }

        private void ApplyToSelectedInstance()
        {
            // P0: an explicit instance selection applies to that PID only — never widens.
            var row = SelectedInstanceRow;
            if (row == null) return;
            if (IsSafeMode)
            {
                AddLog("Safe mode: apply blocked, OS mutations disabled (--safe-mode).");
                return;
            }

            try
            {
                var profile = _profiles.FirstOrDefault(p => p.IsEnabled
                    && ProfileService.MatchesIdentity(p, row.Name, row.ExecutablePath));
                if (profile == null)
                {
                    AddLog($"Apply: no enabled profile matches PID {row.ProcessId} ('{row.Name}').");
                    return;
                }

                var batch = _optimizationService.ApplyProfileToInstances(
                    profile, row.Name, new[] { row.Key }, AppSettings.AllowRealtimePriority, force: true);
                var first = batch.Results.FirstOrDefault();
                row.LastStatus = first?.Status.ToString() ?? "Skipped";
                AddLog($"Applied '{profile.DisplayName}' to PID {row.ProcessId} only: {row.LastStatus} ({batch.Successful}/{batch.Total} applied).");
                RefreshWatcherStatus();
            }
            catch (Exception ex)
            {
                AddLog($"Apply to instance failed: {ex.Message}");
            }
        }

        private void CreateNewProfile()
        {
            var draft = new ProcessProfile
            {
                ProcessName = SelectedProcess?.Name ?? SelectedInstanceRow?.Name ?? string.Empty,
                ExecutablePath = SelectedInstanceRow?.ExecutablePath,
                AffinityMask = 0xFF,
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
                AddLog($"Profile editor failed: {ex.Message}");
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
            SaveRules();
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
            SaveRules();
            AddLog(IsPolishLanguage ? $"Zaktualizowano regułę '{existing.Name}'." : $"Updated rule '{existing.Name}'.");
        }

        private void DeleteSelectedRule()
        {
            var selected = SelectedRule;
            if (selected == null) return;
            _rules = SmartRuleService.RemoveById(_rules, selected.Id);
            _ruleEngine.ClearOverride(selected.Id);
            SelectedRule = null;
            SaveRules();
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
            SaveRules();
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

        private void SaveRules()
        {
            _ruleService.SaveRules(_rules);
            _rules = _ruleService.LoadRules();
            _rulesSnapshot = _rules.ToList();
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                Rules.Clear();
                foreach (var rule in _rules.OrderBy(r => r.Priority).ThenBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase))
                    Rules.Add(rule);
            });
            RefreshRuleStatus();
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
                AddLog($"Rule editor failed: {ex.Message}");
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
                        AddLog($"Capture failed: '{SelectedProcess.Name}' is no longer running.");
                        return;
                    }

                    var draft = ProfilePreviewService.CaptureFromLive(live.Id, _cpuSetMap, _processService);
                    if (draft == null)
                    {
                        AddLog($"Capture failed: the OS refused to read '{SelectedProcess.Name}'.");
                        return;
                    }

                    _profiles.RemoveAll(x => x.ProcessName.Equals(draft.ProcessName, StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(x.ExecutablePath) && string.IsNullOrWhiteSpace(draft.ExecutablePath));
                    _profiles.Add(draft);
                    _profileService.SaveProfiles(_profiles);
                    _profiles = _profileService.LoadProfiles();
                    RefreshSavedProfilesView();
                    RefreshProfileConflicts();
                    AddLog($"Captured live settings of '{draft.ProcessName}' as a new disabled profile.");
                }
                finally
                {
                    foreach (var p in candidates) p.Dispose();
                }
            }
            catch (Exception ex)
            {
                AddLog($"Capture failed: {ex.Message}");
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
            WatcherStatusText = IsPolishLanguage
                ? $"Ostatni przebieg {time} ({last.Source}): {last.FullyApplied}/{last.Total} zastosowano, {last.Failed} błędów, {last.Skipped} pominięto w {last.Duration.TotalMilliseconds:F0} ms."
                : $"Last pass {time} ({last.Source}): {last.FullyApplied}/{last.Total} applied, {last.Failed} failed, {last.Skipped} skipped in {last.Duration.TotalMilliseconds:F0} ms."
                + (string.IsNullOrWhiteSpace(last.Error) ? string.Empty : $" Error: {last.Error}");
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
                AddLog(message);
            }

            foreach (var result in batch.Results.Where(IsActionableOptimizationFailure))
            {
                if (!ShouldLogOptimizationDiagnostic(result, TimeSpan.FromSeconds(60))) continue;

                string message = BuildOptimizationLogMessage(result, source, success: false);
                LoggerService.Instance.Warn(message);
                AddLog(message);
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
                await Task.Delay(2000);
                var update = await _updateService.CheckForUpdatesAsync(AppVersion);
                if (update.IsUpdateAvailable)
                {
                    AddLog($"Update available: v{update.LatestVersion}");

                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        var result = WpfMessageBox.Show(
                            $"A new version (v{update.LatestVersion}) is available. Open download page?",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            UpdateService.OpenReleasePage(update.ReleaseUrl);
                        }
                    });
                }
                else if (string.IsNullOrWhiteSpace(update.Error))
                {
                    AddLog("App is up to date.");
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Debug($"Update check failed: {ex.Message}");
            }
        }

        private void ApplyLanguage(string langCode)
        {
            _localizationService.ApplyLanguage(langCode);
            RefreshAvailablePriorities();
            RefreshDisplayedPriorities();
            RefreshProfileConflicts();
            RefreshWatcherStatus();
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
            if (IsStartupBusy) return;
            IsStartupBusy = true;
            try
            {
                var result = await _startupCoordinator.ApplyLatestAsync(AppSettings.DesiredStartup).ConfigureAwait(true);
                UpdateStartupStatus(result.FinalEvaluation, result.Error ?? (result.WasElevationCancelled ? StartupUacCancelledText() : null));
                if (!string.IsNullOrWhiteSpace(result.Error) || result.WasElevationCancelled)
                {
                    AddLog($"Startup apply: {result.Error ?? StartupUacCancelledText()}");
                }
                else if (result.Success)
                {
                    AddLog($"Startup configured: {result.FinalEvaluation.State}.");
                }
            }
            catch (Exception ex)
            {
                UpdateStartupStatus(null, ex.Message);
            }
            finally
            {
                IsStartupBusy = false;
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
                return;
            }

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

            _refreshTimer.Stop();
            try { _profileWatcher.Dispose(); } catch { }
            StopHardwareMonitor(closeSensors: true, resetMetrics: false);
            _hardwareService.Dispose();
            _settingsService.Dispose();
        }
    }
}
