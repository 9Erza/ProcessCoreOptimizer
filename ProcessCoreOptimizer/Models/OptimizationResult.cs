using System.Collections.Generic;

namespace ProcessCoreOptimizer.WPF.Models
{
    public sealed class OptimizationResult
    {
        public bool Success { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public OptimizationMode Mode { get; set; } = OptimizationMode.Affinity;
        public string Priority { get; set; } = "Normal";
        public string Message { get; set; } = string.Empty;
        public bool CoreApplied { get; set; }
        public bool PriorityApplied { get; set; }
        public bool EcoQosApplied { get; set; }
        public bool RequiresAdmin { get; set; }
        public int? Win32Error { get; set; }

        /// <summary>
        /// Authoritative outcome. <see cref="Success"/> stays true when at least one
        /// requested action was applied (backward compatible); use <see cref="Status"/>
        /// to distinguish full success from partial success.
        /// </summary>
        public OperationStatus Status { get; set; } = OperationStatus.Failed;

        /// <summary>True only when every requested action was applied.</summary>
        public bool IsFullSuccess => Status.IsFullSuccess();

        /// <summary>Whether retrying later can help (partial/failed, not skips).</summary>
        public bool Retryable { get; set; }

        public bool RequestedCore { get; set; }
        public bool RequestedPriority { get; set; }
        public bool RequestedEcoQos { get; set; }
    }

    public sealed class OptimizationBatchResult
    {
        public int Total { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public List<OptimizationResult> Results { get; set; } = new();

        public bool HasAnySuccess => Successful > 0;

        /// <summary>Results where every requested action was applied.</summary>
        public int FullyApplied => Results.Count(r => r.Status.IsFullSuccess());
    }
}
