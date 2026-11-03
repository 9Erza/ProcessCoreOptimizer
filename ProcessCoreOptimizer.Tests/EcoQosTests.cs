using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Diagnostics;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class EcoQosTests
{
    [TestMethod]
    public void ResolveStatus_EcoOnly_Success_IsFullyApplied()
    {
        var s = OptimizationService.ResolveStatus(
            success: true, allRequested: true, requiresAdmin: false,
            requestedCore: false, requestedPriority: false,
            coreMessage: "SKIPPED_CORE", priorityMessage: "SKIPPED_PRIORITY",
            requestedEcoQos: true, ecoMessage: "OK_ECOQOS_ON");
        Assert.AreEqual(OperationStatus.FullyApplied, s);
    }

    [TestMethod]
    public void ResolveStatus_EcoUnsupportedAlone_IsUnsupported()
    {
        var s = OptimizationService.ResolveStatus(
            success: false, allRequested: false, requiresAdmin: false,
            requestedCore: false, requestedPriority: false,
            coreMessage: "SKIPPED_CORE", priorityMessage: "SKIPPED_PRIORITY",
            requestedEcoQos: true, ecoMessage: "ERR_ECOQOS_UNSUPPORTED");
        Assert.AreEqual(OperationStatus.Unsupported, s);
    }

    [TestMethod]
    public void ResolveStatus_EcoFailed_WithCoreSuccess_IsPartial()
    {
        var s = OptimizationService.ResolveStatus(
            success: true, allRequested: false, requiresAdmin: false,
            requestedCore: true, requestedPriority: false,
            coreMessage: "OK_AFFINITY", priorityMessage: "SKIPPED_PRIORITY",
            requestedEcoQos: true, ecoMessage: "ERR_ECOQOS_5");
        Assert.AreEqual(OperationStatus.PartiallyApplied, s);
    }

    [TestMethod]
    public void ProfileSanitize_PreservesEcoQosNull()
    {
        var p = new ProcessProfile { ProcessName = "myapp", AffinityMask = 0xFF, EcoQos = null };
        var sane = string.Join(",", SmartRuleService.SanitizeRules(Array.Empty<SmartRule>()));
        Assert.AreEqual(string.Empty, sane);
        Assert.IsNull(p.EcoQos);
    }

    [TestMethod]
    public void TryGetEcoQos_BogusPid_FailsSafely()
    {
        var service = new ProcessService();
        var (readable, enabled, error) = service.TryGetEcoQos(-12345);
        Assert.IsFalse(readable);
        Assert.IsNull(enabled);
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }

    [TestMethod]
    public void TryGetEcoQos_SelfProcess_ReadsWithoutThrowing()
    {
        // Read-only on our own test process: no mutation, no other process touched.
        var service = new ProcessService();
        int pid = Process.GetCurrentProcess().Id;
        var (readable, enabled, error) = service.TryGetEcoQos(pid);
        if (ProcessService.IsEcoQosSupported())
        {
            Assert.IsTrue(readable, $"Expected readable EcoQoS for self PID, error={error}");
            Assert.IsTrue(enabled.HasValue);
        }
        else
        {
            Assert.IsFalse(readable);
        }
    }

    [TestMethod]
    public void SetEcoQos_InvalidTarget_RefusedWithoutMutation()
    {
        var service = new ProcessService();
        Assert.IsFalse(ProcessMutationTarget.TryCreate(-99, default, "x", null, out _));
        var target = default(ProcessMutationTarget);
        var (success, message, _) = service.SetEcoQos(-99, true, target);
        Assert.IsFalse(success);
        Assert.IsTrue(message.Contains("IDENTITY") || message.Contains("UNSUPPORTED"));
    }
}
