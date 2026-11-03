using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.IO;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class DiagnosticsAnonymizationTests
{
    [TestMethod]
    public void AnonymizeLine_MasksEmbeddedUserPath()
    {
        string line = "Error while reading C:\\Users\\Example\\Documents\\private.exe denied";
        string masked = DiagnosticsExportService.AnonymizeLine(line);
        Assert.IsFalse(masked.Contains("Example"), masked);
        Assert.IsTrue(masked.Contains(@"C:\Users\<user>"), masked);
        Assert.IsTrue(masked.Contains("private.exe"), "File name stays for support value.");
    }

    [TestMethod]
    public void AnonymizeLine_MasksUncHostKeepsShare()
    {
        string masked = DiagnosticsExportService.AnonymizeLine(@"Copy from \\FileSrv\Share\app.exe failed");
        Assert.IsFalse(masked.Contains("FileSrv"), masked);
        Assert.IsTrue(masked.Contains(@"\\<host>"), masked);
    }

    [TestMethod]
    public void AnonymizeLine_MasksEmail()
    {
        string masked = DiagnosticsExportService.AnonymizeLine("Contact john.doe@example.com for logs");
        Assert.IsFalse(masked.Contains("john.doe@example.com"), masked);
        Assert.IsTrue(masked.Contains("<email>"), masked);
    }

    [TestMethod]
    public void AnonymizeLine_PreservesSystemPaths()
    {
        string line = @"Read C:\Windows\System32\timeout.exe ok";
        Assert.AreEqual(line, DiagnosticsExportService.AnonymizeLine(line));
    }

    [TestMethod]
    public void AnonymizeLine_HandlesNullAndEmpty()
    {
        Assert.AreEqual(string.Empty, DiagnosticsExportService.AnonymizeLine(null));
        Assert.AreEqual(string.Empty, DiagnosticsExportService.AnonymizeLine(string.Empty));
    }

    [TestMethod]
    public void SummaryHeader_StatesPartialAnonymizationOpenly()
    {
        string summary = DiagnosticsExportService.BuildSummaryText(
            "2.0.0", Array.Empty<ProcessProfile>(), Array.Empty<SmartRule>(),
            _ => null, Array.Empty<RuleDecision>(), "watcher: idle",
            Array.Empty<string>());
        Assert.IsTrue(summary.Contains("PARTIALLY"), summary.Split('\n')[0]);
    }
}

[TestClass]
public sealed class SmartRuleSaveResultTests
{
    private string? _tempRoot;
    private string? _previousOverride;

    [TestInitialize]
    public void Isolate()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pco_saver_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _previousOverride = Environment.GetEnvironmentVariable("PCO_USER_DATA_DIR");
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", _tempRoot);
    }

    [TestCleanup]
    public void Restore()
    {
        Environment.SetEnvironmentVariable("PCO_USER_DATA_DIR", _previousOverride);
        try
        {
            if (_tempRoot != null)
            {
                foreach (var f in Directory.GetFiles(_tempRoot)) File.SetAttributes(f, FileAttributes.Normal);
                File.SetAttributes(_tempRoot, FileAttributes.Normal);
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public void TrySaveRules_SuccessRoundTrips()
    {
        var service = new SmartRuleService();
        var rules = new[] { new SmartRule { ProcessName = "myapp", IsEnabled = true } };

        Assert.IsTrue(service.TrySaveRules(rules, out string? error), error);
        Assert.IsNull(error);
        Assert.AreEqual(1, service.LoadRules().Count);
    }

    [TestMethod]
    public void TrySaveRules_ReadOnlyFile_ReportsFailureWithoutClobbering()
    {
        var service = new SmartRuleService();
        var seed = new[] { new SmartRule { ProcessName = "seed", IsEnabled = true } };
        Assert.IsTrue(service.TrySaveRules(seed, out _), "Seed save must succeed.");

        string target = Path.Combine(_tempRoot!, "smartrules.json");
        string original = File.ReadAllText(target);
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            var rules = new[] { new SmartRule { ProcessName = "myapp", IsEnabled = true } };
            bool ok = service.TrySaveRules(rules, out string? error);

            Assert.IsFalse(ok, "Save over an unwritable file must report failure.");
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.AreEqual(original, File.ReadAllText(target),
                "A failed save must not clobber the previous good file.");
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }
}
