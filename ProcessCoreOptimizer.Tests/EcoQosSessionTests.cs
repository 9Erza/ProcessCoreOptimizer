using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// P0-6: EcoQoS-only profiles must capture a baseline BEFORE mutation, restore
/// only verifiably captured state, and never confuse Unknown with Disabled.
/// All mutations target a sacrificial renamed copy of timeout.exe in %TEMP%.
/// </summary>
[TestClass]
public sealed class EcoQosSessionTests
{
    private string? _probePath;
    private Process? _probe;
    private string _probeName = string.Empty;

    [TestInitialize]
    public void SpawnProbe()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        _probeName = $"pco_ecoprobe_{tag}";
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
        Assert.IsFalse(_probe.HasExited);
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
    public void EcoOnlyProfile_CapturesBaselineBeforeMutation()
    {
        if (!ProcessService.IsEcoQosSupported())
            Assert.Inconclusive("EcoQoS API not supported on this OS build.");

        var probe = _probe!;
        var svc = new ProcessService();
        var key = new ProcessInstanceKey(probe.Id, probe.StartTime.ToUniversalTime());
        var restore = new SessionRestoreService();

        Assert.IsTrue(restore.TryCaptureBaseline(key, _probeName, ProcessIdentityValidator.TryGetProcessPath(probe), svc, CpuMap()));
        var entry = restore.ForProcess(_probeName);
        Assert.AreEqual(1, entry.Count);
        Assert.IsTrue(entry[0].EcoQosObserved.HasValue, "Readable EcoQoS must be captured even for eco-only flows.");
    }

    [TestMethod]
    public void EcoOnly_RoundTrip_OffOnRestoreOff()
    {
        if (!ProcessService.IsEcoQosSupported())
            Assert.Inconclusive("EcoQoS API not supported on this OS build.");

        var probe = _probe!;
        var svc = new ProcessService();
        var key = new ProcessInstanceKey(probe.Id, probe.StartTime.ToUniversalTime());
        var map = CpuMap();
        var restore = new SessionRestoreService();

        bool initial = svc.TryGetEcoQos(probe.Id).Enabled ?? false;

        var profile = new ProcessProfile
        {
            ProcessName = _probeName,
            ApplyCoreOptimization = false,
            ApplyPriority = false,
            EcoQos = !initial,
            IsEnabled = true
        };
        var engine = new OptimizationService(new ProcessService(), () => map, restore);
        var batch = engine.ApplyProfileToInstances(profile, _probeName, new[] { key }, allowRealtimePriority: false, force: true);

        Assert.AreEqual(1, batch.Total);
        var result = batch.Results[0];
        Assert.IsTrue(result.EcoQosApplied, $"EcoQoS apply failed: {result.Message}");
        Assert.AreEqual(OperationStatus.FullyApplied, result.Status);
        Assert.AreEqual(!initial, svc.TryGetEcoQos(probe.Id).Enabled);

        // Baseline was captured BEFORE the mutation: restore returns the original.
        var restored = restore.RestoreForProcess(_probeName, svc, map);
        Assert.IsTrue(restored.Restored == 1, restored.Message);
        Assert.AreEqual(initial, svc.TryGetEcoQos(probe.Id).Enabled, "Restore must return the pre-mutation EcoQoS state.");
    }

    [TestMethod]
    public void UnknownEcoQos_IsNeverRestoredAsDisabled()
    {
        if (!ProcessService.IsEcoQosSupported())
            Assert.Inconclusive("EcoQoS API not supported on this OS build.");

        var probe = _probe!;
        var svc = new ProcessService();
        var key = new ProcessInstanceKey(probe.Id, probe.StartTime.ToUniversalTime());
        var map = CpuMap();
        var restore = new SessionRestoreService();

        // Entry WITHOUT EcoQoS observation (priority-only baseline).
        var prio = svc.TryGetPriority(probe.Id);
        Assert.IsNotNull(prio);
        restore.Capture(key, _probeName, ProcessIdentityValidator.TryGetProcessPath(probe), null, prio);
        Assert.IsNull(restore.ForProcess(_probeName)[0].EcoQosObserved);

        // Flip EcoQoS live, then restore: EcoQoS must be untouched.
        var target = TargetFor(probe);
        bool before = svc.TryGetEcoQos(probe.Id).Enabled ?? false;
        var set = svc.SetEcoQos(probe.Id, !before, target);
        Assert.IsTrue(set.Success, set.Message);
        try
        {
            restore.RestoreForProcess(_probeName, svc, map);
            Assert.AreEqual(!before, svc.TryGetEcoQos(probe.Id).Enabled,
                "Restore without a captured EcoQoS baseline must leave EcoQoS alone.");
        }
        finally
        {
            svc.SetEcoQos(probe.Id, before, TargetFor(probe));
        }
    }
}
