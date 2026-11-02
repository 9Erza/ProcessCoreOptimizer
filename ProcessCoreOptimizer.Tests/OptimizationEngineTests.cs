using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

internal sealed class FakeProcessService : ProcessService
{
    public int CoreCalls;
    public int PriorityCalls;
    public Func<int, long, OptimizationMode, Dictionary<int, uint>, string>? CoreBehavior;
    public Func<int, ProcessPriorityClass, bool>? PriorityBehavior;

    public override string ApplyCoreOptimization(
        int pid, long mask, OptimizationMode mode, Dictionary<int, uint> cpuSetMap,
        ProcessInstanceKey? expected, string? expectedName, string? expectedPath)
    {
        CoreCalls++;
        if (mask == 0) return "ERR_EMPTY_MASK";
        if (CoreBehavior != null) return CoreBehavior(pid, mask, mode, cpuSetMap);
        return "OK_AFFINITY";
    }

    public override bool SetPriority(
        int pid, ProcessPriorityClass priority,
        ProcessInstanceKey? expected, string? expectedName, string? expectedPath)
    {
        PriorityCalls++;
        if (PriorityBehavior != null) return PriorityBehavior(pid, priority);
        return true;
    }
}

[TestClass]
public sealed class ProfileSanitizeTests
{
    private static ProcessProfile Make(string name, long mask, bool applyP, bool applyC, string? path = null) => new()
    {
        ProcessName = name,
        AffinityMask = mask,
        ApplyPriority = applyP,
        ApplyCoreOptimization = applyC,
        ExecutablePath = path,
        Priority = "Normal",
        IsEnabled = true
    };

    [TestMethod]
    public void PriorityOnlyProfile_WithZeroMask_IsKept()
    {
        var profiles = new[] { Make("notepad", 0, applyP: true, applyC: false) };
        var sanitized = ProfileService.SanitizeProfiles(profiles).ToList();
        Assert.AreEqual(1, sanitized.Count, "Priority-only profiles must survive sanitization (P0 regression 6.1).");
    }

    [TestMethod]
    public void CoreProfile_WithZeroMask_IsDropped()
    {
        var profiles = new[] { Make("notepad", 0, applyP: false, applyC: true) };
        var sanitized = ProfileService.SanitizeProfiles(profiles).ToList();
        Assert.AreEqual(0, sanitized.Count, "Core-bound profiles with mask 0 do nothing and must be dropped.");
    }

    [TestMethod]
    public void AffinityOnlyProfile_IsKept()
    {
        var profiles = new[] { Make("notepad", 0x3, applyP: false, applyC: true) };
        Assert.AreEqual(1, ProfileService.SanitizeProfiles(profiles).Count());
    }

    [TestMethod]
    public void NoopProfile_BothFlagsFalse_IsDropped()
    {
        var profiles = new[] { Make("notepad", 0x3, applyP: false, applyC: false) };
        Assert.AreEqual(0, ProfileService.SanitizeProfiles(profiles).Count());
    }

    [TestMethod]
    public void ExeSuffix_IsNormalized()
    {
        var profiles = new[] { Make("notepad.exe", 0x1, true, false) };
        var sanitized = ProfileService.SanitizeProfiles(profiles).ToList();
        Assert.AreEqual("notepad", sanitized[0].ProcessName);
    }

#pragma warning disable CS0618
    [TestMethod]
    public void LegacyExclusive_IsMigratedToAffinity()
    {
        var p = Make("game", 0x1, true, true);
        p.OptimizationMode = OptimizationMode.Exclusive;
        var sanitized = ProfileService.SanitizeProfiles(new[] { p }).ToList();
        Assert.AreEqual(OptimizationMode.Affinity, sanitized[0].OptimizationMode);
    }
#pragma warning restore CS0618

    [TestMethod]
    public void UnknownPriority_IsNormalizedToNormal()
    {
        var p = Make("game", 0x1, true, true);
        p.Priority = "UltraBoost!!!";
        var sanitized = ProfileService.SanitizeProfiles(new[] { p }).ToList();
        Assert.AreEqual("Normal", sanitized[0].Priority);
    }

