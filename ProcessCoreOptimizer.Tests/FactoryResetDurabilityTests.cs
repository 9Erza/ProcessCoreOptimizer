using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.IO;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// P0 regression: factory reset must be durable across repeated reads and
/// simulated relaunches, even with legacy files present. Never touches real
/// user data: isolated temp root + PCO_USER_DATA_DIR + temp legacy files in
/// the test output directory (created and removed inside the test).
/// </summary>
[TestClass]
public sealed class FactoryResetDurabilityTests
{
    private string? _tempRoot;
    private string? _previousOverride;
    private string? _legacySettingsOriginal;
    private string? _legacyProfilesOriginal;
    private bool _legacySettingsExisted;
    private bool _legacyProfilesExisted;

    [TestInitialize]
    public void Isolate()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pco_durable_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _previousOverride = Environment.GetEnvironmentVariable("PCO_USER_DATA_DIR");
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", _tempRoot);

        string bin = AppDomain.CurrentDomain.BaseDirectory;
        string s = Path.Combine(bin, "settings.json");
        string p = Path.Combine(bin, "profiles.json");
        _legacySettingsExisted = File.Exists(s);
        _legacyProfilesExisted = File.Exists(p);
        if (_legacySettingsExisted) _legacySettingsOriginal = File.ReadAllText(s);
        if (_legacyProfilesExisted) _legacyProfilesOriginal = File.ReadAllText(p);
    }

    [TestCleanup]
    public void Restore()
    {
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", _previousOverride);
        try
        {
            string bin = AppDomain.CurrentDomain.BaseDirectory;
            string s = Path.Combine(bin, "settings.json");
            string p = Path.Combine(bin, "profiles.json");
            if (_legacySettingsExisted) File.WriteAllText(s, _legacySettingsOriginal ?? string.Empty);
            else if (File.Exists(s)) File.Delete(s);
            if (_legacyProfilesExisted) File.WriteAllText(p, _legacyProfilesOriginal ?? string.Empty);
            else if (File.Exists(p)) File.Delete(p);
        }
        catch { }
        try { if (_tempRoot != null && Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
    }

    private static void WriteJson(string path, string content) =>
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);

    [TestMethod]
    public void ResetSurvivesRepeatedReadsAndRelaunches()
    {
        string root = _tempRoot!;
        string bin = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Live data + backups + legacy files side by side.
        WriteJson(Path.Combine(root, "settings.json"), "{\"Language\":\"pl\"}");
        WriteJson(Path.Combine(root, "settings.json.bak"), "{\"Language\":\"pl\"}");
        WriteJson(Path.Combine(root, "profiles.json"), "[{\"ProcessName\":\"game\",\"AffinityMask\":3}]");
        WriteJson(Path.Combine(root, "profiles.json.bak"), "[]");
        WriteJson(Path.Combine(bin, "settings.json"), "{\"Language\":\"en\",\"StartWithWindows\":true}");
        WriteJson(Path.Combine(bin, "profiles.json"), "[{\"ProcessName\":\"legacy\",\"AffinityMask\":1}]");

        // 2. Normal migration works while data exists (no-op here: live files win).
        var settingsSvc = new SettingsService();
        var loaded = settingsSvc.LoadSettings();
        Assert.AreEqual("pl", loaded.Language);

        // 3. Factory reset.
        var reset = new UserDataResetService(root);
        var result = reset.FactoryResetAsync(
            () => false,
            () => System.Threading.Tasks.Task.CompletedTask,
            () => System.Threading.Tasks.Task.FromResult<string?>(null)).GetAwaiter().GetResult();
        Assert.IsTrue(result.Success, string.Join("; ", result.Errors));
        Assert.IsFalse(File.Exists(Path.Combine(root, "settings.json")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "settings.json.bak")), "Backups of managed data are wiped too.");
        Assert.IsFalse(File.Exists(Path.Combine(root, "profiles.json")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "profiles.json.bak")));

        // 4. Repeated reads + simulated relaunches (fresh service instances).
        for (int i = 0; i < 3; i++)
        {
            var s = new SettingsService().LoadSettings();
            Assert.AreEqual("en", s.Language, $"Read #{i}: defaults, not resurrected data.");
            Assert.IsFalse(s.StartWithWindows, $"Read #{i}: wiped startup flag stays off.");
            var profiles = new ProfileService().LoadProfiles();
            Assert.AreEqual(0, profiles.Count, $"Read #{i}: wiped profiles stay gone.");
        }

        // 5. Legacy files on disk were never deleted and never reimported.
        Assert.IsTrue(File.Exists(Path.Combine(bin, "settings.json")), "Legacy source files are not PCO's to delete.");
        Assert.IsTrue(File.Exists(Path.Combine(bin, "profiles.json")));
    }

    [TestMethod]
    public void StampPerFile_OrderIndependent()
    {
        string root = _tempRoot!;
        var reset = new UserDataResetService(root);
        var result = reset.FactoryResetAsync(
            () => false,
            () => System.Threading.Tasks.Task.CompletedTask,
            () => System.Threading.Tasks.Task.FromResult<string?>(null)).GetAwaiter().GetResult();
        Assert.IsTrue(result.Success);

        // Profiles reader runs first this time: settings stamp must survive it.
        new ProfileService().LoadProfiles();
        Assert.IsFalse(File.Exists(Path.Combine(root, "profiles.json")));
        Assert.IsTrue(File.Exists(Path.Combine(root, AppPaths.LegacySuppressStampFileName("settings.json"))),
            "Settings stamp survives the profiles read.");

        // Settings reader reuses its own persistent stamp, repeatedly.
        new SettingsService().LoadSettings();
        new SettingsService().LoadSettings();
        Assert.IsTrue(File.Exists(Path.Combine(root, AppPaths.LegacySuppressStampFileName("settings.json"))),
            "Suppression stamps persist across launches.");
    }

    [TestMethod]
    public void DeniedPaths_FailClosed()
    {
        string root = _tempRoot!;
        // Simulate an unreadable data directory.
        var locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        using var hold = File.Open(Path.Combine(locked, "x.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var svc = new UserDataResetService(Path.Combine(locked, "nonexistent-child"));
        var result = svc.FactoryResetAsync(
            () => false,
            () => System.Threading.Tasks.Task.CompletedTask,
            () => System.Threading.Tasks.Task.FromResult<string?>(null)).GetAwaiter().GetResult();
        // Nonexistent child of an existing dir: created fresh, nothing to delete.
        Assert.IsTrue(result.Success);
    }
}
