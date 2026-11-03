using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Semantic contract of the context-aware UI: no CPU editor without a live
/// target, explicit mutation scope, preview-only CPU tab, selection-gated
/// actions. Static XAML/VM assertions (no window needed); runtime behavior
/// is covered by the GUI automation harness.
/// </summary>
[TestClass]
public sealed class ProcessScopeContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "version.txt")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Repo root not found.");
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string MainWindow() => Read("ProcessCoreOptimizer", "Views", "MainWindow.xaml");
    private static string MainViewModel() => Read("ProcessCoreOptimizer", "ViewModels", "MainViewModel.cs");

    [TestMethod]
    public void RightPanel_OnlyWithLiveTarget_NeverBare()
    {
        string xaml = MainWindow();
        Assert.IsTrue(xaml.Contains("HasProcessTarget"), "Processes panel must gate on HasProcessTarget.");
        Assert.IsTrue(xaml.Contains("HasSavedProfileTarget"), "Profiles panel must gate on HasSavedProfileTarget.");
        // No unconditional CPU panel: the Cpu tab owns the full width and hosts no Apply button.
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(xaml, "ViewCpu").Count,
            "Only the CpuView host may reference ViewCpu; no ViewCpu-gated action stack may exist.");
    }

    [TestMethod]
    public void ClosePanel_ClearsSelectionWithoutMutation()
    {
        string xaml = MainWindow();
        Assert.IsTrue(xaml.Contains("CloseProcessTargetCommand"), "Panel must offer selection close.");
        string vm = MainViewModel();
        Assert.IsTrue(vm.Contains("SelectedProcess = null") && vm.Contains("SelectedInstanceRow = null"),
            "Close must null both selections.");
        // Closing clears details (no stale target survives).
        int closeIdx = vm.IndexOf("CloseProcessTargetCommand = new RelayCommand", StringComparison.Ordinal);
        Assert.IsTrue(closeIdx >= 0);
    }

    [TestMethod]
    public void ScopeLabel_ExposesGroupCountAndPid()
    {
        string vm = MainViewModel();
        Assert.IsTrue(vm.Contains("TargetScopeLabel"), "Explicit scope label must exist.");
        Assert.IsTrue(vm.Contains("Process group"), "Group scope must name the instance count.");
        Assert.IsTrue(vm.Contains("SelectedInstanceRow.ProcessId"),
            "Instance scope must name the PID.");
    }

    [TestMethod]
    public void ModeSwitchAndExit_InvalidateSelection()
    {
        string vm = MainViewModel();
        // Grouped <-> Instances and tab switches drop the old selection: no stale target survives.
        Assert.IsTrue(vm.Contains("ProcessViewMode") && vm.Contains("SelectedProcess = null") && vm.Contains("SelectedInstanceRow = null"),
            "Mode switch and tab switch must clear both process selections.");
    }

    [TestMethod]
    public void CpuTab_IsReadOnly_PresetIsPreviewOnly()
    {
        string cpu = Read("ProcessCoreOptimizer", "Views", "CpuView.xaml");
        Assert.IsFalse(cpu.Contains("ApplyCorePresetCommand"),
            "CPU tab must not apply presets (informational only).");
        Assert.IsTrue(cpu.Contains("PreviewCorePresetCommand"), "Presets must preview, not apply.");
        Assert.IsTrue(cpu.Contains("IsTileReadOnly=\"True\""), "CPU tiles must be read-only.");
        Assert.IsTrue(cpu.Contains("StrAffinityExplained") && cpu.Contains("StrCpuSetsExplained"),
            "Affinity vs CPU Sets must be explained, not implied.");
    }

    [TestMethod]
    public void LoadingState_DistinguishesScanFromEmpty()
    {
        string procs = Read("ProcessCoreOptimizer", "Views", "ProcessesView.xaml");
        Assert.IsTrue(procs.Contains("IsProcessListLoading"), "List must expose a loading state.");
        Assert.IsTrue(procs.Contains("StrInitialLoading"), "Loading hint must be localized.");
        Assert.IsTrue(procs.Contains("StrSelectProcessHint"), "No-selection hint must be localized.");
    }

    [TestMethod]
    public void ProfilesAndRules_GateActionsOnSelection()
    {
        string profiles = Read("ProcessCoreOptimizer", "Views", "ProfilesView.xaml");
        Assert.IsTrue(profiles.Contains("StrNoProfilesTitle"), "Empty state is required.");
        Assert.IsTrue(profiles.Contains("HasSavedProfileTarget"), "Edit row must gate on selection.");
        string rules = Read("ProcessCoreOptimizer", "Views", "SmartRulesView.xaml");
        Assert.IsTrue(rules.Contains("StrNoRulesTitle"), "Rules empty state is required.");
        Assert.IsTrue(rules.Contains("HasSelectedRule"), "Rule actions must gate on selection.");
    }

    [TestMethod]
    public void CpuTab_ShowsRealModel_NotButtonLabel()
    {
        string cpu = Read("ProcessCoreOptimizer", "Views", "CpuView.xaml");
        Assert.IsTrue(cpu.Contains("CpuModelText"),
            "Processor overview must show the real CPU model, not the SMT toggle label.");
    }

    [TestMethod]
    public void NoDeferRefreshAroundCollectionMutation()
    {
        // CollectionView forbids source mutation while Refresh is deferred;
        // the pause-sort pattern must be used instead.
        string vm = MainViewModel();
        Assert.IsFalse(vm.Contains("DeferRefresh"), "DeferRefresh must not wrap collection mutation.");
        Assert.IsTrue(vm.Contains("SortDescriptions"), "Sort pause pattern must guard bulk row updates.");
    }

    [TestMethod]
    public void CoreLoads_NeverBlockUiThread()
    {
        string vm = MainViewModel();
        Assert.IsTrue(vm.Contains("Task.Run(() => _hardwareService.GetCurrentLoads())"),
            "PDH warmup must run off the UI thread.");
    }
}
