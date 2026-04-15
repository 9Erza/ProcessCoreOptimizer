using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProcessCoreOptimizer.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly HardwareMonitorService _hwService = new();
        private DispatcherTimer _hwTimer;

        private double _monCpuTemp;
        private double _monGpuTemp;
        private double _monGpuLoad;
        private double _monRamUsage;

        public double MonCpuTemp { get => _monCpuTemp; set => SetProperty(ref _monCpuTemp, value); }
        public double MonGpuTemp { get => _monGpuTemp; set => SetProperty(ref _monGpuTemp, value); }
        public double MonGpuLoad { get => _monGpuLoad; set => SetProperty(ref _monGpuLoad, value); }
        public double MonRamUsage { get => _monRamUsage; set => SetProperty(ref _monRamUsage, value); }

        private readonly SettingsService _settingsService = new();
        private AppSettings _appSettings;

        public AppSettings AppSettings
        {
            get => _appSettings;
            set => SetProperty(ref _appSettings, value);
        }

        private readonly ProcessService _processService = new();
        private readonly HardwareService _hardwareService = new();
        private readonly ProfileService _profileService = new();
        private readonly DispatcherTimer _refreshTimer;

        private readonly Dictionary<int, TimeSpan> _lastCpuTime = new();
        private DateTime _lastSampleTime = DateTime.Now;
        private bool _isRefreshing = false;

        private ProcessItem? _selectedProcess;
        private ProcessProfile? _selectedSavedProfile;
        private string _selectedPriority = "Normal";
        private List<ProcessProfile> _profiles = new();

        public string AppVersion { get; } = "1.1.0";
        private readonly string _versionRawUrl = "https://raw.githubusercontent.com/9Erza/ProcessCoreOptimizer/refs/heads/main/ProcessCoreOptimizer/version.txt";
        private readonly string _releasesUrl = "https://github.com/9Erza/ProcessCoreOptimizer/releases";

        public ObservableCollection<ProcessItem> Processes { get; set; } = new();
        public ObservableCollection<CoreInfo> Cores { get; set; } = new();
        public ObservableCollection<string> ActionLogs { get; } = new();
        public ObservableCollection<ProcessProfile> SavedProfiles { get; set; } = new();
        public ObservableCollection<string> AvailablePriorities { get; set; } = new();

        private string _activeTab = "Processes";
        public string ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }

        private System.Windows.Visibility _viewProcesses = System.Windows.Visibility.Visible;
        public System.Windows.Visibility ViewProcesses { get => _viewProcesses; set => SetProperty(ref _viewProcesses, value); }

        private System.Windows.Visibility _viewProfiles = System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility ViewProfiles { get => _viewProfiles; set => SetProperty(ref _viewProfiles, value); }

        private System.Windows.Visibility _viewHardwareMonitor = System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility ViewHardwareMonitor { get => _viewHardwareMonitor; set => SetProperty(ref _viewHardwareMonitor, value); }

        private System.Windows.Visibility _viewSettings = System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility ViewSettings { get => _viewSettings; set => SetProperty(ref _viewSettings, value); }

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

        public bool HasECores => Cores.Any(c => c.IsECore);

        public List<string> Languages { get; } = new() { "English", "Polski" };

        public bool IsPolishLanguage
        {
            get => AppSettings.Language == "pl";
            set
            {
                string code = value ? "pl" : "en";
                if (AppSettings.Language != code)
                {
                    AppSettings.Language = code;
                    ApplyLanguage(code);
                    _settingsService.SaveSettings(AppSettings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedLanguageName));
                }
            }
        }

        public string SelectedLanguageName
        {
            get => AppSettings.Language == "pl" ? "Polski" : "English";
            set
            {
                bool isPl = value == "Polski";
                if (IsPolishLanguage != isPl)
                {
                    IsPolishLanguage = isPl;
                }
            }
        }

        private string TranslatePriority(string rawEnum)
        {
            if (AppSettings.Language == "pl")
            {
                return rawEnum switch
                {
                    "Idle" => "Bezczynny",
                    "BelowNormal" => "Poniżej Normalnego",
                    "Normal" => "Normalny",
                    "AboveNormal" => "Powyżej Normalnego",
                    "High" => "Wysoki",
                    "RealTime" => "Czas Rzeczywisty",
                    _ => rawEnum
                };
            }
            return rawEnum switch
            {
                "BelowNormal" => "Below Normal",
                "AboveNormal" => "Above Normal",
                "RealTime" => "Real Time",
                _ => rawEnum
            };
        }

        private string UntranslatePriority(string displayStr)
        {
            if (string.IsNullOrEmpty(displayStr)) return "Normal";

            if (AppSettings.Language == "pl")
            {
                return displayStr switch
                {
                    "Bezczynny" => "Idle",
                    "Poniżej Normalnego" => "BelowNormal",
                    "Normalny" => "Normal",
                    "Powyżej Normalnego" => "AboveNormal",
                    "Wysoki" => "High",
                    "Czas Rzeczywisty" => "RealTime",
                    _ => displayStr
                };
            }
            return displayStr switch
            {
                "Below Normal" => "BelowNormal",
                "Above Normal" => "AboveNormal",
                "Real Time" => "RealTime",
                _ => displayStr
            };
        }

        private void ApplyLanguage(string langCode)
        {
            var res = System.Windows.Application.Current.Resources;
            AvailablePriorities.Clear();

            if (langCode == "pl")
            {
                res["StrSystemProcesses"] = "Procesy Systemowe";
                res["StrSavedProfiles"] = "Zapisane Profile";
                res["StrHardwareMonitor"] = "Monitor Sprzętu";
                res["StrSettings"] = "Ustawienia";
                res["StrConsoleLog"] = "LOGI KONSOLI";
                res["StrProcessList"] = "Lista Procesów";
                res["StrYourProfiles"] = "Twoje Profile";
                res["StrCpuUsage"] = "UŻYCIE CPU";
                res["StrCpuTemp"] = "TEMPERATURA CPU";
                res["StrGpuUsage"] = "UŻYCIE GPU";
                res["StrGpuTemp"] = "TEMPERATURA GPU";
                res["StrRamUsage"] = "UŻYCIE RAM (%)";
                res["StrRamUsedAvail"] = "RAM ZAJĘTY / DOSTĘPNY";
                res["StrExpandHardware"] = "Rozwiń Szczegóły Sprzętu";
                res["StrGpuDetails"] = "SZCZEGÓŁY GPU";
                res["StrCpuDetails"] = "SZCZEGÓŁY CPU";
                res["StrAppSettings"] = "Ustawienia Aplikacji";
                res["StrStartWindows"] = "Uruchamiaj z systemem Windows";
                res["StrStartMinimized"] = "Uruchom zminimalizowany";
                res["StrMinToTray"] = "Minimalizuj do zasobnika";
                res["StrCloseToTray"] = "Zamykaj do zasobnika";
                res["StrStartAdmin"] = "Uruchom jako Administrator";
                res["StrAdminReq"] = "Wymaga restartu. Pozwala na optymalizację w tle.";
                res["StrLanguage"] = "Język aplikacji";
                res["StrCpuCores"] = "Rdzenie CPU";
                res["StrSelectAll"] = "Zaznacz Wszystko";
                res["StrClearAll"] = "Odznacz Wszystko";
                res["StrDisableSMT"] = "Wyłącz SMT";
                res["StrDisableECores"] = "Wyłącz E-Cores";
                res["StrSetPriority"] = "Ustaw Priorytet";
                res["StrSetAffinity"] = "Ustaw Koligację";
                res["StrSaveProfile"] = "Zapisz Profil";
                res["StrUpdateProfile"] = "Aktualizuj Profil";
                res["StrDeleteProfile"] = "Usuń Profil";
                res["StrColName"] = "NAZWA";
                res["StrColPriority"] = "PRIORYTET";
                res["StrColRam"] = "RAM";
                res["StrColCpu"] = "CPU";
                res["StrVramUsage"] = "Zużycie VRAM:";
                res["StrCoreClock"] = "Takt. Rdzenia:";
                res["StrMemClock"] = "Takt. Pamięci:";
                res["StrHotSpot"] = "Hot Spot:";
                res["StrVramTemp"] = "Temp. VRAM:";
                res["StrPowerDraw"] = "Pobór Mocy:";
                res["StrAvgClock"] = "Średnie Takt.:";
                res["StrCoreTemps"] = "Temperatury Rdzeni:";

                AvailablePriorities.Add("Bezczynny");
                AvailablePriorities.Add("Poniżej Normalnego");
                AvailablePriorities.Add("Normalny");
                AvailablePriorities.Add("Powyżej Normalnego");
                AvailablePriorities.Add("Wysoki");
                AvailablePriorities.Add("Czas Rzeczywisty");
            }
            else
            {
                res["StrSystemProcesses"] = "System Processes";
                res["StrSavedProfiles"] = "Saved Profiles";
                res["StrHardwareMonitor"] = "Hardware Monitor";
                res["StrSettings"] = "Settings";
                res["StrConsoleLog"] = "CONSOLE LOG";
                res["StrProcessList"] = "Process List";
                res["StrYourProfiles"] = "Your Profiles";
                res["StrCpuUsage"] = "CPU USAGE";
                res["StrCpuTemp"] = "CPU TEMPERATURE";
                res["StrGpuUsage"] = "GPU USAGE";
                res["StrGpuTemp"] = "GPU TEMPERATURE";
                res["StrRamUsage"] = "RAM USAGE (%)";
                res["StrRamUsedAvail"] = "RAM USED / AVAILABLE";
                res["StrExpandHardware"] = "Detailed Hardware Data";
                res["StrGpuDetails"] = "GPU DETAILS";
                res["StrCpuDetails"] = "CPU DETAILS";
                res["StrAppSettings"] = "Application Settings";
                res["StrStartWindows"] = "Start with Windows";
                res["StrStartMinimized"] = "Start Minimized";
                res["StrMinToTray"] = "Minimize to Tray";
                res["StrCloseToTray"] = "Close to Tray";
                res["StrStartAdmin"] = "Start as Administrator";
                res["StrAdminReq"] = "Requires restart. Allows silent background optimization.";
                res["StrLanguage"] = "Language";
                res["StrCpuCores"] = "CPU Cores";
                res["StrSelectAll"] = "Select All";
                res["StrClearAll"] = "Clear All";
                res["StrDisableSMT"] = "Disable SMT";
                res["StrDisableECores"] = "Disable E-Cores";
                res["StrSetPriority"] = "Set Priority";
                res["StrSetAffinity"] = "Set Affinity";
                res["StrSaveProfile"] = "Save Profile";
                res["StrUpdateProfile"] = "Update Profile";
                res["StrDeleteProfile"] = "Delete Profile";
                res["StrColName"] = "NAME";
                res["StrColPriority"] = "PRIORITY";
                res["StrColRam"] = "RAM";
                res["StrColCpu"] = "CPU";
                res["StrVramUsage"] = "VRAM Usage:";
                res["StrCoreClock"] = "Core Clock:";
                res["StrMemClock"] = "Mem Clock:";
                res["StrHotSpot"] = "Hot Spot:";
                res["StrVramTemp"] = "VRAM Temp:";
                res["StrPowerDraw"] = "Power Draw:";
                res["StrAvgClock"] = "Avg Clock:";
                res["StrCoreTemps"] = "Core Temperatures:";

                AvailablePriorities.Add("Idle");
                AvailablePriorities.Add("Below Normal");
                AvailablePriorities.Add("Normal");
                AvailablePriorities.Add("Above Normal");
                AvailablePriorities.Add("High");
                AvailablePriorities.Add("Real Time");
            }

            foreach (var p in Processes)
            {
                string raw = UntranslatePriority(p.Priority);
                p.Priority = TranslatePriority(raw);
            }

            string currentRawSelected = UntranslatePriority(_selectedPriority);
            SelectedPriority = TranslatePriority(currentRawSelected);
        }

        public string SelectedPriority
        {
            get => _selectedPriority;
            set
            {
                if (SetProperty(ref _selectedPriority, value))
                {
                    if (ActiveTab == "Processes" && SelectedProcess != null)
                    {
                        string rawValue = UntranslatePriority(value);
                        string currentRaw = UntranslatePriority(SelectedProcess.Priority);
                        if (rawValue != currentRaw)
                        {
                            ChangePriority(rawValue);
                        }
                    }
                }
            }
        }

        public ProcessItem? SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (SetProperty(ref _selectedProcess, value) && value != null)
                {
                    _selectedPriority = value.Priority;
                    OnPropertyChanged(nameof(SelectedPriority));
                    UpdateCoreSelectionFromAffinity(value.Name);
                }
            }
        }

        public ProcessProfile? SelectedSavedProfile
        {
            get => _selectedSavedProfile;
            set
            {
                if (SetProperty(ref _selectedSavedProfile, value) && value != null)
                {
                    _selectedPriority = TranslatePriority(value.Priority);
                    OnPropertyChanged(nameof(SelectedPriority));

                    long mask = value.AffinityMask;
                    for (int i = 0; i < Cores.Count; i++) { Cores[i].IsChecked = (mask & (1L << i)) != 0; }
                }
            }
        }

        public MainViewModel()
        {
            _appSettings = _settingsService.LoadSettings();

            if (_appSettings.StartWithWindows && _appSettings.RunAsAdministrator && _settingsService.IsRunAsAdmin())
            {
                _settingsService.ApplyWindowsStartup(_appSettings);
            }
            _profiles = _profileService.LoadProfiles();
            SavedProfiles = new ObservableCollection<ProcessProfile>(_profiles);

            _hwTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _hwTimer.Tick += (s, e) => UpdateHardwareMetrics();

            SwitchTabCommand = new RelayCommand(tab => SwitchTab(tab?.ToString()));

            ApplySettingsCommand = new RelayCommand(o => ApplyAffinity());
            SaveProfileCommand = new RelayCommand(o => SaveCurrentAsProfile());
            UpdateProfileCommand = new RelayCommand(o => UpdateSelectedProfile());
            DeleteProfileCommand = new RelayCommand(o => DeleteSelectedProfile());

            Cores = new ObservableCollection<CoreInfo>(_hardwareService.GetCoreTopology());

            ApplyLanguage(_appSettings.Language);

            SelectAllCommand = new RelayCommand(o => { foreach (var c in Cores) c.IsChecked = true; });
            ClearAllCommand = new RelayCommand(o => { foreach (var c in Cores) c.IsChecked = false; });
            DisableSmtCommand = new RelayCommand(o => { foreach (var c in Cores) if (c.IsThread) c.IsChecked = false; });
            DisableECoresCommand = new RelayCommand(o => { foreach (var c in Cores) if (c.IsECore) c.IsChecked = false; });

            ToggleSettingCommand = new RelayCommand(o => {
                _settingsService.SaveSettings(AppSettings);

                if (AppSettings.RunAsAdministrator && !_settingsService.IsRunAsAdmin())
                {
                    _settingsService.RestartAsAdmin();
                }
            });

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += async (s, e) => await RefreshStatisticsAsync();
            _refreshTimer.Start();

            AddLog($"System initialized. App Version v{AppVersion}");
            _ = CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                await Task.Delay(2000);
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                string noCacheUrl = $"{_versionRawUrl}?t={Guid.NewGuid()}";
                string latestVersionStr = await client.GetStringAsync(noCacheUrl);
                latestVersionStr = latestVersionStr.Trim();

                if (latestVersionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    latestVersionStr = latestVersionStr.Substring(1);
                }

                if (Version.TryParse(latestVersionStr, out Version latestVersion) &&
                    Version.TryParse(AppVersion, out Version currentVersion))
                {
                    if (latestVersion > currentVersion)
                    {
                        AddLog($"Update available: v{latestVersionStr}");

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var result = System.Windows.MessageBox.Show(
                                $"A new version (v{latestVersionStr}) is available! Open download page?",
                                "Update Available",
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Information);

                            if (result == System.Windows.MessageBoxResult.Yes)
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
            }
            catch { }
        }

        private void SwitchTab(string? tabName)
        {
            if (string.IsNullOrEmpty(tabName)) return;
            ActiveTab = tabName;
            ViewProcesses = tabName == "Processes" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ViewProfiles = tabName == "Profiles" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ViewHardwareMonitor = tabName == "Hardware" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ViewSettings = tabName == "Settings" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            SelectedProcess = null;
            SelectedSavedProfile = null;
            if (tabName == "Hardware") _hwTimer.Start();
            else _hwTimer.Stop();
        }

        private HardwareMetrics _fullMetrics = new();
        public HardwareMetrics FullMetrics { get => _fullMetrics; set => SetProperty(ref _fullMetrics, value); }

        private void UpdateHardwareMetrics()
        {
            if (System.Windows.Application.Current.MainWindow?.IsVisible != true) return;
            var m = _hwService.GetAllMetrics();
            FullMetrics = m;
            MonCpuTemp = Math.Round(m.CpuTemp, 1);
            MonGpuTemp = Math.Round(m.GpuTemp, 1);
            MonGpuLoad = Math.Round(m.GpuLoad, 1);
            MonRamUsage = Math.Round(m.RamUsagePct, 1);
        }

        private void AddLog(string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                ActionLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (ActionLogs.Count > 100) ActionLogs.RemoveAt(0);
            });
        }

        private async Task RefreshStatisticsAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                var systemProcesses = await Task.Run(() => Process.GetProcesses());
                double elapsedSeconds = (DateTime.Now - _lastSampleTime).TotalSeconds;
                _lastSampleTime = DateTime.Now;

                var coreLoads = _hardwareService.GetCurrentLoads();
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    for (int i = 0; i < Cores.Count && i < coreLoads.Count; i++)
                        Cores[i].LoadUsage = coreLoads[i];
                });

                var userProcs = systemProcesses.Where(p => _processService.IsUserProcess(p)).ToList();
                var groupedProcs = userProcs.GroupBy(p => p.ProcessName).ToList();

                foreach (var group in groupedProcs)
                {
                    if (Processes.All(x => x.Name != group.Key))
                    {
                        var profile = _profiles.FirstOrDefault(x => x.ProcessName.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
                        string rawPrio = profile?.Priority ?? _processService.GetPriorityString(group.First().Id);

                        if (profile != null && profile.IsEnabled)
                        {
                            foreach (var p in group)
                            {
                                _processService.SetProcessAffinity(p.Id, profile.AffinityMask);
                                if (Enum.TryParse(profile.Priority, out ProcessPriorityClass parsedPrio))
                                    _processService.SetPriority(p.Id, parsedPrio);
                            }
                        }

                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            Processes.Add(new ProcessItem { Name = group.Key, Priority = TranslatePriority(rawPrio), IsOptimized = profile != null });
                        });
                    }
                }

                for (int i = Processes.Count - 1; i >= 0; i--)
                {
                    var item = Processes[i];
                    var procsInGroup = userProcs.Where(p => p.ProcessName == item.Name).ToList();

                    if (!procsInGroup.Any())
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => Processes.RemoveAt(i));
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
                                double usage = (currentCpu - lastTime).TotalMilliseconds;
                                totalCpuPct += (usage / (elapsedSeconds * 1000 * Environment.ProcessorCount)) * 100;
                            }
                            _lastCpuTime[p.Id] = currentCpu;
                            currentPrio = p.PriorityClass.ToString();
                        }
                        catch { _lastCpuTime.Remove(p.Id); }
                    }

                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        item.RamUsageMB = $"{totalMem / 1024 / 1024} MB";
                        item.CpuUsage = $"{Math.Round(totalCpuPct, 1)}%";
                        item.Priority = TranslatePriority(currentPrio);
                    });
                }
            }
            finally { _isRefreshing = false; }
        }

        private void ChangePriority(string priorityRawName)
        {
            if (SelectedProcess == null) return;
            if (Enum.TryParse(priorityRawName, out ProcessPriorityClass priority))
            {
                var procs = Process.GetProcessesByName(SelectedProcess.Name);
                int count = 0;
                foreach (var p in procs) { if (_processService.SetPriority(p.Id, priority)) count++; }
                SelectedProcess.Priority = TranslatePriority(priorityRawName);

                AddLog($"Set '{SelectedProcess.Name}' priority to {TranslatePriority(priorityRawName)} ({count} processes)");
            }
        }

        private void ApplyAffinity()
        {
            if (SelectedProcess == null) return;
            long mask = 0;
            int coreCount = 0;
            for (int i = 0; i < Cores.Count; i++) { if (Cores[i].IsChecked) { mask |= (1L << i); coreCount++; } }
            if (mask == 0) return;

            var procs = Process.GetProcessesByName(SelectedProcess.Name);
            foreach (var p in procs) { _processService.SetProcessAffinity(p.Id, mask); }
            SelectedProcess.IsOptimized = true;
            AddLog($"Set '{SelectedProcess.Name}' affinity to {coreCount} cores.");
        }

        private void SaveCurrentAsProfile()
        {
            if (SelectedProcess == null) return;
            long mask = 0;
            for (int i = 0; i < Cores.Count; i++) { if (Cores[i].IsChecked) mask |= (1L << i); }
            if (mask == 0) return;

            _profiles.RemoveAll(x => x.ProcessName.Equals(SelectedProcess.Name, StringComparison.OrdinalIgnoreCase));

            string rawPrioToSave = UntranslatePriority(_selectedPriority);
            var newProfile = new ProcessProfile { ProcessName = SelectedProcess.Name, AffinityMask = mask, Priority = rawPrioToSave };
            _profiles.Add(newProfile);
            _profileService.SaveProfiles(_profiles);

            SavedProfiles.Clear();
            foreach (var p in _profiles) SavedProfiles.Add(p);

            SelectedProcess.IsOptimized = true;
            AddLog($"Saved new profile for '{SelectedProcess.Name}'");
        }

        private void UpdateSelectedProfile()
        {
            if (SelectedSavedProfile == null) return;
            long mask = 0;
            for (int i = 0; i < Cores.Count; i++) { if (Cores[i].IsChecked) mask |= (1L << i); }

            SelectedSavedProfile.AffinityMask = mask;
            SelectedSavedProfile.Priority = UntranslatePriority(_selectedPriority);

            _profiles = SavedProfiles.ToList();
            _profileService.SaveProfiles(_profiles);
            AddLog($"Updated profile: '{SelectedSavedProfile.ProcessName}'");
        }

        private void DeleteSelectedProfile()
        {
            if (SelectedSavedProfile == null) return;
            string name = SelectedSavedProfile.ProcessName;

            SavedProfiles.Remove(SelectedSavedProfile);
            _profiles = SavedProfiles.ToList();
            _profileService.SaveProfiles(_profiles);

            var activeProcess = Processes.FirstOrDefault(p => p.Name == name);
            if (activeProcess != null) activeProcess.IsOptimized = false;

            AddLog($"Deleted profile: '{name}'");
            SelectedSavedProfile = null;
        }

        private void UpdateCoreSelectionFromAffinity(string processName)
        {
            try
            {
                var procs = Process.GetProcessesByName(processName);
                if (procs.Any())
                {
                    long mask = (long)procs.First().ProcessorAffinity;
                    for (int i = 0; i < Cores.Count; i++) { Cores[i].IsChecked = (mask & (1L << i)) != 0; }
                }
            }
            catch { }
        }
    }
}