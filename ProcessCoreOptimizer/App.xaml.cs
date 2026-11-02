using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using ProcessCoreOptimizer.WPF.Logging;
using System;
using System.Threading;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace ProcessCoreOptimizer.WPF
{
    public partial class App : System.Windows.Application
    {
        private const string SingleInstanceMutexName = "ERZA.ProcessCoreOptimizer.SingleInstance";
        private static Mutex? _singleInstanceMutex;

        public static AppStartupOptions StartupOptions { get; private set; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            StartupOptions = AppStartupOptions.Parse(e.Args);

            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
                if (!createdNew)
                {
                    WpfMessageBox.Show(
                        "Process Core Optimizer is already running.",
                        "Process Core Optimizer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    Shutdown();
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                // Previous instance crashed without releasing the mutex.
                // We now own it; continue startup instead of crashing.
                try
                {
                    _singleInstanceMutex = Mutex.OpenExisting(SingleInstanceMutexName);
                }
                catch
                {
                    _singleInstanceMutex = null;
                }
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch
            {
                // Ignore shutdown cleanup errors.
            }

            LoggerService.Shared.Dispose();
            base.OnExit(e);
        }
    }
}
