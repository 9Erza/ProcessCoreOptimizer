using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Bounded in-session history of applied profile operations.
    /// Diagnostics only: never persisted, never feeds decisions, never mutates processes.
    /// </summary>
    public sealed class AppliedOperationHistory
    {
        public const int MaxEntries = 200;

        private readonly List<AppliedOperation> _entries = new();
        private readonly object _lock = new();
        private readonly ILogger _logger = LoggerService.Instance;

        public void Record(OptimizationResult result, string source)
        {
            if (result == null) return;
            lock (_lock)
            {
                _entries.Add(new AppliedOperation
                {
                    AtUtc = DateTime.UtcNow,
                    ProcessName = result.ProcessName,
                    ProcessId = result.ProcessId,
                    Mode = result.Mode,
                    Priority = result.Priority,
                    Status = result.Status,
                    Message = result.Message,
                    CoreApplied = result.CoreApplied,
                    PriorityApplied = result.PriorityApplied,
                    RequiresAdmin = result.RequiresAdmin,
                    Win32Error = result.Win32Error,
                    Source = source
                });

                while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }
        }

        public void RecordBatch(OptimizationBatchResult batch, string source)
        {
            if (batch == null) return;
            foreach (var result in batch.Results) Record(result, source);
        }

        public IReadOnlyList<AppliedOperation> Recent(int count = 50)
        {
            lock (_lock)
            {
                return _entries
                    .OrderByDescending(e => e.AtUtc)
                    .Take(Math.Max(count, 0))
                    .ToList();
            }
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }
    }

    public sealed class AppliedOperation
    {
        public DateTime AtUtc { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public OptimizationMode Mode { get; set; }
        public string Priority { get; set; } = string.Empty;
        public OperationStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool CoreApplied { get; set; }
        public bool PriorityApplied { get; set; }
        public bool RequiresAdmin { get; set; }
        public int? Win32Error { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
