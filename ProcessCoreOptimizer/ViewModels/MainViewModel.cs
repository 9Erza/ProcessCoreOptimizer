using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
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
        private readonly HardwareMonitorService _hwService = new();
        private readonly SettingsService _settingsService = new();
        private readonly ProcessService _processService = new();
        private readonly HardwareService _hardwareService = new();
        private readonly ProfileService _profileService = new();

        private readonly DispatcherTimer _hwTimer;
        private readonly DispatcherTimer _refreshTimer;

        private AppSettings _appSettings;
        private Dictionary<int, uint> _cpuSetMap = new();
        private readonly Dictionary<int, TimeSpan> _lastCpuTime = new();
        private readonly Dictionary<int, string> _appliedProfileSignatures = new();
        private List<ProcessProfile> _profiles = new();

        private DateTime _lastSampleTime = DateTime.Now;
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

        public string AppVersion { get; } = "1.2.0";
        private readonly string _versionRawUrl = "https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/refs/heads/main/version.txt";
        private readonly string _releasesUrl = "https://github.com/9Erza/ProcessCoreOptimizer/releases";

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

            _cpuSetMap = _hardwareService.GetLogicalCoreToCpuSetIdMap();
            foreach (var core in _hardwareService.GetCoreTopology())
            {
                Cores.Add(core);
            }
            OnPropertyChanged(nameof(HasECores));

            _profiles = _profileService.LoadProfiles();
            RefreshSavedProfilesView();

            _hwTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _hwTimer.Tick += (s, e) => UpdateHardwareMetrics();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
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
            _ = CheckForUpdatesAsync();
        }

        private async Task RefreshStatisticsAsync()
        {
            if (_isRefreshing || _disposed) return;
            _isRefreshing = true;
            Process[] systemProcesses = Array.Empty<Process>();

            try
            {
                systemProcesses = await Task.Run(Process.GetProcesses);
                double elapsedSeconds = Math.Max((DateTime.Now - _lastSampleTime).TotalSeconds, 0.1);
                _lastSampleTime = DateTime.Now;

                UpdateCoreLoads();

                var userProcs = systemProcesses.Where(p => _processService.IsUserProcess(p)).ToList();
                CleanupProcessCaches(userProcs.Select(p => p.Id).ToHashSet());

                var groupedProcs = userProcs
                    .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var group in groupedProcs)
                {
                    var profile = _profiles.FirstOrDefault(x => x.IsEnabled && x.ProcessName.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
                    if (profile != null)
                    {
                        ApplyProfileToProcessGroup(profile, group, force: false);
                    }

                    if (Processes.All(x => !x.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        string rawPrio = profile?.Priority ?? _processService.GetPriorityString(group.First().Id);
                        string tag = profile != null ? GetModeTag(profile.OptimizationMode) : string.Empty;

                        WpfApplication.Current.Dispatcher.Invoke(() =>
                        {
                            Processes.Add(new ProcessItem
                            {
                                Id = group.First().Id,
                                Name = group.Key,
                                InstanceCount = group.Count(),
                                Priority = TranslatePriority(rawPrio),
                                IsOptimized = profile != null,
                                ModeTag = tag
                            });
                        });
                    }
                }

                UpdateProcessRows(userProcs, elapsedSeconds);
            }
            catch (Exception ex)
            {
                AddLog($"Refresh failed: {ex.Message}");
            }
            finally
            {
                foreach (var process in systemProcesses)
                {
                    process.Dispose();
                }

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

        private void CleanupProcessCaches(HashSet<int> activePids)
        {
            foreach (var stalePid in _appliedProfileSignatures.Keys.Where(pid => !activePids.Contains(pid)).ToList())
            {
                _appliedProfileSignatures.Remove(stalePid);
            }

            foreach (var stalePid in _lastCpuTime.Keys.Where(pid => !activePids.Contains(pid)).ToList())
            {
                _lastCpuTime.Remove(stalePid);
            }
        }

        private void UpdateProcessRows(List<Process> userProcs, double elapsedSeconds)
        {
            for (int i = Processes.Count - 1; i >= 0; i--)
            {
                var item = Processes[i];
                var procsInGroup = userProcs.Where(p => p.ProcessName.Equals(item.Name, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!procsInGroup.Any())
                {
                    WpfApplication.Current.Dispatcher.Invoke(() => Processes.RemoveAt(i));
                    continue;
                }

                long totalMem = 0;
                double totalCpuPct = 0;
                string currentPrio = "Normal";

                foreach (var p in procsInGroup)
                {
                    try
                    {
                        totalMem += p.WorkingSet64;
                        TimeSpan currentCpu = p.TotalProcessorTime;

                        if (_lastCpuTime.TryGetValue(p.Id, out TimeSpan lastTime))
                        {
                            double usageMs = (currentCpu - lastTime).TotalMilliseconds;
                            totalCpuPct += (usageMs / (elapsedSeconds * 1000 * Math.Max(Environment.ProcessorCount, 1))) * 100;
                        }

                        _lastCpuTime[p.Id] = currentCpu;
                        currentPrio = p.PriorityClass.ToString();
                    }
                    catch (Exception ex)
                    {
                        _lastCpuTime.Remove(p.Id);
                        _appliedProfileSignatures.Remove(p.Id);
                        LoggerService.Instance.Debug($"Failed to update process row for {item.Name}: {ex.Message}");
                    }
                }

                var profile = _profiles.FirstOrDefault(x => x.IsEnabled && x.ProcessName.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
                string currentTag = profile != null ? GetModeTag(profile.OptimizationMode) : string.Empty;

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    item.Id = procsInGroup.First().Id;
                    item.InstanceCount = procsInGroup.Count;
                    item.RamUsageMB = $"{totalMem / 1024 / 1024} MB";
                    item.CpuUsage = $"{Math.Round(totalCpuPct, 1)}%";
                    item.Priority = TranslatePriority(currentPrio);
                    item.IsOptimized = profile != null;
                    item.ModeTag = currentTag;
                });
            }
        }

        private void UpdateHardwareMetrics()
        {
            if (WpfApplication.Current.MainWindow?.IsVisible != true) return;

            try
            {
                var metrics = _hwService.GetAllMetrics();
                FullMetrics = metrics;
                MonCpuTemp = Math.Round(metrics.CpuTemp, 1);
                MonGpuTemp = Math.Round(metrics.GpuTemp, 1);
                MonGpuLoad = Math.Round(metrics.GpuLoad, 1);
                MonRamUsage = Math.Round(metrics.RamUsagePct, 1);
            }
            catch (Exception ex)
            {
                AddLog($"Hardware monitor failed: {ex.Message}");
            }
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

            if (tabName == "Hardware") _hwTimer.Start();
            else _hwTimer.Stop();
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
                IsEnabled = true
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
                IsEnabled = true
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
            int affected = 0;
            Process[] processes = Array.Empty<Process>();

            try
            {
                processes = Process.GetProcessesByName(profile.ProcessName);
                foreach (var process in processes)
                {
                    if (ApplyProfileToPid(process.Id, profile, force)) affected++;
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }

            return affected;
        }

        private int ApplyProfileToProcessGroup(ProcessProfile profile, IEnumerable<Process> processes, bool force)
        {
            int affected = 0;
            foreach (var process in processes)
            {
                if (ApplyProfileToPid(process.Id, profile, force)) affected++;
            }
            return affected;
        }

        private bool ApplyProfileToPid(int pid, ProcessProfile profile, bool force)
        {
            if (!profile.IsEnabled) return false;

            string priority = NormalizePriority(profile.Priority);
            var mode = NormalizeOptimizationMode(profile.OptimizationMode);
            string signature = BuildProfileSignature(profile.ProcessName, profile.AffinityMask, priority, mode, profile.IsEnabled);

            if (!force && _appliedProfileSignatures.TryGetValue(pid, out string? existingSignature) && existingSignature == signature)
            {
                return false;
            }

            string result = _processService.ApplyCoreOptimization(pid, profile.AffinityMask, mode, _cpuSetMap);
            bool coreApplied = result.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
            bool priorityApplied = true;

            if (PriorityService.TryParse(priority, AppSettings.AllowRealtimePriority, out ProcessPriorityClass parsedPriority))
            {
                priorityApplied = _processService.SetPriority(pid, parsedPriority);
            }

            if (coreApplied || priorityApplied)
            {
                _appliedProfileSignatures[pid] = signature;
                return true;
            }

            LoggerService.Instance.Debug($"Profile apply failed for PID {pid}: {result}");
            return false;
        }

        private string BuildProfileSignature(string processName, long affinityMask, string priority, OptimizationMode mode, bool isEnabled)
        {
            return $"{processName}|{affinityMask:X}|{NormalizePriority(priority)}|{NormalizeOptimizationMode(mode)}|{isEnabled}|RT:{AppSettings.AllowRealtimePriority}";
        }

        private void ClearAppliedProfileCache(string processName)
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    _appliedProfileSignatures.Remove(process.Id);
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
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
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string latestVersionStr = (await client.GetStringAsync($"{_versionRawUrl}?t={Guid.NewGuid()}"))
                    .Trim()
                    .TrimStart('v', 'V');

                if (Version.TryParse(latestVersionStr, out Version? latestVersion) &&
                    Version.TryParse(AppVersion, out Version? currentVersion) &&
                    latestVersion > currentVersion)
                {
                    AddLog($"Update available: v{latestVersionStr}");

                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        var result = WpfMessageBox.Show(
                            $"A new version (v{latestVersionStr}) is available. Open download page?",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo { FileName = _releasesUrl, UseShellExecute = true });
                        }
                    });
                }
                else
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
            res["StrColInstances"] = isPl ? "INST." : "INST.";
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
            _settingsService.SaveSettings(AppSettings);
            RefreshAvailablePriorities();
            RefreshDisplayedPriorities();
            ClearAllAppliedProfileCache();

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
            _appliedProfileSignatures.Clear();
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
            _hwTimer.Stop();
            _hwService.Dispose();
            _hardwareService.Dispose();
            _settingsService.Dispose();
        }
    }
}
