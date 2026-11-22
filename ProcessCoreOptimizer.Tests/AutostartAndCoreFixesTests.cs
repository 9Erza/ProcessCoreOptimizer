using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Regression tests for the autostart repair and the CPU engine fixes:
/// Startup apps "disabled" flag, unattended self-repair rules, drift re-apply,
/// EcoQoS-only profile persistence and editor validation.
/// </summary>
[TestClass]
public sealed class AutostartAndCoreFixesTests
{
    private static readonly DesiredStartupConfiguration NormalOn = new(true, StartupWindowMode.Tray, false);
    private static readonly DesiredStartupConfiguration ElevatedOn = new(true, StartupWindowMode.Tray, true);

    private static RegistryStartupSnapshot ValidRegistry(bool disabledInStartupApps = false) =>
        new(true, @"C:\PCO\ProcessCoreOptimizer.exe", "--tray", true, true, true, DisabledInStartupApps: disabledInStartupApps);

    private static RegistryStartupSnapshot NoRegistry() => new(false, null, string.Empty, false, false, false);

    private static ScheduledTaskStartupSnapshot NoTask() => new(false, false, null, string.Empty, false, false, false, false, false);

    private static ScheduledTaskStartupSnapshot ValidTask() =>
        new(true, true, @"C:\PCO\ProcessCoreOptimizer.exe", "--tray", true, true, true, true, true);

