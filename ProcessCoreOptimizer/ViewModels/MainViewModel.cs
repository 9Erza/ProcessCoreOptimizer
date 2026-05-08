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
        private readonly LocalizationService _localizationService = new();
        private readonly UpdateService _updateService = new();

        private readonly DispatcherTimer _hwTimer;
        private readonly DispatcherTimer _refreshTimer;

        private AppSettings _appSettings;
        private Dictionary<int, uint> _cpuSetMap = new();
        private List<ProcessProfile> _profiles = new();
        private readonly Dictionary<string, DateTime> _lastOptimizationDiagnosticLogUtc = new();

        private bool _isRefreshing;
        private bool _disposed;

        private double _monCpuTemp;
        private double _monGpuTemp;
        private double _monGpuLoad;
        private double _monRamUsage;

        private ProcessItem? _selectedProcess;
        private ProcessProfile? _selectedSavedProfile;
        private string _selectedPriority = "Normal";
        private OptimizationMode _selectedOptimizationMode = OptimizationMode.Affinity;
        private string _activeTab = "Processes";

        private Visibility _viewProcesses = Visibility.Visible;
        private Visibility _viewProfiles = Visibility.Collapsed;
        private Visibility _viewHardwareMonitor = Visibility.Collapsed;
        private Visibility _viewSettings = Visibility.Collapsed;
        private HardwareMetrics _fullMetrics = new();

        public ObservableCollection<ProcessItem> Processes { get; } = new();
        public ObservableCollection<CoreInfo> Cores { get; } = new();
        public ObservableCollection<string> ActionLogs { get; } = new();
        public ObservableCollection<ProcessProfile> SavedProfiles { get; } = new();
        public ObservableCollection<string> AvailablePriorities { get; } = new();

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

        public string AppVersion { get; } = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.3.0";

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
        public Visibility ViewHardwareMonitor { get => _viewHardwareMonitor; set => SetProperty(ref _viewHardwareMonitor, value); }
        public Visibility ViewSettings { get => _viewSettings; set => SetProperty(ref _viewSettings, value); }

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
                string translatedValue = TranslatePriority(value);
                if (!SetProperty(ref _selectedPriority, translatedValue)) return;

                if (ActiveTab == "Processes" && SelectedProcess != null)
                {
                    string rawValue = NormalizePriority(translatedValue);
                    string currentRaw = NormalizePriority(SelectedProcess.Priority);

                    if (rawValue != currentRaw)
                    {
                        ChangePriority(rawValue);
                    }
                }
            }
        }

        public ProcessItem? SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (!SetProperty(ref _selectedProcess, value) || value == null) return;

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

        public string CpuVendor => _hardwareService.GetCpuVendor();

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
            _optimizationService = new OptimizationService(_processService, () => _cpuSetMap);

            _cpuSetMap = _hardwareService.GetLogicalCoreToCpuSetIdMap();
            foreach (var core in _hardwareService.GetCoreTopology())
            {
                Cores.Add(core);
            }
            OnPropertyChanged(nameof(HasECores));

            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();
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

                ProcessScanResult scan = fullUiScan
                    ? await _processScannerService.ScanUserProcessesAsync()
                    : await _processScannerService.ScanProfileProcessesAsync(_profiles);

                _optimizationService.CleanupStaleCache(scan.ActiveInstances);
                CleanupOptimizationDiagnosticLogCache();
                var batch = _optimizationService.ApplyProfilesForSnapshots(_profiles, scan.Groups, AppSettings.AllowRealtimePriority, force: false);
                LogOptimizationBatch(batch, fullUiScan ? "process list refresh" : "background profile watcher");

                if (!fullUiScan) return;

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
            ViewHardwareMonitor = tabName == "Hardware" ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings = tabName == "Settings" ? Visibility.Visible : Visibility.Collapsed;

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

        private void ChangePriority(string priorityRawName)
        {
            if (SelectedProcess == null) return;

            string normalizedPriority = NormalizePriority(priorityRawName);
            if (normalizedPriority == "RealTime" && !ConfirmRealTimePriority())
            {
                _selectedPriority = SelectedProcess.Priority;
                OnPropertyChanged(nameof(SelectedPriority));
                return;
            }

            if (!PriorityService.TryParse(normalizedPriority, AppSettings.AllowRealtimePriority, out ProcessPriorityClass priority))
            {
                AddLog($"Unsupported priority: {priorityRawName}");
                return;
            }

            int count = 0;
            foreach (var process in Process.GetProcessesByName(SelectedProcess.Name))
            {
                try
                {
                    if (_processService.SetPriority(process.Id, priority)) count++;
                }
                finally
                {
                    process.Dispose();
                }
            }

            SelectedProcess.Priority = TranslatePriority(normalizedPriority);
            AddLog($"Set '{SelectedProcess.Name}' priority to {TranslatePriority(normalizedPriority)} ({count} process instance(s)).");
        }

        private void UpdateCoreSelectionFromAffinity(string processName)
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcessesByName(processName);
                var first = processes.FirstOrDefault();
                if (first == null) return;

                long mask = (long)first.ProcessorAffinity;
                ApplyMaskToCoreSelection(mask);
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
            for (int i = 0; i < Cores.Count && i < 64; i++)
            {
                Cores[i].IsChecked = (mask & (1L << i)) != 0;
            }
        }

        private long BuildSelectedCoreMask()
        {
            long mask = 0;
            for (int i = 0; i < Cores.Count && i < 64; i++)
            {
                if (Cores[i].IsChecked)
                {
                    mask |= (1L << i);
                }
            }
            return mask;
        }

        private int CountSelectedCores() => Cores.Count(c => c.IsChecked);

        private int ApplyProfileToRunningProcesses(ProcessProfile profile, bool force)
        {
            var result = _optimizationService.ApplyProfileToRunningProcesses(profile, AppSettings.AllowRealtimePriority, force);
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
            if (batch.Total == 0) return;

            foreach (var result in batch.Results.Where(r => r.Success))
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
            if (result.Success) return false;
            if (string.Equals(result.Message, "SKIPPED_ALREADY_APPLIED", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private bool ShouldLogOptimizationDiagnostic(OptimizationResult result, TimeSpan throttleWindow)
        {
            string key = $"{result.ProcessName}|{result.ProcessId}|{result.Mode}|{result.Priority}|{result.Message}";
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
                return $"Applied profile '{profileName}' via {source}: PID={result.ProcessId}, mode={GetModeTag(result.Mode)}, priority={TranslatePriority(result.Priority)}, {coreStatus}, {priorityStatus}.";
            }

            return $"Failed to apply profile '{profileName}' via {source}: PID={result.ProcessId}, mode={GetModeTag(result.Mode)}, priority={TranslatePriority(result.Priority)}, reason={result.Message}.{adminHint}";
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
            foreach (var profile in _profiles.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                SavedProfiles.Add(profile);
            }
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
            ConfigureLoggerFromSettings();
            _hwService?.Configure(AppSettings.EnableStorageSensors);
            _settingsService.SaveSettings(AppSettings);
            OnPropertyChanged(nameof(IsHardwareMonitorEnabled));
            RefreshAvailablePriorities();
            RefreshDisplayedPriorities();
            ClearAllAppliedProfileCache();

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
            StopHardwareMonitor(closeSensors: true, resetMetrics: false);
            _hardwareService.Dispose();
            _settingsService.Dispose();
        }
    }
}
