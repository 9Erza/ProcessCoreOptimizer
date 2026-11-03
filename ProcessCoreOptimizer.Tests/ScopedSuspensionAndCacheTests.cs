using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// P1-1: suspension and FullyApplied-cache invalidation must scope to the single
/// instance a rule acted on — same-name siblings keep flowing. Name-wide
/// suspend/restore stays available as a separate conscious group action.
/// Two live instances share one probe name; all mutations are priority-only
/// on sacrificial timeout.exe copies, killed and deleted in cleanup.
/// </summary>
[TestClass]
public sealed class ScopedSuspensionAndCacheTests
{
    private readonly List<string> _probePaths = new();
    private readonly List<Process> _probes = new();
    private string _probeName = string.Empty;

    [TestInitialize]
    public void SpawnProbes()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        _probeName = $"pco_scope_{tag}";
        string path = Path.Combine(Path.GetTempPath(), _probeName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "timeout.exe"), path, overwrite: false);
        _probePaths.Add(path);

        for (int i = 0; i < 2; i++)
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "/t 120 /nobreak",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            Assert.IsNotNull(proc);
            proc.WaitForExit(100);
            Assert.IsFalse(proc.HasExited);
            _probes.Add(proc);
        }

        Thread.Sleep(300);
    }

    [TestCleanup]
    public void KillProbes()
    {
        foreach (var proc in _probes)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(5000);
                }
            }
            catch { }
            try { proc.Dispose(); } catch { }
        }

        _probes.Clear();
        foreach (var path in _probePaths)
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        _probePaths.Clear();
    }

    private static ProcessInstanceKey KeyOf(Process proc) =>
        new(proc.Id, proc.StartTime.ToUniversalTime());

    private static ProcessProfile PriorityProfile(string name) =>
        new()
        {
            ProcessName = name,
            ApplyCoreOptimization = false,
            ApplyPriority = true,
            Priority = "BelowNormal",
            IsEnabled = true
        };

    private static OptimizationService Engine() =>
        new(new ProcessService(), () => new Dictionary<int, uint>(), restoreService: null);

    [TestMethod]
    public void SuspendInstance_ScopesToSingleKey()
    {
        var engine = Engine();
        var keyA = KeyOf(_probes[0]);
        var keyB = KeyOf(_probes[1]);
        var profile = PriorityProfile(_probeName);

        engine.SuspendInstance(keyA, TimeSpan.FromMinutes(5), "test hold");

        var skipped = engine.ApplyProfileToInstances(profile, _probeName, new[] { keyA }, allowRealtimePriority: false, force: true);
        Assert.AreEqual(OperationStatus.SkippedSuspended, skipped.Results[0].Status);

        var sibling = engine.ApplyProfileToInstances(profile, _probeName, new[] { keyB }, allowRealtimePriority: false, force: true);
        Assert.AreEqual(OperationStatus.FullyApplied, sibling.Results[0].Status,
            "Suspending one instance must not hold its same-name sibling.");
    }

    [TestMethod]
    public void SuspensionExpiresOnItsOwn()
    {
        var engine = Engine();
        var keyA = KeyOf(_probes[0]);
        var profile = PriorityProfile(_probeName);

        engine.SuspendInstance(keyA, TimeSpan.FromMilliseconds(200), "test hold");
        Assert.AreEqual(1, engine.SuspendedCount);
        Thread.Sleep(600);

        var after = engine.ApplyProfileToInstances(profile, _probeName, new[] { keyA }, allowRealtimePriority: false, force: true);
        Assert.AreNotEqual(OperationStatus.SkippedSuspended, after.Results[0].Status,
            "Expired suspension must not block the instance.");
    }

    [TestMethod]
    public void ClearProfileCacheForKey_ScopesToSingleKey()
    {
        var engine = Engine();
        var keyA = KeyOf(_probes[0]);
        var keyB = KeyOf(_probes[1]);
        var profile = PriorityProfile(_probeName);

        Assert.AreEqual(OperationStatus.FullyApplied,
            engine.ApplyProfileToInstances(profile, _probeName, new[] { keyA }, allowRealtimePriority: false, force: true).Results[0].Status);
        Assert.AreEqual(OperationStatus.FullyApplied,
            engine.ApplyProfileToInstances(profile, _probeName, new[] { keyB }, allowRealtimePriority: false, force: true).Results[0].Status);

        Assert.AreEqual(OperationStatus.SkippedAlreadyApplied,
            engine.ApplyProfileToInstances(profile, _probeName, new[] { keyA }, allowRealtimePriority: false, force: false).Results[0].Status);

        engine.ClearProfileCacheForKey(keyA);

        Assert.AreEqual(OperationStatus.FullyApplied,
            engine.ApplyProfileToInstances(profile, _probeName, new[] { keyA }, allowRealtimePriority: false, force: false).Results[0].Status,
            "Cleared key must re-evaluate.");
        Assert.AreEqual(OperationStatus.SkippedAlreadyApplied,
            engine.ApplyProfileToInstances(profile, _probeName, new[] { keyB }, allowRealtimePriority: false, force: false).Results[0].Status,
            "Sibling cache entry must survive scoped invalidation.");
    }

    [TestMethod]
    public void SuspendLiveInstances_RemainsNameWideGroupAction()
    {
        var engine = Engine();
        var profile = PriorityProfile(_probeName);

        engine.SuspendLiveInstances(_probeName, TimeSpan.FromMinutes(5), "group hold");
        Assert.AreEqual(2, engine.SuspendedCount);

        foreach (var proc in _probes)
        {
            var r = engine.ApplyProfileToInstances(profile, _probeName, new[] { KeyOf(proc) }, allowRealtimePriority: false, force: true);
            Assert.AreEqual(OperationStatus.SkippedSuspended, r.Results[0].Status);
        }
    }
}
