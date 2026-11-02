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
        private static bool _restartHandoff;

        public static AppStartupOptions StartupOptions { get; private set; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            StartupOptions = AppStartupOptions.Parse(e.Args);

            // Elevated helper entry: perform the privileged task and exit with a code.
            // No UI, no mutex, no side effects beyond the requested operation.
            var helper = StartupHelperCommand.TryParse(e.Args);
            if (helper != null)
            {
                int exitCode;
                try
                {
                    using var service = new StartupService();
                    exitCode = service.RunStartupHelper(e.Args);
                }
                catch (Exception ex)
                {
                    LoggerService.Instance.Warn($"Startup helper failed: {ex.Message}");
                    exitCode = 1;
                }

                Shutdown(exitCode);
                return;
            }

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

        /// <summary>
        /// Elevated restart handoff. Releases the single-instance mutex first so the
        /// successor can start, launches it with runas, then shuts down. On UAC
        /// cancel or launch failure the mutex is re-acquired and the app keeps running.
        /// </summary>
        public static RestartElevatedResult RequestElevatedRestart(StartupService service, string[] forwardArgs)
        {
            try
            {
                try
                {
                    _singleInstanceMutex?.ReleaseMutex();
                    _singleInstanceMutex?.Dispose();
                }
                catch { }
                _singleInstanceMutex = null;
                _restartHandoff = true;

                var outcome = service.RestartAsAdmin(forwardArgs);
                if (outcome != RestartResult.Started)
                {
                    ReacquireMutexAfterHandoff();
                    return outcome == RestartResult.Cancelled
                        ? RestartElevatedResult.Cancelled
                        : RestartElevatedResult.Failed;
                }

                Current.Shutdown();
                return RestartElevatedResult.Started;
            }
            catch
            {
                ReacquireMutexAfterHandoff();
                return RestartElevatedResult.Failed;
            }
        }

        private static void ReacquireMutexAfterHandoff()
        {
            _restartHandoff = false;
            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
                if (!createdNew)
                {
                    // Another instance (the successor or a foreign launch) owns the
                    // mutex now. Staying alive without it would allow two active
                    // watchers, so this process steps aside instead.
                    try { _singleInstanceMutex.Dispose(); } catch { }
                    _singleInstanceMutex = null;
                    try { Current.Shutdown(); } catch { }
                }
            }
            catch
            {
                _singleInstanceMutex = null;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // During a restart handoff the mutex is already released for the successor.
            if (!_restartHandoff)
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
            }

            LoggerService.Shared.Dispose();
            base.OnExit(e);
        }
    }

    public enum RestartElevatedResult
    {
        Started,
        Cancelled,
        Failed
    }
}
