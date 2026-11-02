using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
        private string? _topologyLimitation;

        private Visibility _viewProcesses = Visibility.Visible;
        private Visibility _viewProfiles = Visibility.Collapsed;
        private Visibility _viewCpu = Visibility.Collapsed;
        private Visibility _viewHardwareMonitor = Visibility.Collapsed;
        private Visibility _viewSettings = Visibility.Collapsed;
        private Visibility _viewDiagnostics = Visibility.Collapsed;
        private string _watcherStatusText = string.Empty;
        private string _conflictSummaryText = string.Empty;
        private HardwareMetrics _fullMetrics = new();

        public ObservableCollection<ProcessItem> Processes { get; } = new();
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
        public ICommand ApplyCorePresetCommand { get; }
        public ICommand DuplicateProfileCommand { get; }
        public ICommand ExportProfilesCommand { get; }
        public ICommand ImportProfilesCommand { get; }
        public ICommand CaptureLiveSettingsCommand { get; }
        public ICommand RestoreSettingsCommand { get; }

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

        public MainViewModel()
        {
            _appSettings = _settingsService.LoadSettings();
            ConfigureLoggerFromSettings();

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
            ToggleSettingCommand = new RelayCommand(_ => SaveAndApplySettings(restartAsAdminIfNeeded: true));
            ApplyCorePresetCommand = new RelayCommand(preset => ApplyCorePreset(preset?.ToString()));
            DuplicateProfileCommand = new RelayCommand(_ => DuplicateSelectedProfile());
            ExportProfilesCommand = new RelayCommand(_ => ExportProfilesToFile());
            ImportProfilesCommand = new RelayCommand(_ => ImportProfilesFromFile());
            CaptureLiveSettingsCommand = new RelayCommand(_ => CaptureLiveSettings());
            RestoreSettingsCommand = new RelayCommand(_ => RestoreSessionSettings());

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
                    if (IsSafeMode)
                    {
                        LogSafeModeSkipOnce();
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
                OptimizationBatchResult uiBatch = IsSafeMode
                    ? new OptimizationBatchResult()
                    : _optimizationService.ApplyProfilesForSnapshots(profilesSnapshot, scan.Groups, AppSettings.AllowRealtimePriority, force: false);
                if (IsSafeMode) LogSafeModeSkipOnce();
                _operationHistory.RecordBatch(uiBatch, "process list refresh");
                LogOptimizationBatch(uiBatch, "process list refresh");

                UpdateCoreLoads();
                AddMissingProcessRows(scan.Groups);
                UpdateProcessRows(scan.Groups);
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
                    if (!MatchesProcessFilter(group.Name)) continue;
                    if (Processes.Any(x => x.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase))) continue;

                    var profile = _profiles.FirstOrDefault(x => x.IsEnabled && x.ProcessName.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
                    string rawPrio = profile?.Priority ?? group.Priority.ToString();
                    string tag = profile != null ? GetModeTag(profile.OptimizationMode) : string.Empty;

                    Processes.Add(new ProcessItem
                    {
                        Id = group.FirstProcessId,
                        Name = group.Name,
                        InstanceCount = group.InstanceCount,
                        Priority = TranslatePriority(rawPrio),
                        IsOptimized = profile != null,
                        ModeTag = tag,
                        RamUsageMB = $"{group.TotalMemoryBytes / 1024 / 1024} MB",
                        CpuUsage = $"{Math.Round(group.CpuUsagePercent, 1)}%"
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
            OnPropertyChanged(nameof(IsRightPanelVisible));

            SelectedProcess = null;
            SelectedSavedProfile = null;

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
            _profiles.RemoveAll(x => x.ProcessName.Equals(SelectedProcess.Name, StringComparison.OrdinalIgnoreCase));

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

            _profiles.Add(newProfile);
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

            var profileToApply = _profiles.FirstOrDefault(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
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
            SavedProfiles.Remove(SelectedSavedProfile);
            _profiles = SavedProfiles.ToList();
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
                : $"{ProfileConflicts.Count} profile conflict(s): {string.Join(", ", ProfileConflicts.Select(c => c.ProcessName))}. Newest/path-bound wins; review in Profiles.";
        }

        private void RefreshWatcherStatus()
        {
            var last = _profileWatcher.LastRun;
            WatcherStatusText = last.AtUtc == default
                ? "Watcher idle."
                : $"Last pass {last.AtUtc.ToLocalTime():HH:mm:ss} ({last.Source}): {last.FullyApplied}/{last.Total} applied, {last.Failed} failed, {last.Skipped} skipped in {last.Duration.TotalMilliseconds:F0} ms."
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

        private bool MatchesProcessFilter(string name)
        {
            if (string.IsNullOrWhiteSpace(_processFilterText)) return true;
            return name.Contains(_processFilterText.Trim(), StringComparison.OrdinalIgnoreCase);
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
                    if (!MatchesProcessFilter(Processes[i].Name)) Processes.RemoveAt(i);
                }
            });
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
            OnPropertyChanged(nameof(CpuVendorText));
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
            bool realtimeBefore = _realtimeCacheFlag;
            bool realtimeNow = AppSettings.AllowRealtimePriority;

            ConfigureLoggerFromSettings();
            _hwService?.Configure(AppSettings.EnableStorageSensors);
            _settingsService.SaveSettings(AppSettings);

            // Startup registration touches the registry / scheduled tasks: apply it
            // only when startup-related settings actually changed, never as a side
            // effect of language/log/monitoring toggles.
            string startupSignature = StartupSignature(AppSettings);
            if (!string.Equals(startupSignature, _lastStartupSignature, StringComparison.Ordinal))
            {
                _lastStartupSignature = startupSignature;
                _settingsService.ApplyWindowsStartup(AppSettings);
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

            if (restartAsAdminIfNeeded && AppSettings.RunAsAdministrator && !_settingsService.IsRunAsAdmin())
            {
                _settingsService.RestartAsAdmin();
            }
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
            $"{settings.StartWithWindows}|{settings.StartMinimized}|{settings.MinimizeToTray}|{settings.RunAsAdministrator}";

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
