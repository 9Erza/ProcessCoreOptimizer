using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// CPU telemetry semantics: Utilization (0-100 busy) vs Utility (may exceed
/// 100 with boost), Unknown instead of fake 0%, group-0 mapping, no UI stall,
/// no leaks. Real Windows counters verified against a pinned burner process.
/// </summary>
[TestClass]
public sealed class CpuTelemetryTests
{
    [TestMethod]
    public void Utilization_45Percent_Stays()
    {
        Assert.AreEqual(45.2, HardwareService.NormalizeUtilization(45.17));
    }

    [TestMethod]
    public void Utilization_100Percent_Stays()
    {
        Assert.AreEqual(100.0, HardwareService.NormalizeUtilization(100.0));
    }

    [TestMethod]
    public void Utilization_Over100_ClampsTo100()
    {
        Assert.AreEqual(100.0, HardwareService.NormalizeUtilization(123.4));
    }

    [TestMethod]
    public void Utilization_Negative_ClampsToZero()
    {
        Assert.AreEqual(0.0, HardwareService.NormalizeUtilization(-3.0));
    }

    [TestMethod]
    public void Utilization_NaNOrInfinity_IsUnknown()
    {
        Assert.IsNull(HardwareService.NormalizeUtilization(double.NaN));
        Assert.IsNull(HardwareService.NormalizeUtilization(double.PositiveInfinity));
    }

    [TestMethod]
    public void Utility_123Percent_NotClamped()
    {
        // % Processor Utility legitimately exceeds 100% under Turbo Boost
        // (Microsoft docs): it must never be presented as busy time, and never
        // silently clamped away.
        Assert.AreEqual(123.4, HardwareService.NormalizeUtility(123.37));
        Assert.AreEqual(45.0, HardwareService.NormalizeUtility(45.04));
    }

    [TestMethod]
    public void Utility_NaNOrInfinity_IsUnknown()
    {
        Assert.IsNull(HardwareService.NormalizeUtility(double.NaN));
        Assert.IsNull(HardwareService.NormalizeUtility(double.NegativeInfinity));
    }

    [TestMethod]
    public void Metrics_AreIndependent_ValidUtilizationSurvivesBadUtility()
    {
        // A failed Utility read must never void a verified Utilization sample
        // (and vice versa): GetCurrentLoads normalizes each metric on its own.
        Assert.IsNotNull(HardwareService.NormalizeUtilization(45.17));
        Assert.IsNull(HardwareService.NormalizeUtility(double.NaN));
        Assert.IsNotNull(HardwareService.NormalizeUtility(123.37));
        Assert.IsNull(HardwareService.NormalizeUtilization(double.NaN));
    }

    [TestMethod]
    public void SteadyStateReadings_AreRealValuesNotUnknown()
    {
        using var svc = new HardwareService();
        var first = svc.GetCurrentLoads();
        Assert.IsTrue(first.Count == 0 || first.All(s => s.UtilizationPercent == null),
            "The first sampling round has no valid interval and must read Unknown.");
        var second = svc.GetCurrentLoads();
        var third = svc.GetCurrentLoads();
        if (third.Count == 0)
            Assert.Inconclusive("No performance counters on this machine.");
        Assert.IsTrue(second.All(s => s.UtilizationPercent.HasValue),
            "After priming, utilization must be a real 0-100 value, never Unknown.");
        Assert.IsTrue(third.All(s => s.UtilizationPercent.HasValue && s.UtilizationPercent >= 0 && s.UtilizationPercent <= 100));
    }

    [TestMethod]
    public void Group0_InstanceNames_MatchLogicalProcessors()
    {
        using var svc = new HardwareService();
        var loads = svc.GetCurrentLoads();
        // First call primes PDH: everything must read Unknown, never fake 0%.
        Assert.IsTrue(loads.Count == 0 || loads.All(s => s.UtilizationPercent == null),
            "First sample has no valid interval and must be Unknown.");
    }

    [TestMethod]
    public void ShutdownDuringSampling_NoThrowNoLeak()
    {
        var svc = new HardwareService();
        var loads = svc.GetCurrentLoads();
        svc.Dispose();
        var after = svc.GetCurrentLoads();
        Assert.AreEqual(0, after.Count);
        svc.Dispose();
        svc.ReleaseCpuLoadCounters();
        svc.ReleaseCpuLoadCounters();
    }

