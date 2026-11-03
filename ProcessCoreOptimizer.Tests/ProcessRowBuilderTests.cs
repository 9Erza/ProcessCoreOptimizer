using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ProcessRowBuilderTests
{
    private static ProcessInstanceData Data(int pid, string name, string? path, double? cpu, long? mem, System.Diagnostics.ProcessPriorityClass? prio)
    {
        return new ProcessInstanceData
        {
            Key = new ProcessInstanceKey(pid, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(pid)),
            ProcessName = name,
            ExecutablePath = path,
            CpuUsagePercent = cpu,
            WorkingSetBytes = mem,
            Priority = prio
        };
    }

    [TestMethod]
    public void BuildInstanceRows_AttachesValuesAndUnknowns()
    {
        var rows = ProcessRowBuilder.BuildInstanceRows(
            new[]
            {
                Data(10, "myapp", @"C:\A\myapp.exe", 12.345, 50L * 1024 * 1024, System.Diagnostics.ProcessPriorityClass.Normal),
                Data(11, "myapp", null, null, null, null)
            },
            Enumerable.Empty<ProcessProfile>(),
            translatePriority: null);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(10, rows[0].ProcessId);
        Assert.AreEqual(@"C:\A\myapp.exe", rows[0].ExecutablePath);
        Assert.AreEqual($"{12.345:F1}%", rows[0].CpuUsage);
        Assert.AreEqual("50 MB", rows[0].RamUsageMB);
        Assert.AreEqual("Normal", rows[0].Priority);

        // OS refused the reads: honest Unknown placeholders, never invented values.
        Assert.AreEqual("…", rows[1].CpuUsage);
        Assert.AreEqual("…", rows[1].RamUsageMB);
        Assert.AreEqual("Unknown", rows[1].Priority);
        Assert.IsNull(rows[1].CpuUsagePercent);
    }

    [TestMethod]
    public void BuildInstanceRows_AttachesMatchingProfileName()
    {
        var nameOnly = new ProcessProfile { ProcessName = "myapp", IsEnabled = true };
        var pathBound = new ProcessProfile { ProcessName = "myapp", ExecutablePath = @"C:\B\myapp.exe", IsEnabled = true };
        var disabled = new ProcessProfile { ProcessName = "myapp", IsEnabled = false };
        var profiles = new[] { nameOnly, pathBound, disabled };

        var rows = ProcessRowBuilder.BuildInstanceRows(
            new[] { Data(20, "myapp", @"C:\B\myapp.exe", 1, 1, System.Diagnostics.ProcessPriorityClass.Normal) },
            profiles,
            translatePriority: null);

        // Name-only comes first in the list, but the path-bound one also matches;
        // either is a legitimate attach — the engine picks by precedence at apply time.
        Assert.AreEqual(1, rows.Count);
        Assert.IsFalse(string.IsNullOrEmpty(rows[0].ProfileName));
    }

    [TestMethod]
    public void FormatCpu_HandlesNonFinite()
    {
        Assert.AreEqual("…", ProcessRowBuilder.FormatCpu(null));
        Assert.AreEqual("…", ProcessRowBuilder.FormatCpu(double.NaN));
        Assert.AreEqual("…", ProcessRowBuilder.FormatCpu(double.PositiveInfinity));
        Assert.AreEqual($"{0:F1}%", ProcessRowBuilder.FormatCpu(0));
    }

    [TestMethod]
    public void IsScopedSelectionValid_EmptyOrNull_IsInvalid()
    {
        Assert.IsFalse(ProcessRowBuilder.IsScopedSelectionValid(null));
        Assert.IsFalse(ProcessRowBuilder.IsScopedSelectionValid(Array.Empty<ProcessInstanceKey>()));
        Assert.IsTrue(ProcessRowBuilder.IsScopedSelectionValid(
            new[] { new ProcessInstanceKey(1, DateTime.UtcNow) }));
    }

    [TestMethod]
    public void ApplyProfileToInstances_EmptyScope_IsNoOpWithoutOsAccess()
    {
        // Must not touch the OS at all: empty selection can never widen to the group.
        var service = new OptimizationService(
            new ProcessService(),
            cpuSetMapProvider: () => new System.Collections.Generic.Dictionary<int, uint>(),
            restoreService: null);
        var profile = new ProcessProfile { ProcessName = "definitely-not-running-xyz", AffinityMask = 0x1 };
        var batch = service.ApplyProfileToInstances(profile, "definitely-not-running-xyz", Array.Empty<ProcessInstanceKey>(), allowRealtimePriority: false, force: true);
        Assert.AreEqual(0, batch.Total);
        Assert.AreEqual(0, batch.Results.Count);
    }
}
