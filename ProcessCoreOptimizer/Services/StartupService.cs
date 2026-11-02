using Microsoft.Win32;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Owns Windows startup registration: HKCU Run value for normal autostart,
    /// Task Scheduler logon task for elevated autostart. Only entries owned by
    /// PCO (own value/task names) are ever read, written or removed.
    /// </summary>
    public sealed class StartupService : IStartupConfigurationBackend, IDisposable
    {
        public const string DefaultRegistryValueName = "ProcessCoreOptimizer";
        public const string DefaultTaskName = "ProcessCoreOptimizer";

        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        private readonly string _registryValueName;
        private readonly string _taskName;
        private readonly ITaskSchedulerQuery _taskQuery;
        private readonly ILogger _logger;
        private bool _disposed;

        /// <summary>Test seam: launches a process and returns its exit code.</summary>
        public Func<ProcessStartInfo, int> ProcessLauncher { get; set; } = DefaultProcessLauncher;

        public StartupService(
            string registryValueName = DefaultRegistryValueName,
            string taskName = DefaultTaskName,
            ITaskSchedulerQuery? taskQuery = null,
            ILogger? logger = null)
        {
            _registryValueName = registryValueName;
            _taskName = taskName;
            _taskQuery = taskQuery ?? new TaskSchedulerComQuery();
            _logger = logger ?? LoggerService.Instance;
        }

        public string CurrentExecutablePath { get; } =
            GetCurrentExecutablePath() ?? string.Empty;

        public static string? GetCurrentExecutablePath()
        {
            try
            {
                string? path = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    try { path = Process.GetCurrentProcess().MainModule?.FileName; } catch { path = null; }
                }

                if (string.IsNullOrWhiteSpace(path)) return null;
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return null;
            }
        }

        public bool IsRunAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public async Task<ActualStartupConfiguration> ReadActualAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
        {
            var registry = await Task.Run(() => ReadRegistry(desired), cancellationToken).ConfigureAwait(false);
            var reader = new ScheduledTaskStartupReader(_taskQuery, _taskName, CurrentExecutablePath);
            var task = await Task.Run(() => reader.Read(desired), cancellationToken).ConfigureAwait(false);
            return new ActualStartupConfiguration(registry, task);
        }

        public async Task<StartupOperationResult> ExecuteAsync(StartupOperation operation, DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
        {
            try
            {
                return operation switch
                {
                    StartupOperation.RemoveRegistry => await Task.Run(() => RemoveRegistry(), cancellationToken).ConfigureAwait(false),
                    StartupOperation.RemoveScheduledTask => await Task.Run(() => RemoveTask(desired), cancellationToken).ConfigureAwait(false),
                    StartupOperation.CreateOrUpdateRegistry => await Task.Run(() => WriteRegistry(desired), cancellationToken).ConfigureAwait(false),
                    StartupOperation.CreateOrUpdateScheduledTask => await Task.Run(() => WriteTask(desired), cancellationToken).ConfigureAwait(false),
                    _ => new StartupOperationResult(false, Error: "Unknown startup operation.")
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new StartupOperationResult(false, Error: ex.Message);
            }
        }

        #region Registry

        private RegistryStartupSnapshot ReadRegistry(DesiredStartupConfiguration desired)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                string? raw = key?.GetValue(_registryValueName) as string;
                if (string.IsNullOrWhiteSpace(raw))
                    return new(false, null, string.Empty, false, false, false);

                var (exe, args) = StartupCommandParser.Parse(raw);
                string? full = NormalizePath(exe);
                return new(true, full, args,
                    full != null && File.Exists(full),
                    PathsEqual(full, CurrentExecutablePath),
                    string.Equals(args, desired.Arguments, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                return new(false, null, string.Empty, false, false, false, false, $"Registry read failed: {ex.Message}");
            }
        }

        private StartupOperationResult RemoveRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(_registryValueName, throwOnMissingValue: false);
                return new StartupOperationResult(true);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new StartupOperationResult(false, Error: $"Access denied removing registry startup: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new StartupOperationResult(false, Error: ex.Message);
            }
        }

        private StartupOperationResult WriteRegistry(DesiredStartupConfiguration desired)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CurrentExecutablePath) || !File.Exists(CurrentExecutablePath))
                    return new StartupOperationResult(false, Error: "Current executable path is unavailable.");

                string expected = $"\"{CurrentExecutablePath}\" {desired.Arguments}".Trim();
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key == null) return new StartupOperationResult(false, Error: "Registry Run key is unavailable.");
                key.SetValue(_registryValueName, expected, RegistryValueKind.String);

                string? actual = key.GetValue(_registryValueName) as string;
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    return new StartupOperationResult(false, Error: "Registry read-back verification failed.");
                return new StartupOperationResult(true);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new StartupOperationResult(false, Error: $"Access denied writing registry startup: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new StartupOperationResult(false, Error: ex.Message);
            }
        }

        #endregion

        #region Scheduled task (schtasks create/delete; COM for reads)

        private StartupOperationResult RemoveTask(DesiredStartupConfiguration desired)
        {
            _ = desired;
            var (ok, output) = RunSchtasks($"/delete /tn \"{_taskName}\" /f");
            if (ok) return new StartupOperationResult(true);
            // Exit code 1 with "not exist" text means already gone: treat as success.
            if (output.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                return new StartupOperationResult(true);
            if (IsRunAsAdmin()) return new StartupOperationResult(false, Error: $"schtasks delete failed: {output}");
            return RemoveTaskElevated();
        }

        private StartupOperationResult WriteTask(DesiredStartupConfiguration desired)
        {
            if (string.IsNullOrWhiteSpace(CurrentExecutablePath) || !File.Exists(CurrentExecutablePath))
                return new StartupOperationResult(false, Error: "Current executable path is unavailable.");

            string xml = BuildTaskXml(CurrentExecutablePath, desired.Arguments);
            string tempXml = Path.Combine(Path.GetTempPath(), $"PCO_{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(tempXml, xml, Encoding.Unicode);
            }
            catch (Exception ex)
            {
                return new StartupOperationResult(false, Error: $"Cannot write task XML: {ex.Message}");
            }

            try
            {
                if (IsRunAsAdmin())
                {
                    var (ok, output) = RunSchtasks($"/create /tn \"{_taskName}\" /xml \"{tempXml}\" /f");
                    if (!ok) return new StartupOperationResult(false, Error: $"schtasks create failed: {output}");
                    return VerifyTask(desired);
                }

                return CreateTaskElevated(desired);
            }
            finally
            {
                try { if (File.Exists(tempXml)) File.Delete(tempXml); }
                catch (Exception ex) { _logger.Debug($"Could not delete temp task XML: {ex.Message}"); }
            }
        }

        private StartupOperationResult VerifyTask(DesiredStartupConfiguration desired)
        {
            var reader = new ScheduledTaskStartupReader(_taskQuery, _taskName, CurrentExecutablePath);
            var snap = reader.Read(desired);
            if (snap.Exists && snap.Enabled && snap.ExecutableExists && snap.HasLogonTrigger
                && snap.IsElevated && snap.IsExpectedExecutable && snap.IsExpectedArguments)
                return new StartupOperationResult(true);
            return new StartupOperationResult(false, Error: "Task read-back verification failed.");
        }

        private (bool ok, string output) RunSchtasks(string arguments, int timeoutSeconds = 15)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (proc == null) return (false, "schtasks could not be started.");
                if (!proc.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return (false, "schtasks timed out.");
                }

                string output = (proc.StandardOutput.ReadToEnd() + " " + proc.StandardError.ReadToEnd()).Trim();
                return (proc.ExitCode == 0, string.IsNullOrWhiteSpace(output) ? $"exit code {proc.ExitCode}" : output);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private string BuildTaskXml(string exePath, string arguments)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Process Core Optimizer autostart (user logon, elevated when configured).</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{EscapeXml(exePath)}</Command>
      <Arguments>{EscapeXml(arguments)}</Arguments>
    </Exec>
  </Actions>
</Task>";
        }

        private static string EscapeXml(string value) => value
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

        #endregion

        #region Elevation

        private StartupOperationResult RemoveTaskElevated()
        {
            return RunHelper(new StartupHelperCommand(true, Models.StartupWindowMode.Normal));
        }

        private StartupOperationResult CreateTaskElevated(DesiredStartupConfiguration desired)
        {
            return RunHelper(new StartupHelperCommand(false, desired.WindowMode));
        }

        private StartupOperationResult RunHelper(StartupHelperCommand command)
        {
            if (string.IsNullOrWhiteSpace(CurrentExecutablePath) || !File.Exists(CurrentExecutablePath))
                return new StartupOperationResult(false, Error: "Current executable path is unavailable.");

            var psi = new ProcessStartInfo
            {
                FileName = CurrentExecutablePath,
                Arguments = command.ToArguments(),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                int exitCode = ProcessLauncher(psi);
                if (exitCode == 0) return new StartupOperationResult(true, ElevationRequired: true);
                return new StartupOperationResult(false, ElevationRequired: true, Error: $"Elevated helper failed (exit {exitCode}).");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED: the user dismissed the UAC prompt. Clean cancel.
                return new StartupOperationResult(false, ElevationRequired: true, ElevationCancelled: true, Error: "UAC prompt was cancelled by the user.");
            }
            catch (Exception ex)
            {
                return new StartupOperationResult(false, ElevationRequired: true, Error: ex.Message);
            }
        }

        private static int DefaultProcessLauncher(ProcessStartInfo psi)
        {
            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Helper process could not be started.");
            if (!proc.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("Elevated helper did not complete in time.");
            }

            return proc.ExitCode;
        }

        /// <summary>
        /// Runs the --startup-helper command in-process (already elevated).
        /// Returns a process exit code: 0 ok, 1 failure, 64 unknown command.
        /// </summary>
        public int RunStartupHelper(string[] args)
        {
            var command = StartupHelperCommand.TryParse(args);
            if (command == null) return 64;

            StartupOperationResult result = command.RemoveTask
                ? RemoveTaskDirect()
                : CreateTaskDirect(command.WindowMode);

            if (!result.Success) _logger.Warn($"Startup helper failed: {result.Error}");
            return result.Success ? 0 : 1;
        }

        private StartupOperationResult RemoveTaskDirect()
        {
            var (ok, output) = RunSchtasks($"/delete /tn \"{_taskName}\" /f");
            if (ok || output.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                return new StartupOperationResult(true);
            return new StartupOperationResult(false, Error: output);
        }

        private StartupOperationResult CreateTaskDirect(Models.StartupWindowMode mode)
        {
            return WriteTask(new DesiredStartupConfiguration(true, mode, true));
        }

        /// <summary>
        /// Restarts the app elevated (runas). Never kills the current process:
        /// the caller (App, mutex owner) performs the handoff after Start succeeds.
        /// </summary>
        public RestartResult RestartAsAdmin(string[] forwardArgs)
        {
            if (IsRunAsAdmin() || string.IsNullOrWhiteSpace(CurrentExecutablePath))
                return RestartResult.AlreadyElevated;

            var psi = new ProcessStartInfo
            {
                FileName = CurrentExecutablePath,
                Arguments = string.Join(" ", forwardArgs.Select(a => $"\"{a}\"")),
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return RestartResult.Failed;
                return RestartResult.Started;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return RestartResult.Cancelled;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Administrator restart failed: {ex.Message}");
                return RestartResult.Failed;
            }
        }

        #endregion

        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path.Trim()); } catch { return path.Trim(); }
        }

        private static bool PathsEqual(string? left, string? right) => left != null && right != null &&
            string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    public enum RestartResult
    {
        Started,
        Cancelled,
        Failed,
        AlreadyElevated
    }
}
