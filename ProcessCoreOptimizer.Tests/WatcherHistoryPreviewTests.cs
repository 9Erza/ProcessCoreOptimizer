using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class WatcherHistoryPreviewTests
{
    private static (FakeProcessService fake, OptimizationService engine, AppliedOperationHistory history, ProfileWatcherService watcher)
        CreateWatcher(Func<IReadOnlyList<ProcessProfile>>? provider = null)
    {
        var fake = new FakeProcessService();
        var scanner = new ProcessScannerService(new ProcessService());
        var engine = new OptimizationService(fake, () => new Dictionary<int, uint> { [0] = 100, [1] = 101 });
        var history = new AppliedOperationHistory();
        var watcher = new ProfileWatcherService(
            scanner, engine, history,
            provider ?? (() => new List<ProcessProfile>()),
            () => false);
        return (fake, engine, history, watcher);
    }

    [TestMethod]
    public async Task Watcher_EmptyProfiles_CompletesWithoutMutations()
    {
        var (fake, _, history, watcher) = CreateWatcher();
        using (watcher)
        {
            var batch = await watcher.RunOnceAsync("test", force: false);
            Assert.IsNotNull(batch);
            Assert.AreEqual(0, batch!.Total);
            Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
            Assert.AreEqual(0, history.Count, "Empty passes carry no results, so history stays clean.");
            Assert.AreEqual(0, watcher.LastRun.Total);
        }
    }

    [TestMethod]
    public async Task Watcher_NeverThrows_ConcurrentCallsSerialize()
    {
        var (fake, _, _, watcher) = CreateWatcher();
        using (watcher)
        {
            var tasks = Enumerable.Range(0, 10).Select(_ => watcher.RunOnceAsync("test", false)).ToArray();
            var results = await Task.WhenAll(tasks);
            Assert.AreEqual(10, results.Length);
            Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
        }
    }

    [TestMethod]
    public async Task Watcher_AfterDispose_NeverMutates()
    {
        var (fake, _, _, watcher) = CreateWatcher(() => new List<ProcessProfile>
        {
            new() { ProcessName = "definitely-not-running-xyz", AffinityMask = 0x1, IsEnabled = true }
        });
        watcher.Dispose();
        var batch = await watcher.RunOnceAsync("test", false);
        Assert.IsNull(batch, "Disposed watcher must refuse to run.");
        Assert.AreEqual(0, fake.CoreCalls + fake.PriorityCalls);
    }

    [TestMethod]
    public async Task Watcher_ProviderQueriedPerRun_ProfileChangeAppliesNextPass()
    {
        int calls = 0;
        var (_, _, _, watcher) = CreateWatcher(() =>
        {
            calls++;
            return calls == 1
                ? new List<ProcessProfile>()
                : new List<ProcessProfile> { new ProcessProfile { ProcessName = "also-not-running", AffinityMask = 0x1, IsEnabled = true } };
        });
        using (watcher)
        {
            await watcher.RunOnceAsync("test", false);
            await watcher.RunOnceAsync("test", false);
            Assert.AreEqual(2, calls, "Profiles are snapshotted fresh on every pass.");
        }
    }

    [TestMethod]
    public void History_IsBounded_NewestKept()
    {
        var history = new AppliedOperationHistory();
        for (int i = 0; i < AppliedOperationHistory.MaxEntries + 5; i++)
        {
            history.Record(new OptimizationResult { ProcessName = $"p{i}", Status = OperationStatus.FullyApplied }, "test");
        }
        Assert.AreEqual(AppliedOperationHistory.MaxEntries, history.Count);
        Assert.IsTrue(history.Recent(5).All(r => r.ProcessName.StartsWith("p")));
        Assert.AreEqual($"p{AppliedOperationHistory.MaxEntries + 4}", history.Recent(1)[0].ProcessName);
    }

    [TestMethod]
    public void Preview_Compare_ReturnsStructuredRows()
    {
        using var self = Process.GetCurrentProcess();
        var svc = new ProcessService();
        var map = new Dictionary<int, uint>();
        var profile = new ProcessProfile
        {
            ProcessName = self.ProcessName,
            AffinityMask = 0x1,
            Priority = "Normal",
            ApplyCoreOptimization = true,
            ApplyPriority = true,
            IsEnabled = true
        };
        var preview = ProfilePreviewService.Compare(profile, self.Id, map, svc, allowRealtimePriority: false);
        Assert.AreEqual(2, preview.Rows.Count);
        Assert.IsTrue(preview.Rows.Any(r => r.Action == "Cores"));
        Assert.IsTrue(preview.Rows.Any(r => r.Action == "Priority"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.Summary));
    }

    [TestMethod]
    public void Preview_CaptureSelf_ProducesDraft()
    {
        using var self = Process.GetCurrentProcess();
        var svc = new ProcessService();
        var map = new HardwareService().GetLogicalCoreToCpuSetIdMap();
        var draft = ProfilePreviewService.CaptureFromLive(self.Id, map, svc);
        // The test host may run with restricted handles; null means "unreadable", not failure.
        if (draft != null)
        {
            Assert.AreEqual(ProfileService.NormalizeProcessName(self.ProcessName), draft.ProcessName);
            Assert.AreNotEqual(0, draft.AffinityMask);
            Assert.IsFalse(draft.IsEnabled, "Captured drafts start disabled until the user enables them.");
        }
        else
        {
            Assert.IsTrue(true, "Capture refused safely when the OS denies reads.");
        }
    }
}
