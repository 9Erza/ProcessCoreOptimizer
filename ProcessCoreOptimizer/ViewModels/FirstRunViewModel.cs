using ProcessCoreOptimizer.WPF.Helpers;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Windows.Input;

namespace ProcessCoreOptimizer.WPF.ViewModels
{
    /// <summary>
    /// Short first-run wizard: welcome + language, CPU modes explainer,
    /// startup behavior. Never enables admin, monitoring or aggressive
    /// optimization by default. Finish/Skip stamps onboarding as completed.
    /// </summary>
    public class FirstRunViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService = new();
        private int _pageIndex;
        private string _language = "en";
        private StartupWindowMode _windowMode = StartupWindowMode.Normal;
        private bool _startWithWindows;
        private bool _runElevated;

        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (SetProperty(ref _pageIndex, Math.Clamp(value, 0, TotalPages - 1)))
                {
                    OnPropertyChanged(nameof(IsFirstPage));
                    OnPropertyChanged(nameof(IsLastPage));
                }
            }
        }

        public int TotalPages => 3;
        public bool IsFirstPage => PageIndex == 0;
        public bool IsLastPage => PageIndex == TotalPages - 1;

        public string Language
        {
            get => _language;
            set => SetProperty(ref _language, value == "pl" ? "pl" : "en");
        }

        public bool IsPolishLanguage
        {
            get => Language == "pl";
            set => Language = value ? "pl" : "en";
        }

        public bool IsEnglishLanguage
        {
            get => Language != "pl";
            set { if (value) Language = "en"; }
        }

        public System.Collections.Generic.IReadOnlyList<StartupWindowMode> StartupWindowModes { get; } = new[]
        {
            StartupWindowMode.Normal,
            StartupWindowMode.Minimized,
            StartupWindowMode.Tray
        };

        public StartupWindowMode WindowMode
        {
            get => _windowMode;
            set => SetProperty(ref _windowMode, value);
        }

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set => SetProperty(ref _startWithWindows, value);
        }

        public bool RunElevated
        {
            get => _runElevated;
            set => SetProperty(ref _runElevated, value);
        }

        public ICommand NextCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand SkipCommand { get; }
        public ICommand FinishCommand { get; }

        public event Action? RequestClose;

        public FirstRunViewModel()
        {
            NextCommand = new RelayCommand(_ => PageIndex++);
            BackCommand = new RelayCommand(_ => PageIndex--);
            SkipCommand = new RelayCommand(_ => Complete(applyChoices: false));
            FinishCommand = new RelayCommand(_ => Complete(applyChoices: true));
        }

        public void LoadFrom(AppSettings settings)
        {
            Language = settings.Language;
            WindowMode = settings.StartupWindowMode;
            StartWithWindows = settings.StartWithWindows;
            RunElevated = settings.StartupRunElevated;
        }

        private void Complete(bool applyChoices)
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                settings.Language = Language;
                if (applyChoices)
                {
                    settings.StartWithWindows = StartWithWindows;
                    settings.StartupWindowMode = WindowMode;
                    settings.StartupRunElevated = RunElevated;
                    settings.HardwareMonitorEnabled = false;
                    settings.AllowRealtimePriority = false;
                }

                settings.OnboardingCompletedVersion = 1;
                _settingsService.SaveSettings(settings);
            }
            catch
            {
                // Wizard must never block startup on a settings write failure.
            }

            RequestClose?.Invoke();
        }
    }
}
