using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Authoritative owner of the background profile-watching pipeline.
    /// The UI keeps its own refresh timer; this service owns scan → re-validate →
    /// apply → cleanup → history with reentrancy, cancellation and shutdown guards.
    /// After <see cref="Dispose"/> no OS mutation is performed.
    /// </summary>
    public sealed class ProfileWatcherService : IDisposable
    {
        private readonly ProcessScannerService _scanner;
        private readonly OptimizationService _optimization;
        private readonly AppliedOperationHistory _history;
        private readonly Func<IReadOnlyList<ProcessProfile>> _profilesProvider;
        private readonly Func<bool> _allowRealtimeProvider;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public ProfileWatcherService(
            ProcessScannerService scanner,
            OptimizationService optimization,
            AppliedOperationHistory history,
            Func<IReadOnlyList<ProcessProfile>> profilesProvider,
            Func<bool> allowRealtimeProvider)
        {
            _scanner = scanner;
            _optimization = optimization;
            _history = history;
            _profilesProvider = profilesProvider;
            _allowRealtimeProvider = allowRealtimeProvider;
            _logger = LoggerService.Instance;
        }

        public WatcherRunInfo LastRun { get; private set; } = new();

        public event EventHandler<OptimizationBatchResult>? BatchCompleted;

        /// <summary>
        /// Fired after every pass with the scan that drove it, so automation rules
        /// can evaluate against the same snapshot (no extra process enumeration).
        /// Handlers run on the pass thread; they must be thread-safe.
        /// </summary>
        public event EventHandler<ProcessScanResult>? ScanCompleted;

        /// <summary>
        /// Runs one background pass. Never overlaps, never throws, never mutates
        /// after disposal. Returns null when the run was skipped (busy/disposed).
        /// </summary>
        public async Task<OptimizationBatchResult?> RunOnceAsync(string source, bool force)
        {
            if (_disposed || _cts.IsCancellationRequested) return null;
            if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return null;

            var startedAt = DateTime.UtcNow;
            try
            {
                if (_disposed || _cts.IsCancellationRequested) return null;

                // Consistent snapshot: profile edits mid-run apply on the next pass.
                List<ProcessProfile> profiles = _profilesProvider()?.ToList() ?? new List<ProcessProfile>();
                bool allowRealtime = _allowRealtimeProvider();

                ProcessScanResult scan = await _scanner
                    .ScanProfileProcessesAsync(profiles)
                    .ConfigureAwait(false);

                if (_disposed || _cts.IsCancellationRequested) return null;

                _optimization.CleanupStaleCache(scan.ActiveInstances);
                OptimizationBatchResult batch = _optimization.ApplyProfilesForSnapshots(
                    profiles, scan.Groups, allowRealtime, force);

                if (!_disposed && !_cts.IsCancellationRequested)
                {
                    _history.RecordBatch(batch, source);
                }

                LastRun = new WatcherRunInfo
                {
                    AtUtc = startedAt,
                    Duration = DateTime.UtcNow - startedAt,
                    Source = source,
                    Total = batch.Total,
                    FullyApplied = batch.FullyApplied,
                    Failed = batch.Results.Count(r => r.Status == OperationStatus.Failed),
                    Skipped = batch.Total - batch.Successful - batch.Results.Count(r => r.Status == OperationStatus.Failed)
                };

                try { BatchCompleted?.Invoke(this, batch); } catch (Exception ex)
                {
                    _logger.Debug($"Watcher BatchCompleted handler failed: {ex.Message}");
                }

                try { ScanCompleted?.Invoke(this, scan); } catch (Exception ex)
                {
                    _logger.Debug($"Watcher ScanCompleted handler failed: {ex.Message}");
                }

                return batch;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Background profile watcher pass failed: {ex.Message}");
                LastRun = new WatcherRunInfo
                {
                    AtUtc = startedAt,
                    Duration = DateTime.UtcNow - startedAt,
                    Source = source,
                    Total = 0,
                    Failed = 1,
                    Error = ex.Message
                };
                return new OptimizationBatchResult();
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts.Cancel(); } catch { }
            _cts.Dispose();
            _gate.Dispose();
        }
    }

    public sealed class WatcherRunInfo
    {
        public DateTime AtUtc { get; set; }
        public TimeSpan Duration { get; set; }
        public string Source { get; set; } = string.Empty;
        public int Total { get; set; }
        public int FullyApplied { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public string? Error { get; set; }
    }
}