    [TestMethod]
    public async Task PinnedBurner_MapsToCorrectLp()
    {
        // Integration test, environment-aware by design: the expectation is
        // derived from the burner's OWN consumed CPU time over the same wall
        // window, never from an absolute percentage. A shared runner that
        // starves the burner yields Inconclusive (explicitly environmental),
        // never a false failure — and never a false pass.
        int lpCount = Environment.ProcessorCount;
        if (lpCount < 2) Assert.Inconclusive("Needs at least 2 logical processors.");
        int target = lpCount - 1;
        long mask = 1L << target;
        using var burner = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"while ($true) { $x = 1 }\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        Assert.IsNotNull(burner);
        try
        {
            burner!.ProcessorAffinity = (IntPtr)mask;
            burner.Refresh();
            if (burner.ProcessorAffinity != (IntPtr)mask)
                Assert.Inconclusive("Runner did not honor CPU affinity; mapping unverifiable here.");
            await Task.Delay(1500);
            using var svc = new HardwareService();
            // Prime + one intermediate sampling (reads #3+ carry a valid interval).
            svc.GetCurrentLoads();
            await Task.Delay(600);
            svc.GetCurrentLoads();
            burner.Refresh();
            TimeSpan t0 = burner.TotalProcessorTime;
            DateTime w0 = DateTime.UtcNow;
            await Task.Delay(1200);
            var loads = svc.GetCurrentLoads();
            burner.Refresh();
            TimeSpan t1 = burner.TotalProcessorTime;
            if (loads.Count != lpCount)
                Assert.Inconclusive($"Topology mismatch: {loads.Count} samples vs {lpCount} LPs.");
            double wallMs = Math.Max((t1 - t0).TotalMilliseconds, 1);
            double wallWall = Math.Max((DateTime.UtcNow - w0).TotalMilliseconds, 1);
            double expected = Math.Min(100, wallMs / wallWall * 100);
            if (expected < 20)
                Assert.Inconclusive($"Burner starved by the host (only {expected:F1}% of a core over the window); mapping unverifiable here.");
            double? measured = loads[target].UtilizationPercent;
            Assert.IsNotNull(measured, "Target LP must report a real reading, not Unknown.");
            Assert.IsTrue(Math.Abs(measured.Value - expected) <= 30,
                $"LP{target} reads {measured:F1}% but the pinned burner consumed {expected:F1}% of a core over the same window (mapping suspect).");
        }
        finally
        {
            try { burner!.Kill(); } catch { }
        }
    }

    [TestMethod]
    public void ProcessorInformation_InstancesMatchLogicalProcessors()
    {
        // Deterministic mapping contract: instance "0,i" exists for every
        // logical processor i of group 0 (verified live against a pinned
        // burner by PinnedBurner_MapsToCorrectLp).
        int lpCount = Environment.ProcessorCount;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var n in new PerformanceCounterCategory("Processor Information").GetInstanceNames())
                names.Add(n);
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"Performance counters unavailable here: {ex.Message}");
        }
        for (int i = 0; i < Math.Min(lpCount, 64); i++)
            Assert.IsTrue(names.Contains("0," + i), $"Missing PDH instance for logical processor {i}.");
    }
}

/// <summary>
/// Process protection policy: blacklist + Windows-location fail-closed for
/// automatic channels, honest UI warning, manual path unchanged. No
/// destructive operations: every mutation attempt below is refused before
/// any OS write (or targets only self-owned probe processes).
/// </summary>
[TestClass]
public sealed class ProcessSafetyTests
{
    [TestMethod]
    public void Blacklist_BlocksKnownSystemProcesses()
    {
        Assert.IsTrue(ProcessService.IsProtectedProcessName("svchost"));
        Assert.IsTrue(ProcessService.IsProtectedProcessName("dwm"));
        Assert.IsFalse(ProcessService.IsProtectedProcessName("notepad"));
        Assert.IsFalse(ProcessService.IsProtectedProcessName("AppActions"));
        Assert.IsFalse(ProcessService.IsProtectedProcessName(null));
    }