    [TestMethod]
    public void Planner_RegistryDisabledInStartupApps_IsBrokenAndRewritten()
    {
        var eval = StartupConfigurationPlanner.Evaluate(NormalOn, new ActualStartupConfiguration(ValidRegistry(disabledInStartupApps: true), NoTask()));
        Assert.AreEqual(StartupState.Broken, eval.State);
        CollectionAssert.Contains(eval.Reasons.ToList(), StartupReason.DisabledInStartupApps);
        CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateRegistry }, eval.RequiredOperations.ToList());
    }

    [TestMethod]
    public void Planner_ValidRegistry_IsHealthy()
    {
        var eval = StartupConfigurationPlanner.Evaluate(NormalOn, new ActualStartupConfiguration(ValidRegistry(), NoTask()));
        Assert.AreEqual(StartupState.Registry, eval.State);
        Assert.IsTrue(StartupConfigurationPlanner.IsHealthy(NormalOn, eval));
        Assert.IsFalse(StartupConfigurationPlanner.CanAutoRepair(NormalOn, eval, isElevated: false));
    }

    [DataTestMethod]
    [DataRow(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [DataRow(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [DataRow(new byte[] { 0x03, 1, 2, 3, 4, 5, 6, 7, 0, 0, 0, 0 }, true)]
    [DataRow(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    public void StartupApprovedBlob_Decodes(byte[] blob, bool disabled)
    {
        Assert.AreEqual(disabled, StartupService.IsStartupApprovedDisabled(blob));
    }

    [TestMethod]
    public void StartupApprovedBlob_MissingOrEmpty_MeansEnabled()
    {
        Assert.IsFalse(StartupService.IsStartupApprovedDisabled(null));
        Assert.IsFalse(StartupService.IsStartupApprovedDisabled(Array.Empty<byte>()));
    }

    [TestMethod]
    public void AutoRepair_MissingRegistry_RepairsWithoutElevation()
    {
        var eval = StartupConfigurationPlanner.Evaluate(NormalOn, new ActualStartupConfiguration(NoRegistry(), NoTask()));
        Assert.IsTrue(StartupConfigurationPlanner.CanAutoRepair(NormalOn, eval, isElevated: false),
            "A wizard/moved-EXE registry entry must be repaired at launch without any prompt.");
    }

    [TestMethod]
    public void AutoRepair_MissingElevatedTask_OnlyWhenAlreadyElevated()
    {
        var eval = StartupConfigurationPlanner.Evaluate(ElevatedOn, new ActualStartupConfiguration(NoRegistry(), NoTask()));
        Assert.IsFalse(StartupConfigurationPlanner.CanAutoRepair(ElevatedOn, eval, isElevated: false),
            "Never raise a surprise UAC prompt at logon.");
        Assert.IsTrue(StartupConfigurationPlanner.CanAutoRepair(ElevatedOn, eval, isElevated: true));
    }

    [TestMethod]
    public void AutoRepair_StaleTaskWhenStartupOff_NeedsElevation()
    {
        var off = new DesiredStartupConfiguration(false, StartupWindowMode.Normal, false);
        var eval = StartupConfigurationPlanner.Evaluate(off, new ActualStartupConfiguration(NoRegistry(), ValidTask()));
        Assert.IsFalse(StartupConfigurationPlanner.CanAutoRepair(off, eval, isElevated: false));
        Assert.IsTrue(StartupConfigurationPlanner.CanAutoRepair(off, eval, isElevated: true));
    }

    [TestMethod]
    public void AutoRepair_ReadFailure_NeverActs()
    {
        var failed = new RegistryStartupSnapshot(false, null, string.Empty, false, false, false, ReadSucceeded: false, Error: "x");
        var eval = StartupConfigurationPlanner.Evaluate(NormalOn, new ActualStartupConfiguration(failed, NoTask()));
        Assert.IsFalse(StartupConfigurationPlanner.CanAutoRepair(NormalOn, eval, isElevated: true));
    }

    [TestMethod]
    public void EcoQosOnlyProfile_SurvivesSanitize()
    {
        var p = new ProcessProfile { ProcessName = "background", ApplyCoreOptimization = false, ApplyPriority = false, EcoQos = true };
        var sanitized = ProfileService.SanitizeProfiles(new[] { p }).ToList();
        Assert.AreEqual(1, sanitized.Count, "EcoQoS-only profiles used to be deleted on the next load.");
        Assert.AreEqual(true, sanitized[0].EcoQos);
    }

    [TestMethod]
    public void ProfileWithNoAction_IsStillDropped()
    {
        var p = new ProcessProfile { ProcessName = "nothing", ApplyCoreOptimization = false, ApplyPriority = false, EcoQos = null };
        Assert.AreEqual(0, ProfileService.SanitizeProfiles(new[] { p }).Count());
    }

    [TestMethod]
    public void DuplicateProfile_KeepsEcoQos()
    {
        var p = new ProcessProfile { ProcessName = "game", AffinityMask = 0x3, EcoQos = false };
        Assert.AreEqual(false, ProfileService.DuplicateProfile(p).EcoQos);
    }

    [TestMethod]
    public void Validator_EcoQosOnly_IsAccepted()
    {
        var r = ProfileStudioValidator.Validate("game", "0", applyCoreOptimization: false, applyPriority: false, ecoQosRequested: true);
        Assert.IsTrue(r.Ok);
    }

    [TestMethod]
    public void Validator_MaskBeyondThisCpu_IsRejected()
    {
        var r = ProfileStudioValidator.Validate("game", "0xFF00", true, true, knownProcessorMask: 0xFF);
        Assert.IsFalse(r.Ok);
        Assert.IsTrue(ProfileStudioValidator.Validate("game", "0xF0", true, true, knownProcessorMask: 0xFF).Ok);
    }

    private static ProcessInstanceKey SelfKey()
    {
        using var self = Process.GetCurrentProcess();
        return new ProcessInstanceKey(self.Id, self.StartTime.ToUniversalTime());
    }

    private static string SelfName()
    {
        using var self = Process.GetCurrentProcess();
        return self.ProcessName;
    }

    private static ProcessProfile CoreProfile() => new()
    {
        ProcessName = SelfName(),
        AffinityMask = 0x1,
        ApplyCoreOptimization = true,
        ApplyPriority = false,
        OptimizationMode = OptimizationMode.Affinity,
        IsEnabled = true
    };

    private static Dictionary<int, uint> Map() => new() { [0] = 10, [1] = 11 };

    [TestMethod]
    public void Drift_ReappliesWhenAnotherProgramChangedSettings_Bounded()
    {
        var fake = new FakeProcessService();
        var engine = new OptimizationService(fake, Map) { DriftCheckInterval = TimeSpan.Zero, MaxDriftReapplies = 2 };
        var profile = CoreProfile();
        var keys = new[] { SelfKey() };

        var first = engine.ApplyProfileToInstances(profile, SelfName(), keys, false, force: false);
        Assert.AreEqual(OperationStatus.FullyApplied, first.Results[0].Status);
        Assert.AreEqual(1, fake.CoreCalls);

        fake.DriftBehavior = () => false;
        Assert.AreEqual(OperationStatus.SkippedAlreadyApplied, engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false).Results[0].Status);
        Assert.AreEqual(1, fake.CoreCalls);

        fake.DriftBehavior = () => true;
        Assert.AreEqual(OperationStatus.FullyApplied, engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false).Results[0].Status);
        Assert.AreEqual(OperationStatus.FullyApplied, engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false).Results[0].Status);
        Assert.AreEqual(3, fake.CoreCalls, "Drift must trigger a re-apply.");

        Assert.AreEqual(OperationStatus.SkippedAlreadyApplied, engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false).Results[0].Status,
            "After the limit PCO stops fighting the application.");
        Assert.AreEqual(3, fake.CoreCalls);
    }

    [TestMethod]
    public void Drift_UnknownRead_NeverReapplies()
    {
        var fake = new FakeProcessService { DriftBehavior = () => null };
        var engine = new OptimizationService(fake, Map) { DriftCheckInterval = TimeSpan.Zero };
        var profile = CoreProfile();
        var keys = new[] { SelfKey() };
        engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false);
        engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false);
        Assert.AreEqual(1, fake.CoreCalls);
    }

    [TestMethod]
    public void Drift_CheckIsThrottledByInterval()
    {
        var fake = new FakeProcessService { DriftBehavior = () => true };
        var engine = new OptimizationService(fake, Map) { DriftCheckInterval = TimeSpan.FromMinutes(5) };
        var profile = CoreProfile();
        var keys = new[] { SelfKey() };
        engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false);
        var second = engine.ApplyProfileToInstances(profile, SelfName(), keys, false, false);
        Assert.AreEqual(OperationStatus.SkippedAlreadyApplied, second.Results[0].Status);
        Assert.AreEqual(0, fake.DriftChecks, "No OS read inside the interval right after an apply.");
    }

    [TestMethod]
    public void ClearSuspensionsForProcess_LetsExplicitActionsThrough()
    {
        var fake = new FakeProcessService();
        var engine = new OptimizationService(fake, Map);
        var profile = CoreProfile();
        var keys = new[] { SelfKey() };
        engine.SuspendInstance(keys[0], TimeSpan.FromMinutes(5), "test");
        Assert.AreEqual(OperationStatus.SkippedSuspended, engine.ApplyProfileToInstances(profile, SelfName(), keys, false, true).Results[0].Status);

        engine.ClearSuspensionsForProcess(SelfName());
        Assert.AreEqual(OperationStatus.FullyApplied, engine.ApplyProfileToInstances(profile, SelfName(), keys, false, true).Results[0].Status);
    }

    [TestMethod]
    public void DetectDrift_BogusPid_IsUnknown()
    {
        var svc = new ProcessService();
        Assert.IsNull(svc.DetectDrift(-1, CoreProfile(), OptimizationMode.Affinity, "Normal", false, Map()));
        Assert.IsNull(svc.DetectDrift(int.MaxValue - 7, CoreProfile(), OptimizationMode.Affinity, "Normal", false, Map()));
    }

    [TestMethod]
    public void DetectDrift_SelfProcess_MatchesOwnAffinity()
    {
        var masks = ProcessService.TryGetAffinityMasks(Environment.ProcessId);
        Assert.IsNotNull(masks, "Own affinity must be readable with QUERY_LIMITED rights.");
        var profile = CoreProfile();
        profile.AffinityMask = unchecked((long)masks!.Value.Process);
        Assert.AreEqual(false, new ProcessService().DetectDrift(Environment.ProcessId, profile, OptimizationMode.Affinity, "Normal", false, new Dictionary<int, uint>()));
    }

    [TestMethod]
    public void TryGetProcessPath_ById_ReturnsOwnExecutable()
    {
        string? path = ProcessIdentityValidator.TryGetProcessPath(Environment.ProcessId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(path));
        Assert.AreEqual(Environment.ProcessPath, path, ignoreCase: true);
        Assert.IsNull(ProcessIdentityValidator.TryGetProcessPath(0));
    }
}
