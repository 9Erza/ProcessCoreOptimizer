using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using ProcessCoreOptimizer.WPF.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ResetFirstRunTests
{
    private string? _tempRoot;
    private string? _previousOverride;

    [TestInitialize]
    public void UseIsolatedUserDir()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pco_reset_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _previousOverride = Environment.GetEnvironmentVariable("PCO_USER_DATA_DIR");
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", _tempRoot);
    }

    [TestCleanup]
    public void RestoreUserDir()
    {
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", _previousOverride);
        try { if (_tempRoot != null && Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
    }

    private static UserDataResetService Service(string root) => new(root);

    [TestMethod]
    public void FactoryReset_DeletesOnlyAllowlisted_PreservesForeign()
    {
        string root = _tempRoot!;
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(root, "profiles.json"), "[]");
        File.WriteAllText(Path.Combine(root, "ProcessCoreOptimizer.log"), "log");
        File.WriteAllText(Path.Combine(root, "my-export.json"), "keep me");
        string sub = Path.Combine(root, "user-stuff");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "notes.txt"), "keep");

        var svc = Service(root);
        var result = svc.FactoryResetAsync(() => false, () => System.Threading.Tasks.Task.CompletedTask, () => System.Threading.Tasks.Task.FromResult<string?>(null)).GetAwaiter().GetResult();

        Assert.IsTrue(result.Success, string.Join("; ", result.Errors));
        Assert.IsFalse(result.Blocked);
        Assert.IsFalse(File.Exists(Path.Combine(root, "settings.json")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "profiles.json.bak")) || File.Exists(Path.Combine(root, "profiles.json")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "my-export.json")), "Foreign files must survive.");
        Assert.IsTrue(Directory.Exists(sub), "Non-empty user directory must survive.");
        Assert.IsTrue(File.Exists(Path.Combine(root, "ProcessCoreOptimizer.log")), "Active log handle cannot be deleted; it is truncated post-wipe.");
        Assert.IsNotNull(result.ActiveLogPath);
        Assert.IsTrue(File.Exists(Path.Combine(root, AppPaths.FactoryResetStampFileName)), "Reset stamp must be left for the legacy guard.");
        Assert.IsNull(result.StartupRemovalError);
    }

    [TestMethod]
    public void FactoryReset_BlockedWhenBusy_DeletesNothing()
    {
        string root = _tempRoot!;
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        var svc = Service(root);
        var result = svc.FactoryResetAsync(() => true, () => System.Threading.Tasks.Task.CompletedTask, () => System.Threading.Tasks.Task.FromResult<string?>(null)).GetAwaiter().GetResult();
        Assert.IsTrue(result.Blocked);
        Assert.IsTrue(File.Exists(Path.Combine(root, "settings.json")));
    }

    [TestMethod]
    public void FactoryReset_ActiveLogOutsideRoot_NotTouched()
    {
        string root = _tempRoot!;
        string external = Path.Combine(Path.GetTempPath(), $"pco_extlog_{Guid.NewGuid():N}.log");
        File.WriteAllText(external, "external");
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
            var svc = Service(root);
            var result = svc.FactoryResetAsync(() => false, () => System.Threading.Tasks.Task.CompletedTask, () => System.Threading.Tasks.Task.FromResult<string?>(null)).GetAwaiter().GetResult();
            Assert.IsTrue(result.Success);
            Assert.IsTrue(File.Exists(external), "External log paths are never touched.");
        }
        finally
        {
            try { File.Delete(external); } catch { }
        }
    }

    [TestMethod]
    public void FactoryReset_StartupRemovalError_SurfacesSeparately()
    {
        string root = _tempRoot!;
        var svc = Service(root);
        var result = svc.FactoryResetAsync(
            () => false,
            () => System.Threading.Tasks.Task.CompletedTask,
            () => System.Threading.Tasks.Task.FromResult<string?>("task still registered")).GetAwaiter().GetResult();
        Assert.IsTrue(result.Success, "Data wipe succeeded; startup failure must not fail the wipe.");
        Assert.AreEqual("task still registered", result.StartupRemovalError);
    }

    [TestMethod]
    public void FactoryReset_StampConsumesLegacyReimport()
    {
        string root = _tempRoot!;
        var svc = Service(root);
        var result = svc.FactoryResetAsync(() => false, () => System.Threading.Tasks.Task.CompletedTask, () => System.Threading.Tasks.Task.FromResult<string?>(null)).GetAwaiter().GetResult();
        Assert.IsTrue(result.Success);

        // A legacy file appearing afterwards must NOT be reimported on the next launch.
        AppPaths.MigrateLegacyFileIfNeeded("settings.json");
        Assert.IsFalse(File.Exists(Path.Combine(root, "settings.json")), "Stamp must suppress the legacy reimport once.");
        Assert.IsFalse(File.Exists(Path.Combine(root, AppPaths.FactoryResetStampFileName)), "Stamp is one-shot.");
    }

    [TestMethod]
    public void RestoreDefaults_PreservesLanguage_ResetsRest()
    {
        var current = new AppSettings
        {
            Language = "pl",
            StartWithWindows = true,
            AllowRealtimePriority = true,
            HardwareMonitorEnabled = true,
            OnboardingCompletedVersion = 1
        };
        var defaults = UserDataResetService.BuildDefaultSettings(current);
        Assert.AreEqual("pl", defaults.Language, "Language is preserved for readable messages.");
        Assert.IsFalse(defaults.StartWithWindows);
        Assert.IsFalse(defaults.AllowRealtimePriority);
        Assert.IsFalse(defaults.HardwareMonitorEnabled);
        Assert.AreEqual(0, defaults.OnboardingCompletedVersion);
        Assert.AreEqual(StartupWindowMode.Normal, defaults.StartupWindowMode);
    }

    [TestMethod]
    public void FirstRun_Finish_PersistsChoicesAndMarker()
    {
        var vm = new FirstRunViewModel { Language = "pl" };
        vm.LoadFrom(new AppSettings { Language = "en" });
        Assert.AreEqual("en", vm.Language);
        vm.Language = "pl";
        vm.StartWithWindows = true;
        vm.WindowMode = StartupWindowMode.Tray;
        vm.RunElevated = false;
        int page = 0;
        vm.NextCommand.Execute(null); page++;
        vm.NextCommand.Execute(null); page++;
        Assert.AreEqual(2, vm.PageIndex);
        Assert.IsTrue(vm.IsLastPage);
        vm.FinishCommand.Execute(null);

        var loaded = new SettingsService().LoadSettings();
        Assert.AreEqual("pl", loaded.Language);
        Assert.IsTrue(loaded.StartWithWindows);
        Assert.AreEqual(StartupWindowMode.Tray, loaded.StartupWindowMode);
        Assert.IsFalse(loaded.HardwareMonitorEnabled, "Monitoring stays off unless the user enables it later.");
        Assert.IsFalse(loaded.AllowRealtimePriority);
        Assert.AreEqual(1, loaded.OnboardingCompletedVersion);
    }

    [TestMethod]
    public void FirstRun_Skip_StampsWithoutApplying()
    {
        var vm = new FirstRunViewModel();
        vm.SkipCommand.Execute(null);
        var loaded = new SettingsService().LoadSettings();
        Assert.AreEqual(1, loaded.OnboardingCompletedVersion);
        Assert.IsFalse(loaded.StartWithWindows);
    }

    [TestMethod]
    public void HadExistingSettings_ReflectsIsolatedRoot()
    {
        Assert.IsFalse(new SettingsService().HadExistingSettings());
        new SettingsService().SaveSettings(new AppSettings());
        Assert.IsTrue(new SettingsService().HadExistingSettings());
    }
}