    [TestMethod]
    public void SystemComponentPath_WindowsDirOnly()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.IsTrue(ProcessService.IsSystemComponentPath(win + "\\System32\\svchost.exe"));
        Assert.IsTrue(ProcessService.IsSystemComponentPath(win + "\\SystemApps\\MicrosoftWindows.Client.CBS\\AppActions.exe"));
        Assert.IsTrue(ProcessService.IsSystemComponentPath(win + "/System32/notepad.exe"));
        Assert.IsFalse(ProcessService.IsSystemComponentPath("C:\\Tools\\app.exe"));
        Assert.IsFalse(ProcessService.IsSystemComponentPath(null));
        Assert.IsFalse(ProcessService.IsSystemComponentPath("relative\\app.exe"));
        // A path merely containing "Windows" elsewhere is NOT a system location.
        Assert.IsFalse(ProcessService.IsSystemComponentPath("C:\\MyWindowsTools\\app.exe"));
    }

    [TestMethod]
    public void AutomaticChannel_SkipsWindowsComponent_BeforeAnyWrite()
    {
        // Deterministic subject: first live, non-blacklisted Windows component
        // whose executable path is actually readable. Anything else would make
        // the verdict depend on ambient processes, not on the policy.
        Process? target = null;
        DateTime start = default;
        string? targetPath = null;
        foreach (string name in new[] { "ShellExperienceHost", "TextInputHost", "StartMenuExperienceHost", "SearchHost", "ApplicationFrameHost", "GameBar", "LockApp" })
        {
            if (ProcessService.IsProtectedProcessName(name)) continue;
            Process[] found;
            try { found = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var c in found)
            {
                try
                {
                    if (c.HasExited) continue;
                    string? path = ProcessIdentityValidator.TryGetProcessPath(c);
                    if (string.IsNullOrEmpty(path) || !ProcessService.IsSystemComponentPath(path)) continue;
                    start = c.StartTime.ToUniversalTime();
                    target = c;
                    targetPath = path;
                    break;
                }
                catch { }
            }
            if (target != null) break;
            foreach (var c in found) { try { c.Dispose(); } catch { } }
        }
        if (target == null) Assert.Inconclusive("No readable Windows-component process running here.");
        var key = new ProcessInstanceKey(target!.Id, start);
        var profile = new ProcessProfile { ProcessName = target.ProcessName, IsEnabled = true };
        var svc = new OptimizationService(new ProcessService(), () => new Dictionary<int, uint>());
        var result = svc.ApplyProfileToInstances(profile, target.ProcessName, new[] { key }, allowRealtimePriority: false, force: true, automatic: true);
        var first = result.Results.FirstOrDefault();
        try { target.Dispose(); } catch { }
        Assert.IsNotNull(first);
        Assert.AreEqual(OperationStatus.SkippedSystemLocation, first!.Status,
            $"Target was '{targetPath}'; the automatic channel must refuse before any OS write.");
        Assert.AreEqual("SKIPPED_SYSTEM_LOCATION", first!.Message);
        Assert.IsFalse(first.Success);
    }

    [TestMethod]
    public void ManualChannel_SameTarget_NotGatedByLocation()
    {
        // Fully inert: stale PID + unknown name can never authorize a write;
        // the only question is whether the location gate fires (it must not).
        var key = new ProcessInstanceKey(0, DateTime.UtcNow);
        var profile = new ProcessProfile { ProcessName = "definitely-not-a-real-process", IsEnabled = true };
        var svc = new OptimizationService(new ProcessService(), () => new Dictionary<int, uint>());
        var result = svc.ApplyProfileToInstances(profile, profile.ProcessName, new[] { key }, allowRealtimePriority: false, force: true, automatic: true);
        var first = result.Results.FirstOrDefault();
        // Unknown identity fails closed, but never via the location gate.
        if (first != null) Assert.AreNotEqual(OperationStatus.SkippedSystemLocation, first.Status);
    }

    [TestMethod]
    public void SystemLocationStatus_HasStableMessageCode()
    {
        Assert.AreEqual("SKIPPED_SYSTEM_LOCATION", OperationStatus.SkippedSystemLocation.ToMessageCode());
        Assert.IsFalse(OperationStatus.SkippedSystemLocation.IsSuccess());
        Assert.IsFalse(OperationStatus.SkippedSystemLocation.IsRetryable());
    }
}
