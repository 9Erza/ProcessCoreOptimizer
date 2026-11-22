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

            bool alreadyRunning;
            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
                alreadyRunning = !createdNew;
                if (alreadyRunning)
                {
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;
                }
            }
            catch (AbandonedMutexException)
            {
                // Previous instance crashed without releasing the mutex.
                // We now own it; continue startup instead of crashing.
                alreadyRunning = false;
                try
                {
                    _singleInstanceMutex = Mutex.OpenExisting(SingleInstanceMutexName);
                }
                catch
                {
                    _singleInstanceMutex = null;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The mutex belongs to an elevated instance (e.g. started by the
                // elevated logon task): a standard-user process may not open it.
                // That still means PCO is running; never crash here.
                alreadyRunning = true;
            }

            if (alreadyRunning)
            {
                // Ask the running instance to show itself. Autostart launches
                // (--tray / --minimized) that lose the race exit silently instead
                // of greeting the user with a modal box at logon.
                bool signalled = SignalExistingInstance();
                bool unattended = StartupOptions.StartToTray || StartupOptions.StartMinimized;
                if (!signalled && !unattended)
                {
                    WpfMessageBox.Show(
                        "Process Core Optimizer is already running.",
                        "Process Core Optimizer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            StartShowRequestListener();

            base.OnStartup(e);
            Services.StartupProfiler.Mark("App.OnStartup end");
        }

        private const string ShowRequestEventName = "ERZA.ProcessCoreOptimizer.ShowRequest";
        private static EventWaitHandle? _showRequestEvent;
        private static RegisteredWaitHandle? _showRequestWait;

        private static bool SignalExistingInstance()
        {
            if (StartupOptions.StartToTray || StartupOptions.StartMinimized) return true;
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ShowRequestEventName);
                return handle.Set();
            }
            catch
            {
                return false;
            }
        }

        private void StartShowRequestListener()
        {
            try
            {
                _showRequestEvent?.Dispose();
                _showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestEventName);
                _showRequestWait = ThreadPool.RegisterWaitForSingleObject(_showRequestEvent, (_, _) =>
                {
                    try
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            var window = MainWindow;
                            if (window == null) return;
                            window.Show();
                            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                            window.Activate();
                        });
                    }
                    catch
                    {
                        // Best effort: a failed show request must never crash PCO.
                    }
                }, null, Timeout.Infinite, executeOnlyOnce: false);
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Debug($"Show-request listener unavailable: {ex.Message}");
            }
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

            try
            {
                _showRequestWait?.Unregister(null);
                _showRequestEvent?.Dispose();
            }
            catch { }

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
