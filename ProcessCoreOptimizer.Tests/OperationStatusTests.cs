using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System.Collections.Generic;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class OperationStatusTests
{
    [TestMethod]
    public void ValidateMask_EmptyMask_Rejected()
    {
        Assert.AreEqual("ERR_EMPTY_MASK", ProcessService.ValidateCoreMask(0, new Dictionary<int, uint> { [0] = 1 }));
    }

    [TestMethod]
    public void ValidateMask_BitsOutsideMap_Rejected()
    {
        var map = new Dictionary<int, uint> { [0] = 10, [1] = 11 };
        Assert.AreEqual("ERR_MASK_OUT_OF_RANGE", ProcessService.ValidateCoreMask(0b100, map));
        Assert.IsNull(ProcessService.ValidateCoreMask(0b11, map));
    }

    [TestMethod]
    public void KnownProcessorMask_FromMap_OnlyKnownBits()
    {
        var map = new Dictionary<int, uint> { [0] = 10, [3] = 13 };
        Assert.AreEqual(0b1001L, ProcessService.KnownProcessorMask(map));
    }

    [TestMethod]
    public void ResolveAffinityResult_SurfacesFailedClear()
    {
        Assert.AreEqual("OK_AFFINITY", ProcessService.ResolveAffinityResult(true));
        string degraded = ProcessService.ResolveAffinityResult(false);
        Assert.AreNotEqual("OK_AFFINITY", degraded, "A failed CPU Sets cleanup must not hide behind plain OK_AFFINITY.");
        Assert.IsTrue(degraded.StartsWith("OK"), "Affinity itself still applied, so the code stays in the OK family.");
    }

    [TestMethod]
    public void ResolveStatus_FullVsPartialVsFailed()
    {
        Assert.AreEqual(OperationStatus.FullyApplied,
            OptimizationService.ResolveStatus(true, true, false, true, true, "OK_AFFINITY", "OK_PRIORITY"));
        Assert.AreEqual(OperationStatus.PartiallyApplied,
            OptimizationService.ResolveStatus(true, false, false, true, true, "OK_AFFINITY", "ERR_PRIORITY"));
        Assert.AreEqual(OperationStatus.Failed,
            OptimizationService.ResolveStatus(false, false, false, true, true, "ERR_CPUSETS_5", "ERR_PRIORITY"));
    }

    [TestMethod]
    public void ResolveStatus_NoPermission_OnlyWithoutAnySuccess()
    {
        Assert.AreEqual(OperationStatus.SkippedNoPermission,
            OptimizationService.ResolveStatus(false, false, true, true, false, "ERR_OPEN_PROCESS_5", "SKIPPED_PRIORITY"));
        // Partial success with an elevation hint on the failed half stays retryable partial.
        Assert.AreEqual(OperationStatus.PartiallyApplied,
            OptimizationService.ResolveStatus(true, false, true, true, true, "ERR_CPUSETS_5", "OK_PRIORITY"));
    }

    [TestMethod]
    public void ResolveStatus_ValidationErrors_AreUnsupported()
    {
        Assert.AreEqual(OperationStatus.Unsupported,
            OptimizationService.ResolveStatus(false, false, false, true, false, "ERR_MASK_OUT_OF_RANGE", "SKIPPED_PRIORITY"));
        Assert.AreEqual(OperationStatus.Unsupported,
            OptimizationService.ResolveStatus(false, false, false, true, false, "ERR_EMPTY_MASK", "SKIPPED_PRIORITY"));
        Assert.AreEqual(OperationStatus.Unsupported,
            OptimizationService.ResolveStatus(false, false, false, false, false, "SKIPPED_CORE", "SKIPPED_PRIORITY"));
    }

    [TestMethod]
    public void StatusHelpers_RetrySemantics()
    {
        Assert.IsTrue(OperationStatus.PartiallyApplied.IsRetryable());
        Assert.IsTrue(OperationStatus.Failed.IsRetryable());
        Assert.IsFalse(OperationStatus.FullyApplied.IsRetryable());
        Assert.IsFalse(OperationStatus.SkippedAlreadyApplied.IsRetryable());
        Assert.IsFalse(OperationStatus.SkippedIdentityMismatch.IsRetryable());
        Assert.IsFalse(OperationStatus.SkippedProtected.IsRetryable());
        Assert.IsFalse(OperationStatus.SkippedNoPermission.IsRetryable());
        Assert.IsFalse(OperationStatus.Cancelled.IsRetryable());
        Assert.IsTrue(OperationStatus.FullyApplied.IsSuccess());
        Assert.IsTrue(OperationStatus.PartiallyApplied.IsSuccess());
        Assert.IsFalse(OperationStatus.Failed.IsSuccess());
    }
}
