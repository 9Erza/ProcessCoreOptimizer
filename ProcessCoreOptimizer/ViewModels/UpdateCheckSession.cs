using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.WPF.ViewModels
{
    /// <summary>
    /// Owns the once-per-process automatic update check gate and the update
    /// notification decisions. Reuses <see cref="UpdateService"/> for the
    /// actual version check; no parallel check implementation.
    /// </summary>
    public sealed class UpdateCheckSession
    {
        private readonly Func<Task<UpdateCheckResult>> _checkAsync;
        private int _automaticCheckBegun;

        public UpdateCheckSession(string currentVersion, Func<CancellationToken, Task<UpdateCheckResult>>? checkAsync = null)
        {
            _checkAsync = checkAsync != null
                ? (() => checkAsync(CancellationToken.None))
                : (() => new UpdateService().CheckForUpdatesAsync(currentVersion));
        }

        /// <summary>
        /// Runs the automatic update check at most once per process lifetime.
        /// Returns null when updates are disabled, the check already ran, or
        /// shutdown was requested. Never throws for network failures (the
        /// service converts them to Error results).
        /// </summary>
        public async Task<UpdateCheckResult?> TryRunAutomaticCheckAsync(bool updatesEnabled, CancellationToken cancellationToken = default)
        {
            if (!updatesEnabled) return null;
            if (cancellationToken.IsCancellationRequested) return null;
            if (Interlocked.Exchange(ref _automaticCheckBegun, 1) == 1) return null;
            return await _checkAsync().ConfigureAwait(false);
        }

        /// <summary>Automatic results are silent unless an update is actually available.</summary>
        public static bool ShouldPresentUpdateDialog(UpdateCheckResult? result) =>
            result is not null && string.IsNullOrWhiteSpace(result.Error) && result.IsUpdateAvailable;

        /// <summary>
        /// Decides whether a ready update dialog must wait: never modal over
        /// other programs (tray/minimized), never during the first-run wizard,
        /// never twice in one session. The pending version stays stored and is
        /// presented when the user opens PCO.
        /// </summary>
        public static bool ShouldDeferUpdateDialog(bool windowVisible, bool windowMinimized, bool wizardActive, bool alreadyPresented) =>
            alreadyPresented || wizardActive || !windowVisible || windowMinimized;
    }
}
