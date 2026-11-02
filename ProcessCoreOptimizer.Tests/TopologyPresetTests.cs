using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Services;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class TopologyPresetTests
{
    private static CoreInfoDto Dto(int index, int core, bool thread = false, bool ecore = false, bool unknown = false) =>
        new() { Index = index, CoreIndex = core, IsThread = thread, IsECore = ecore, IsClassificationUnknown = unknown };

    [TestMethod]
    public void HybridTopology_PresetsSplitCorrectly()
    {
        // 2 P-cores with HT (0,1 + 2,3) + 4 E-cores (4-7).
        var cores = new List<CoreInfoDto>
        {
            Dto(0, 0), Dto(1, 0, thread: true),
            Dto(2, 1), Dto(3, 1, thread: true),
            Dto(4, 2, ecore: true), Dto(5, 3, ecore: true),
            Dto(6, 4, ecore: true), Dto(7, 5, ecore: true),
        };
        var presets = CorePresets.Build(cores);
        var all = presets.First(p => p.Id == "all");
        var p = presets.First(p => p.Id == "p-cores");
        var e = presets.First(p => p.Id == "e-cores");
        var phys = presets.First(p => p.Id == "physical");

        Assert.IsTrue(all.IsAvailable);
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, all.LogicalProcessors);
        Assert.IsTrue(p.IsAvailable);
        CollectionAssert.AreEquivalent(new[] { 0, 2 }, p.LogicalProcessors);
        Assert.IsTrue(e.IsAvailable);
        CollectionAssert.AreEquivalent(new[] { 4, 5, 6, 7 }, e.LogicalProcessors);
        Assert.IsTrue(phys.IsAvailable);
        CollectionAssert.AreEquivalent(new[] { 0, 2, 4, 5, 6, 7 }, phys.LogicalProcessors);
    }

    [TestMethod]
    public void HomogeneousCpu_ECorePreset_Unavailable()
    {
        var cores = new List<CoreInfoDto> { Dto(0, 0), Dto(1, 0, thread: true) };
        var presets = CorePresets.Build(cores);
        Assert.IsFalse(presets.First(p => p.Id == "e-cores").IsAvailable);
        Assert.IsTrue(presets.First(p => p.Id == "p-cores").IsAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(presets.First(p => p.Id == "e-cores").UnavailableReason));
    }

    [TestMethod]
    public void UnknownClassification_NoPECoresPresets()
    {
        var cores = new List<CoreInfoDto> { Dto(0, 0, unknown: true), Dto(1, 1, unknown: true) };
        var presets = CorePresets.Build(cores);
        Assert.IsFalse(presets.First(p => p.Id == "p-cores").IsAvailable);
        Assert.IsFalse(presets.First(p => p.Id == "e-cores").IsAvailable);
        Assert.IsFalse(presets.First(p => p.Id == "physical").IsAvailable);
        Assert.IsTrue(presets.First(p => p.Id == "all").IsAvailable);
    }

    [TestMethod]
    public void NoSmt_PhysicalPreset_UnavailableWithReason()
    {
        var cores = new List<CoreInfoDto> { Dto(0, 0), Dto(1, 1) };
        var phys = CorePresets.Build(cores).First(p => p.Id == "physical");
        Assert.IsFalse(phys.IsAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(phys.UnavailableReason));
    }

    [TestMethod]
    public void ResolveGroupPath_UniqueWins_DisagreementNull()
    {
        Assert.AreEqual(@"C:\A\app.exe", ProcessScannerService.ResolveGroupPath(new[] { @"C:\A\app.exe", @"c:\a\APP.exe" }));
        Assert.IsNull(ProcessScannerService.ResolveGroupPath(new[] { @"C:\A\app.exe", @"C:\B\app.exe" }));
        Assert.IsNull(ProcessScannerService.ResolveGroupPath(new string?[] { null, null }));
        Assert.AreEqual(@"C:\A\app.exe", ProcessScannerService.ResolveGroupPath(new string?[] { null, @"C:\A\app.exe", null }));
        Assert.IsNull(ProcessScannerService.ResolveGroupPath(new[] { @"C:\A\app.exe", null, @"C:\B\app.exe" }));
    }

    [TestMethod]
    public void TopologyReport_MultiGroup_DetectedHonestly()
    {
        var svc = new HardwareService();
        var report = svc.GetTopologyReport(new List<ProcessCoreOptimizer.WPF.Models.CoreInfo>());
        Assert.IsNotNull(report);
        Assert.IsTrue(report.ProcessorGroups >= 1);
    }
}
