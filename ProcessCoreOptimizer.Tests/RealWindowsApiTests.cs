using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Real Windows API validation on an isolated helper process owned by the test run.
/// The probe is a renamed copy of timeout.exe in %TEMP%: unique process name, safe
/// to re-pin and re-prioritize, always terminated and deleted in cleanup.
/// No system, service, user or agent process is ever touched.
/// </summary>
[TestClass]
public sealed class RealWindowsApiTests
{
    private string? _probePath;
    private Process? _probe;
    private string _probeName = string.Empty;

    [TestInitialize]
    public void SpawnProbe()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        _probeName = $"pco_probe_{tag}";
        _probePath = Path.Combine(Path.GetTempPath(), _probeName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "timeout.exe"), _probePath, overwrite: false);

        _probe = Process.Start(new ProcessStartInfo
        {
            FileName = _probePath,
            Arguments = "/t 120 /nobreak",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        Assert.IsNotNull(_probe);
        _probe.WaitForExit(100);
        Assert.IsFalse(_probe.HasExited, "Probe helper exited immediately.");
        System.Threading.Thread.Sleep(300);
    }

    [TestCleanup]
    public void KillProbe()
    {
        try
        {
            if (_probe is { HasExited: false })
            {
                _probe.Kill(entireProcessTree: true);
                _probe.WaitForExit(5000);
            }
        }
        catch { }
        try { _probe?.Dispose(); } catch { }
        try { if (_probePath != null && File.Exists(_probePath)) File.Delete(_probePath); } catch { }
    }

    private static Dictionary<int, uint> CpuMap() =>
        new HardwareService().GetLogicalCoreToCpuSetIdMap();

    private static ProcessMutationTarget TargetFor(Process proc)
    {
        Assert.IsTrue(ProcessMutationTarget.TryCreate(
            proc.Id, proc.StartTime.ToUniversalTime(), proc.ProcessName,
            ProcessIdentityValidator.TryGetProcessPath(proc), out var target));
        return target;
    }

    [TestMethod]
    public void RealApi_AffinityRoundTrip()
    {
        var svc = new ProcessService();
        var probe = _probe!;
        var target = TargetFor(probe);

        var before = svc.GetCurrentCoreSelection(probe.Id, CpuMap());
        Assert.IsTrue(before.Mask != 0, $"Pre-read failed: {before.Message}");

        long subset = 1L << 0;
        Assert.IsTrue(svc.SetProcessAffinity(probe.Id, subset, target), "Subset affinity must apply.");

        var readBack = svc.GetCurrentCoreSelection(probe.Id, CpuMap());
        long readMask = readBack.Success ? readBack.Mask : new ProcessService().GetCurrentCoreSelection(probe.Id, new Dictionary<int, uint>()).Mask;
        Assert.AreEqual(subset, readMask, "Read-back must equal the applied subset.");

        Assert.IsTrue(svc.SetProcessAffinity(probe.Id, before.Mask != 0 ? before.Mask : ProcessService.KnownProcessorMask(CpuMap()), TargetFor(probe)), "Restore must apply.");
        var restored = svc.GetCurrentCoreSelection(probe.Id, CpuMap());
        Assert.AreEqual(before.Mask, restored.Mask, "Affinity must be restored to the pre-test state.");
    }

    [TestMethod]
    public void RealApi_CpuSetsRoundTrip()
    {
        var svc = new ProcessService();
        var map = CpuMap();
        Assert.IsTrue(map.Count > 0, "CPU Set map is empty on this machine.");
        var probe = _probe!;
        var target = TargetFor(probe);

        long subset = 1L << map.Keys.Min();
        string applied = svc.ApplyCoreOptimization(probe.Id, subset, OptimizationMode.CpuSets, map, target);
        Assert.IsTrue(applied.StartsWith("OK", StringComparison.OrdinalIgnoreCase), $"CPU Sets write failed: {applied} (Win32 codes flow through the message)");

        var read = svc.GetCurrentCoreSelection(probe.Id, map);
        Assert.IsTrue(read.Success && read.Source == "CpuSets", $"Expected CpuSets source, got {read.Source}: {read.Message}");
        Assert.AreEqual(subset, read.Mask);

        Assert.IsTrue(svc.ClearProcessCpuSets(probe.Id, TargetFor(probe)), "Clearing explicit sets must succeed.");
    }

    [TestMethod]
    public void RealApi_AffinityAndCpuSets_Coexist()
    {
        var svc = new ProcessService();
        var map = CpuMap();
        var probe = _probe!;
        var target = TargetFor(probe);

        long full = ProcessService.KnownProcessorMask(map);
        string aff = svc.ApplyCoreOptimization(probe.Id, full, OptimizationMode.Affinity, map, target);
        Assert.IsTrue(aff.StartsWith("OK", StringComparison.OrdinalIgnoreCase), $"Affinity write failed: {aff}");

        long subset = 1L << map.Keys.Min();
        string sets = svc.ApplyCoreOptimization(probe.Id, subset, OptimizationMode.CpuSets, map, TargetFor(probe));
        Assert.IsTrue(sets.StartsWith("OK", StringComparison.OrdinalIgnoreCase), $"CPU Sets write failed: {sets}");
    }

    [TestMethod]
    public void RealApi_PriorityRoundTrip()
    {
        var svc = new ProcessService();
        var probe = _probe!;

        Assert.IsTrue(svc.SetPriority(probe.Id, ProcessPriorityClass.BelowNormal, TargetFor(probe)));
        Assert.AreEqual(ProcessPriorityClass.BelowNormal, svc.TryGetPriority(probe.Id));

        Assert.IsTrue(svc.SetPriority(probe.Id, ProcessPriorityClass.Normal, TargetFor(probe)));
        Assert.AreEqual(ProcessPriorityClass.Normal, svc.TryGetPriority(probe.Id));
    }

    [TestMethod]
    public void RealApi_ExitedProcess_IsRefused()
    {
        var svc = new ProcessService();
        var probe = _probe!;
        int pid = probe.Id;
        var staleTarget = new ProcessMutationTarget(pid, probe.StartTime.ToUniversalTime(), probe.ProcessName, null);
        probe.Kill(entireProcessTree: true);
        probe.WaitForExit(5000);

        Assert.IsFalse(svc.SetPriority(pid, ProcessPriorityClass.BelowNormal, staleTarget));
        string core = svc.ApplyCoreOptimization(pid, 0x1, OptimizationMode.Affinity, CpuMap(), staleTarget);
        Assert.IsTrue(core.StartsWith("ERR", StringComparison.OrdinalIgnoreCase), $"Exited process must yield ERR, got: {core}");
    }

    [TestMethod]
    public void RealApi_WatcherApplies_PathBound_ThenRestore()
    {
        var svc = new ProcessService();
        var map = CpuMap();
        var restore = new SessionRestoreService();
        var engine = new OptimizationService(svc, () => map, restore);
        var probe = _probe!;
        var key = new ProcessInstanceKey(probe.Id, probe.StartTime.ToUniversalTime());

        string? livePath = ProcessIdentityValidator.TryGetProcessPath(probe);
        Assert.IsNotNull(livePath);

        var profile = new ProcessProfile
        {
            ProcessName = _probeName,
            ExecutablePath = livePath,
            AffinityMask = 1L << map.Keys.Min(),
            Priority = "BelowNormal",
            OptimizationMode = OptimizationMode.Affinity,
            ApplyCoreOptimization = true,
            ApplyPriority = true,
            IsEnabled = true
        };
        var snapshot = new ProcessGroupSnapshot
        {
            Name = _probeName,
            FirstProcessId = key.ProcessId,
            Instances = new List<ProcessInstanceKey> { key },
            ExecutablePath = livePath
        };

        var batch = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, allowRealtimePriority: false, force: true);
        Assert.AreEqual(1, batch.Total);
        var result = batch.Results[0];
        Assert.AreEqual(OperationStatus.FullyApplied, result.Status, $"Engine message: {result.Message}, Win32={result.Win32Error}");
        Assert.IsTrue(restore.HasBaseline(key), "Baseline must be captured before the first real mutation.");

        var restored = restore.RestoreForProcess(_probeName, svc, map);
        Assert.AreEqual(1, restored.Restored, restored.Message);
        Assert.AreEqual(ProcessPriorityClass.Normal, svc.TryGetPriority(probe.Id), "Priority must be back to pre-test Normal.");

        engine.SuspendLiveInstances(_probeName, TimeSpan.FromMinutes(5), "test restore");
        var replay = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, true);
        Assert.AreEqual(OperationStatus.SkippedSuspended, replay.Results[0].Status, "Watcher must respect post-restore suspension.");
    }

    [TestMethod]
    public void RealApi_PathBoundMismatch_NeverApplies()
    {
        var svc = new ProcessService();
        var map = CpuMap();
        var engine = new OptimizationService(svc, () => map);
        var probe = _probe!;
        var key = new ProcessInstanceKey(probe.Id, probe.StartTime.ToUniversalTime());

        var profile = new ProcessProfile
        {
            ProcessName = _probeName,
            ExecutablePath = @"C:\definitely\elsewhere\other.exe",
            AffinityMask = 0x1,
            Priority = "BelowNormal",
            ApplyCoreOptimization = true,
            ApplyPriority = true,
            IsEnabled = true
        };
        var snapshot = new ProcessGroupSnapshot
        {
            Name = _probeName,
            FirstProcessId = key.ProcessId,
            Instances = new List<ProcessInstanceKey> { key }
        };

        var batch = engine.ApplyProfilesForSnapshots(new[] { profile }, new[] { snapshot }, false, true);
        Assert.AreEqual(0, batch.Total, "Path-bound mismatch must apply to nothing.");
        Assert.AreEqual(ProcessPriorityClass.Normal, svc.TryGetPriority(probe.Id), "Untouched process keeps its priority.");
    }
}
