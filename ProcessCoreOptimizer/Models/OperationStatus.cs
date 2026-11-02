using System;

namespace ProcessCoreOptimizer.WPF.Models
{
    /// <summary>
    /// Authoritative outcome of a single process optimization attempt.
    /// Full success means every requested action was applied; anything less
    /// is reported honestly so the UI and watcher can react correctly.
    /// Message codes in <see cref="OptimizationResult.Message"/> stay stable
    /// for log parsing (e.g. "SKIPPED_ALREADY_APPLIED").
    /// </summary>
    public enum OperationStatus
    {
        /// <summary>Every requested action was applied.</summary>
        FullyApplied,

        /// <summary>At least one requested action applied, at least one failed. Retry makes sense.</summary>
        PartiallyApplied,

        /// <summary>Temporarily suspended (e.g. after manual session restore); expires automatically.</summary>
        SkippedSuspended,

        /// <summary>At least one action attempted, none applied.</summary>
        Failed,

        /// <summary>Signature already cached; OS not touched.</summary>
        SkippedAlreadyApplied,

        /// <summary>Live identity did not match the snapshot; OS not touched.</summary>
        SkippedIdentityMismatch,

        /// <summary>System/protected process; OS not touched.</summary>
        SkippedProtected,

        /// <summary>OS refused (access denied / elevation needed); retry only after elevation.</summary>
        SkippedNoPermission,

        /// <summary>Request itself invalid (empty mask, unknown mode, no valid CPU Sets).</summary>
        Unsupported,

        /// <summary>Cancelled before touching the OS (shutdown/dispose).</summary>
        Cancelled
    }

    public static class OperationStatusExtensions
    {
        public static bool IsSuccess(this OperationStatus status) =>
            status is OperationStatus.FullyApplied or OperationStatus.PartiallyApplied;

        public static bool IsFullSuccess(this OperationStatus status) =>
            status == OperationStatus.FullyApplied;

        /// <summary>Whether retrying later (e.g. next watcher tick) can help.</summary>
        public static bool IsRetryable(this OperationStatus status) =>
            status is OperationStatus.PartiallyApplied or OperationStatus.Failed;

        public static string ToMessageCode(this OperationStatus status) => status switch
        {
            OperationStatus.FullyApplied => "APPLIED",
            OperationStatus.PartiallyApplied => "PARTIALLY_APPLIED",
            OperationStatus.SkippedSuspended => "SKIPPED_SUSPENDED",
            OperationStatus.Failed => "FAILED",
            OperationStatus.SkippedAlreadyApplied => "SKIPPED_ALREADY_APPLIED",
            OperationStatus.SkippedIdentityMismatch => "SKIPPED_IDENTITY_MISMATCH",
            OperationStatus.SkippedProtected => "SKIPPED_PROTECTED",
            OperationStatus.SkippedNoPermission => "SKIPPED_NO_PERMISSION",
            OperationStatus.Unsupported => "UNSUPPORTED",
            OperationStatus.Cancelled => "CANCELLED",
            _ => "UNKNOWN"
        };
    }
}
