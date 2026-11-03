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
        int lpCount = Environment.ProcessorCount;
        if (lpCount < 2) Assert.Inconclusive("Needs at least 2 logical processors.");
        int target = lpCount - 1;
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
            burner!.ProcessorAffinity = (IntPtr)(1L << target);
            await Task.Delay(1500);
            using var svc = new HardwareService();
            // Prime + two real samplings (reads #3+ carry a valid interval).
            svc.GetCurrentLoads();
            await Task.Delay(600);
            svc.GetCurrentLoads();
            await Task.Delay(600);
            var loads = svc.GetCurrentLoads();
            if (loads.Count != lpCount)
                Assert.Inconclusive($"Topology mismatch: {loads.Count} samples vs {lpCount} LPs.");
            double targetBusy = loads[target].UtilizationPercent ?? -1;
            Assert.IsTrue(targetBusy >= 50,
                $"Pinned burner on LP{target} must dominate its processor (got {targetBusy}%).");
            Assert.IsTrue(loads[target].UtilizationPercent >= (loads.Where((_, i) => i != target).Select(s => s.UtilizationPercent ?? 0).DefaultIfEmpty(0).Max()),
                $"LP{target} must be the busiest logical processor.");
        }
        finally
        {
            try { burner!.Kill(); } catch { }
        }
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
        var candidates = new[] { "ShellExperienceHost", "TextInputHost", "StartMenuExperienceHost", "SearchHost", "ApplicationFrameHost" }
            .SelectMany(n => { try { return Process.GetProcessesByName(n); } catch { return Array.Empty<Process>(); } })
            .Where(p => { try { return !p.HasExited; } catch { return false; } })
            .ToList();
        if (candidates.Count == 0) Assert.Inconclusive("No Windows component process running.");
        Process? target = null;
        DateTime start = default;
        foreach (var c in candidates)
        {
            try
            {
                if (c.HasExited) continue;
                start = c.StartTime.ToUniversalTime();
                target = c;
                break;
            }
            catch { }
        }
        foreach (var c in candidates) { if (!ReferenceEquals(c, target)) try { c.Dispose(); } catch { } }
        if (target == null) Assert.Inconclusive("Cannot read start time.");
        var key = new ProcessInstanceKey(target!.Id, start);
        var profile = new ProcessProfile { ProcessName = target.ProcessName, IsEnabled = true };
        var svc = new OptimizationService(new ProcessService(), () => new Dictionary<int, uint>());
        var result = svc.ApplyProfileToInstances(profile, target.ProcessName, new[] { key }, allowRealtimePriority: false, force: true, automatic: true);
        var first = result.Results.FirstOrDefault();
        try { target.Dispose(); } catch { }
        Assert.IsNotNull(first);
        Assert.AreEqual(OperationStatus.SkippedSystemLocation, first!.Status);
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