    [TestMethod]
    public void SameNameDifferentPath_BothKept()
    {
        var a = Make("app", 0x1, true, true, @"C:\A\app.exe");
        var b = Make("app", 0x2, true, true, @"C:\B\app.exe");
        var sanitized = ProfileService.SanitizeProfiles(new[] { a, b }).ToList();
        Assert.AreEqual(2, sanitized.Count, "Path-bound variants must not collapse to one profile.");
    }
}

[TestClass]
public sealed class OptimizationCacheTests
{
    private static Dictionary<int, uint> CpuMap() => new() { [0] = 100, [1] = 101 };

    private static (FakeProcessService fake, OptimizationService engine) CreateEngine()
    {
        var fake = new FakeProcessService();
        var engine = new OptimizationService(fake, CpuMap);
        return (fake, engine);
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

    private static ProcessProfile SelfProfile(long mask = 0x1) => new()
    {
        ProcessName = SelfName(),
        AffinityMask = mask,
        Priority = "Normal",
        OptimizationMode = OptimizationMode.Affinity,
        ApplyPriority = true,
        ApplyCoreOptimization = true,
        IsEnabled = true
    };

    private static ProcessGroupSnapshot SelfSnapshot(ProcessInstanceKey key, string name) => new()
    {
        Name = name,
        FirstProcessId = key.ProcessId,
        InstanceCount = 1,
        Instances = new List<ProcessInstanceKey> { key }
    };

    [TestMethod]
    public void PartialFailure_IsNotCached_RetrySucceeds()
    {
        var (fake, engine) = CreateEngine();
        var key = SelfKey();
        var profile = SelfProfile();
        var snapshot = SelfSnapshot(key, SelfName());

        // First tick: core fails, priority succeeds -> partial success, must NOT cache.
        fake.CoreBehavior = (_, _, _, _) => "ERR_CPUSETS_5";
        var batch1 = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, allowRealtimePriority: false, force: false);
        Assert.AreEqual(1, batch1.Total);
        var r1 = batch1.Results[0];
        Assert.IsTrue(r1.Success, "Partial success must report Success=true with split flags.");
        Assert.IsFalse(r1.CoreApplied);
        Assert.IsTrue(r1.PriorityApplied);

        // Second tick with same signature must retry the core half (not SKIPPED_ALREADY_APPLIED).
        fake.CoreBehavior = (_, _, _, _) => "OK_AFFINITY";
        var batch2 = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, allowRealtimePriority: false, force: false);
        Assert.AreEqual(1, batch2.Total);
        Assert.AreNotEqual("SKIPPED_ALREADY_APPLIED", batch2.Results[0].Message);
        Assert.IsTrue(batch2.Results[0].CoreApplied && batch2.Results[0].PriorityApplied);
    }

    [TestMethod]
    public void FullSuccess_IsCached_SecondTickSkipped()
    {
        var (fake, engine) = CreateEngine();
        var key = SelfKey();
        var profile = SelfProfile();
        var snapshot = SelfSnapshot(key, SelfName());

        var b1 = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        Assert.IsTrue(b1.Results[0].Success);
        int callsAfterFirst = fake.CoreCalls + fake.PriorityCalls;

        var b2 = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        Assert.AreEqual("SKIPPED_ALREADY_APPLIED", b2.Results[0].Message);
        Assert.AreEqual(callsAfterFirst, fake.CoreCalls + fake.PriorityCalls, "Cached profile must not touch the OS again.");
    }

    [TestMethod]
    public void ProfileChange_InvalidatesCache()
    {
        var (fake, engine) = CreateEngine();
        var key = SelfKey();
        var profile = SelfProfile(mask: 0x1);
        var snapshot = SelfSnapshot(key, SelfName());

        engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        profile.AffinityMask = 0x3;
        profile.UpdatedAt = DateTime.UtcNow.AddSeconds(1);
        var b2 = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        Assert.AreNotEqual("SKIPPED_ALREADY_APPLIED", b2.Results[0].Message);
    }

    [TestMethod]
    public void FailedForcedApplication_InvalidatesPreviousSignature()
    {
        var (fake, engine) = CreateEngine();
        var key = SelfKey();
        var profile = SelfProfile();
        var snapshot = SelfSnapshot(key, SelfName());

        engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        fake.CoreBehavior = (_, _, _, _) => "ERR_CPUSETS_5";
        fake.PriorityBehavior = (_, _) => false;
        var forced = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, true);
        Assert.IsFalse(forced.Results[0].Success);

        // Cache must be gone: next normal tick retries instead of skipping.
        fake.CoreBehavior = (_, _, _, _) => "OK_AFFINITY";
        fake.PriorityBehavior = (_, _) => true;
        var retry = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        Assert.AreNotEqual("SKIPPED_ALREADY_APPLIED", retry.Results[0].Message);
    }

    [TestMethod]
    public void ProtectedProcess_IsSkipped_WithoutTouchingOs()
    {
        var (fake, engine) = CreateEngine();
        var profile = new ProcessProfile
        {
            ProcessName = "svchost",
            AffinityMask = 0x1,
            Priority = "Normal",
            ApplyCoreOptimization = true,
            ApplyPriority = true,
            IsEnabled = true
        };
        var snapshot = new ProcessGroupSnapshot
        {
            Name = "svchost",
            FirstProcessId = 4,
            Instances = new List<ProcessInstanceKey> { new(4, DateTime.UtcNow) }
        };
        var batch = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, true);
        Assert.AreEqual(1, batch.Total);
        Assert.AreEqual("SKIPPED_PROTECTED", batch.Results[0].Message);
        Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
    }

    [TestMethod]
    public void StalePid_IsSkipped_WithoutTouchingOs()
    {
        var (fake, engine) = CreateEngine();
        using var self = Process.GetCurrentProcess();
        var staleKey = new ProcessInstanceKey(self.Id, self.StartTime.ToUniversalTime().AddMinutes(-5));
        var profile = SelfProfile();
        var snapshot = SelfSnapshot(staleKey, SelfName());
        var batch = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, true);
        // Identity mismatch tries the next path-bound profile; with a single profile
        // nothing is applied and the OS is never touched.
        Assert.AreEqual(0, batch.Total);
        Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
    }

    [TestMethod]
    public void CleanupStaleCache_RemovesDeadInstances()
    {
        var (_, engine) = CreateEngine();
        var key = SelfKey();
        var profile = SelfProfile();
        var snapshot = SelfSnapshot(key, SelfName());
        engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        engine.CleanupStaleCache(new HashSet<ProcessInstanceKey>());

        var batch = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, false);
        Assert.AreNotEqual("SKIPPED_ALREADY_APPLIED", batch.Results[0].Message);
    }

    [TestMethod]
    public void RequiresAdmin_OnlyOnRealErrors()
    {
        Assert.IsFalse(ProcessService.RequiresAdminForMessage("OK_CPUSETS_5"),
            "Successful CPU Sets result must not be flagged as RequiresAdmin (P1 false-positive).");
        Assert.IsFalse(ProcessService.RequiresAdminForMessage("OK_AFFINITY"));
        Assert.IsTrue(ProcessService.RequiresAdminForMessage("ERR_OPEN_PROCESS_5"));
        Assert.IsTrue(ProcessService.RequiresAdminForMessage("ERR_CPUSETS_5"));
    }

    [TestMethod]
    public void ConvertMaskToCpuSetIds_MapsOnlyKnownBits()
    {
        var map = new Dictionary<int, uint> { [0] = 10, [1] = 11, [3] = 13 };
        uint[] ids = ProcessService.ConvertMaskToCpuSetIds(0b1011, map);
        CollectionAssert.AreEquivalent(new[] { 10u, 11u, 13u }, ids);
        Assert.AreEqual(0, ProcessService.ConvertMaskToCpuSetIds(0b100, map).Length, "Bit 2 has no CPU Set and must yield no IDs.");
        Assert.AreEqual(0, ProcessService.ConvertMaskToCpuSetIds(0, map).Length);
    }

    [TestMethod]
    public void EmptyMask_ReturnsEmptyError()
    {
        var (fake, engine) = CreateEngine();
        var key = SelfKey();
        var profile = SelfProfile(mask: 0);
        profile.ApplyPriority = false;
        var snapshot = SelfSnapshot(key, SelfName());
        var batch = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, true);
        StringAssert.Contains(batch.Results[0].Message, "ERR_EMPTY_MASK");
    }
}
