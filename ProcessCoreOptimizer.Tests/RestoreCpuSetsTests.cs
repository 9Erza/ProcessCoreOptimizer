using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class RestoreCpuSetsTests
{
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

    private static SessionRestoreService Seeded(Action<SessionRestoreService, ProcessInstanceKey> seed)
    {
        var svc = new SessionRestoreService();
        seed(svc, SelfKey());
        return svc;
    }

    [TestMethod]
    public void Restore_FullBaseline_RestoresAffinityCpuSetsPriority_InOrder()
    {
        var order = new List<string>();
        var fake = new FakeProcessService();
        fake.AffinityBehavior = (pid, mask) => { order.Add("affinity"); return true; };
        fake.CoreBehavior = (pid, mask, mode, map) => { order.Add(mode == OptimizationMode.CpuSets ? "cpusets" : "core"); return "OK_CPUSETS_2"; };
        fake.PriorityBehavior = (pid, p) => { order.Add("priority"); return true; };

        var svc = Seeded((s, key) => s.Capture(SelfKey(), SelfName(), null, 0x3, ProcessPriorityClass.Normal));
        // Simulate a CPU Sets baseline by re-seeding is not possible (first wins);
        // direct entry with sets:
        var svc2 = new SessionRestoreService();
        var field = typeof(SessionRestoreService).GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (System.Collections.IList)field.GetValue(svc2)!;
        list.Add(new SessionRestoreEntry
        {
            Key = SelfKey(),
            ProcessName = SelfName(),
            AffinityMask = 0x3,
            CpuSetsObserved = true,
            CpuSetMask = 0x3,
            Priority = ProcessPriorityClass.Normal,
            CapturedAtUtc = DateTime.UtcNow
        });

        var result = svc2.RestoreForProcess(SelfName(), fake, new Dictionary<int, uint> { [0] = 10, [1] = 11 });
        Assert.AreEqual(1, result.Restored);
        Assert.AreEqual(1, result.AffinityRestored);
        Assert.AreEqual(1, result.CpuSetsRestored);
        Assert.AreEqual(1, result.PriorityRestored);
        Assert.AreEqual(OptimizationMode.CpuSets, fake.LastCoreMode, "Captured sets must be restored via the CPU Sets path.");
        CollectionAssert.AreEqual(new[] { "affinity", "cpusets", "priority" }, order, "Restore order: affinity, CPU Sets, priority.");
    }

    [TestMethod]
    public void Restore_NoExplicitSetsBaseline_ClearsCurrentSets()
    {
        var fake = new FakeProcessService();
        var svc = new SessionRestoreService();
        var field = typeof(SessionRestoreService).GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((System.Collections.IList)field.GetValue(svc)!).Add(new SessionRestoreEntry
        {
            Key = SelfKey(),
            ProcessName = SelfName(),
            AffinityMask = 0x1,
            CpuSetsObserved = true,
            CpuSetMask = null, // baseline explicitly had no sets
            Priority = null,
            CapturedAtUtc = DateTime.UtcNow
        });

        var result = svc.RestoreForProcess(SelfName(), fake, new Dictionary<int, uint> { [0] = 10 });
        Assert.AreEqual(1, result.Restored);
        Assert.AreEqual(1, fake.ClearCpuSetsCalls, "Baseline without sets must clear current sets, not leave them.");
        Assert.AreEqual(0, fake.CoreCalls);
    }

    [TestMethod]
    public void Restore_UnobservedSets_LeftUntouched()
    {
        var fake = new FakeProcessService();
        var svc = Seeded((s, key) => s.Capture(SelfKey(), SelfName(), null, 0x1, ProcessPriorityClass.Normal));
        var result = svc.RestoreForProcess(SelfName(), fake, new Dictionary<int, uint>());
        Assert.AreEqual(0, fake.ClearCpuSetsCalls, "Unknown sets state must never be guessed or cleared.");
        Assert.AreEqual(0, fake.CoreCalls);
        Assert.AreEqual(1, result.Restored);
    }

    [TestMethod]
    public void Restore_PartialFailure_ReportedHonestly()
    {
        var fake = new FakeProcessService();
        fake.AffinityBehavior = (pid, mask) => false;
        fake.PriorityBehavior = (pid, p) => true;
        var svc = Seeded((s, key) => s.Capture(SelfKey(), SelfName(), null, 0x1, ProcessPriorityClass.Normal));

        var result = svc.RestoreForProcess(SelfName(), fake, new Dictionary<int, uint>());
        Assert.AreEqual(0, result.Restored);
        Assert.AreEqual(1, result.PartiallyRestored);
        Assert.AreEqual(0, result.Failed);
        Assert.AreEqual(0, result.AffinityRestored);
        Assert.AreEqual(1, result.PriorityRestored);
        StringAssert.Contains(result.Message, "partial");
    }

    [TestMethod]
    public void Capture_FirstBaselineWins_NeverOverwritten()
    {
        var svc = new SessionRestoreService();
        var key = SelfKey();
        svc.Capture(key, SelfName(), null, 0x1, ProcessPriorityClass.Normal);
        svc.Capture(key, SelfName(), null, 0x7, ProcessPriorityClass.High);
        var entry = svc.ForProcess(SelfName()).First();
        Assert.AreEqual(0x1, entry.AffinityMask, "Original baseline must survive later captures.");
        Assert.AreEqual(ProcessPriorityClass.Normal, entry.Priority);
    }

    [TestMethod]
    public void Engine_SuspendedInstance_SkippedWithoutOsTouch()
    {
        var fake = new FakeProcessService();
        var engine = new OptimizationService(fake, () => new Dictionary<int, uint> { [0] = 100 });
        var key = SelfKey();
        engine.SuspendInstance(key, TimeSpan.FromMinutes(5), "test");

        var profile = new ProcessProfile
        {
            ProcessName = SelfName(),
            AffinityMask = 0x1,
            Priority = "Normal",
            ApplyCoreOptimization = true,
            ApplyPriority = true,
            IsEnabled = true
        };
        var snapshot = new ProcessGroupSnapshot
        {
            Name = SelfName(),
            FirstProcessId = key.ProcessId,
            Instances = new List<ProcessInstanceKey> { key }
        };

        var batch = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, true);
        Assert.AreEqual(OperationStatus.SkippedSuspended, batch.Results[0].Status);
        Assert.AreEqual("SKIPPED_SUSPENDED: test", batch.Results[0].Message);
        Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
        Assert.AreEqual(1, engine.SuspendedCount);
    }

    [TestMethod]
    public void Engine_ExpiredSuspension_Resumes()
    {
        var fake = new FakeProcessService();
        var engine = new OptimizationService(fake, () => new Dictionary<int, uint> { [0] = 100 });
        var key = SelfKey();
        engine.SuspendInstance(key, TimeSpan.Zero, "test");
        Assert.AreEqual(0, engine.SuspendedCount, "Expired suspensions prune on read.");
    }

    [TestMethod]
    public void Engine_SuspendLiveInstances_UnknownName_NoThrow()
    {
        var fake = new FakeProcessService();
        var engine = new OptimizationService(fake, () => new Dictionary<int, uint>());
        engine.SuspendLiveInstances("definitely-not-running-xyz", TimeSpan.FromMinutes(5), "test");
        Assert.AreEqual(0, engine.SuspendedCount);
        Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
    }
}
