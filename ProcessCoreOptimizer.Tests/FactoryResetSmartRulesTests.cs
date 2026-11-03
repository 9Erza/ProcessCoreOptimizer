using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// P0-1 regression: factory reset must cover smartrules.json (+.bak), leave a
/// persistent no-legacy stamp, and never let wiped rules return. All isolated:
/// temp data root via ctor (no env override needed) + temp legacy file in the
/// test output directory (created and removed inside the test).
/// </summary>
[TestClass]
public sealed class FactoryResetSmartRulesTests
{
    private string? _tempRoot;
    private string? _legacyRulesOriginal;
    private bool _legacyRulesExisted;

    [TestInitialize]
    public void Isolate()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pco_rulesreset_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        string bin = AppDomain.CurrentDomain.BaseDirectory;
        string r = Path.Combine(bin, "smartrules.json");
        _legacyRulesExisted = File.Exists(r);
        if (_legacyRulesExisted) _legacyRulesOriginal = File.ReadAllText(r);
    }

    [TestCleanup]
    public void Restore()
    {
        try
        {
            string bin = AppDomain.CurrentDomain.BaseDirectory;
            string r = Path.Combine(bin, "smartrules.json");
            if (_legacyRulesExisted) File.WriteAllText(r, _legacyRulesOriginal ?? string.Empty);
            else if (File.Exists(r)) File.Delete(r);
        }
        catch { }
        try { if (_tempRoot != null && Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
    }

    private static Task<DataResetResult> RunReset(string root, CancellationToken token = default)
    {
        var service = new UserDataResetService(root);
        return service.FactoryResetAsync(
            isBusy: () => false,
            prepareForWipeAsync: () => Task.CompletedTask,
            removeStartupAsync: () => Task.FromResult<string?>(null),
            cancellationToken: token);
    }

    private static void WriteJson(string path, string content) =>
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);

    [TestMethod]
    public async Task ResetDeletesRulesAndBackups()
    {
        string root = _tempRoot!;
        WriteJson(Path.Combine(root, "settings.json"), "{\"Language\":\"en\"}");
        WriteJson(Path.Combine(root, "smartrules.json"), "[{\"ProcessName\":\"game\"}]");
        WriteJson(Path.Combine(root, "smartrules.json.bak"), "[{\"ProcessName\":\"old\"}]");

        DataResetResult result = await RunReset(root);

        Assert.IsTrue(result.Success, "Errors: " + string.Join("; ", result.Errors));
        Assert.IsFalse(File.Exists(Path.Combine(root, "smartrules.json")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "smartrules.json.bak")));
        Assert.IsTrue(result.DeletedFiles.Contains("smartrules.json"));
        Assert.IsTrue(result.DeletedFiles.Contains("smartrules.json.bak"));
    }

    [TestMethod]
    public async Task ResetStampSuppressesLegacyRuleReimport()
    {
        string root = _tempRoot!;
        string savedOverride = Environment.GetEnvironmentVariable("PCO_USER_DATA_DIR") ?? string.Empty;
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", root);
        try
        {
            await RunReset(root);

            Assert.IsTrue(File.Exists(Path.Combine(root, ".nolegacy.smartrules.json")),
                "Reset must leave a persistent no-legacy stamp for smartrules.json.");

            // Legacy rules appear next to the EXE after the reset (e.g. portable copy).
            WriteJson(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smartrules.json"),
                "[{\"ProcessName\":\"legacy-rule\"}]");

            var service = new SmartRuleService();
            var first = service.LoadRules();
            var second = service.LoadRules();
            Assert.AreEqual(0, first.Count, "Wiped rules must not be reimported from legacy location.");
            Assert.AreEqual(0, second.Count);
            Assert.IsFalse(File.Exists(Path.Combine(root, "smartrules.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR",
                string.IsNullOrEmpty(savedOverride) ? null : savedOverride);
        }
    }

    [TestMethod]
    public async Task InterruptedReset_NeverReportsSuccess()
    {
        string root = _tempRoot!;
        WriteJson(Path.Combine(root, "smartrules.json"), "[{\"ProcessName\":\"game\"}]");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => RunReset(root, cts.Token));
    }

    [TestMethod]
    public async Task ReadOnlyRulesFile_ReportsIncompleteNotFull()
    {
        string root = _tempRoot!;
        string path = Path.Combine(root, "smartrules.json");
        WriteJson(path, "[{\"ProcessName\":\"game\"}]");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            DataResetResult result = await RunReset(root);

            Assert.IsFalse(result.Success, "A partial wipe must not be reported as full success.");
            Assert.IsTrue(result.Errors.Count > 0);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public async Task NoRulesAfterRestartSimulation()
    {
        string root = _tempRoot!;
        string savedOverride = Environment.GetEnvironmentVariable("PCO_USER_DATA_DIR") ?? string.Empty;
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", root);
        try
        {
            WriteJson(Path.Combine(root, "smartrules.json"), "[{\"ProcessName\":\"game\"}]");
            DataResetResult result = await RunReset(root);
            Assert.IsTrue(result.Success, "Errors: " + string.Join("; ", result.Errors));

            // Simulated relaunch: fresh service instance, same data dir.
            var fresh = new SmartRuleService();
            Assert.AreEqual(0, fresh.LoadRules().Count, "Rule list must be empty after restart.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR",
                string.IsNullOrEmpty(savedOverride) ? null : savedOverride);
        }
    }
}
