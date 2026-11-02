using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.WPF.Services
{
    public interface IStartupConfigurationBackend
    {
        Task<ActualStartupConfiguration> ReadActualAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default);
        Task<StartupOperationResult> ExecuteAsync(StartupOperation operation, DesiredStartupConfiguration desired, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Plan → execute → verify for startup registration. Fails closed on unreadable
    /// state; never declares success when the final evaluation differs from desired.
    /// </summary>
    public sealed class StartupConfigurationExecutor
    {
        private readonly IStartupConfigurationBackend _backend;
        private readonly ILogger _logger;

        public StartupConfigurationExecutor(IStartupConfigurationBackend backend, ILogger? logger = null)
        {
            _backend = backend;
            _logger = logger ?? LoggerService.Instance;
        }

        public async Task<StartupApplyResult> ApplyAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
        {
            var initial = StartupConfigurationPlanner.Evaluate(desired, await _backend.ReadActualAsync(desired, cancellationToken).ConfigureAwait(false));
            _logger.Info($"Startup planner. State={initial.State}; Reasons={string.Join(',', initial.Reasons)}; Operations={string.Join(',', initial.RequiredOperations)}.");
            if (initial.Reasons.Contains(StartupReason.ReadFailed))
                return new(false, false, false, Array.Empty<StartupOperation>(), initial, "Startup configuration could not be read safely.");

            var attempted = new List<StartupOperation>();
            bool elevationRequired = false;
            bool elevationCancelled = false;
            foreach (var operation in initial.RequiredOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempted.Add(operation);
                var result = await _backend.ExecuteAsync(operation, desired, cancellationToken).ConfigureAwait(false);
                elevationRequired |= result.ElevationRequired;
                elevationCancelled |= result.ElevationCancelled;
                if (!result.Success)
                {
                    var finalAfterFailure = StartupConfigurationPlanner.Evaluate(desired, await _backend.ReadActualAsync(desired, cancellationToken).ConfigureAwait(false));
                    return new(false, elevationRequired, elevationCancelled, attempted, finalAfterFailure, result.Error);
                }
            }

            var final = StartupConfigurationPlanner.Evaluate(desired, await _backend.ReadActualAsync(desired, cancellationToken).ConfigureAwait(false));
            bool healthy = !final.Reasons.Contains(StartupReason.ReadFailed) &&
                (desired.StartWithWindows
                    ? desired.RunElevated ? final.State == StartupState.ElevatedTask : final.State == StartupState.Registry
                    : final.State == StartupState.Disabled);
            _logger.Info($"Startup final evaluation. State={final.State}; Reasons={string.Join(',', final.Reasons)}; Success={healthy}.");
            return new(healthy, elevationRequired, elevationCancelled, attempted, final, healthy ? null : "Startup configuration differs from the requested configuration.");
        }
    }

    /// <summary>
    /// Serializes concurrent apply requests (latest-wins with retry) so rapid UI
    /// toggles converge on the newest desired configuration.
    /// </summary>
    public sealed class StartupApplyCoordinator
    {
        private readonly StartupConfigurationExecutor _executor;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _sync = new();
        private DesiredStartupConfiguration? _latestDesired;
        private long _desiredVersion;

        public StartupApplyCoordinator(StartupConfigurationExecutor executor)
        {
            _executor = executor;
        }

        public async Task<StartupApplyResult> ApplyLatestAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
        {
            long version;
            lock (_sync)
            {
                _latestDesired = desired;
                _desiredVersion++;
                version = _desiredVersion;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    DesiredStartupConfiguration snapshot;
                    lock (_sync) snapshot = _latestDesired!;
                    var result = await _executor.ApplyAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (version == _desiredVersion) return result;
                        version = _desiredVersion;
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
