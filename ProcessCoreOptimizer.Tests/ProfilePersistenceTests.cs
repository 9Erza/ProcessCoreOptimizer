using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ProfilePersistenceTests
{
    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), $"pco_test_{name}_{Guid.NewGuid():N}.json");

    [TestMethod]
    public void Sanitize_PriorityOnlyRoundTrip_PreservesProfile()
    {
        var profile = new ProcessProfile
        {
            ProcessName = "myapp",
            AffinityMask = 0,
            Priority = "High",
            ApplyPriority = true,
            ApplyCoreOptimization = false,
            IsEnabled = true
        };
        var sanitized = ProfileService.SanitizeProfiles(new[] { profile }).ToList();
        Assert.AreEqual(1, sanitized.Count);

        string json = JsonSerializer.Serialize(sanitized);
        var reloaded = JsonSerializer.Deserialize<List<ProcessProfile>>(json)!;
        var resanitized = ProfileService.SanitizeProfiles(reloaded).ToList();
        Assert.AreEqual(1, resanitized.Count);
        Assert.AreEqual("High", resanitized[0].Priority);
        Assert.IsFalse(resanitized[0].ApplyCoreOptimization);
    }

    [TestMethod]
    public void CorruptLiveJson_FallsBackToBackup()
    {
        string live = TempFile("profiles");
        try
        {
            File.WriteAllText(live, "{\"truncated\": ");
            File.WriteAllText(live + ".bak", "[]");
            string? recovered = AtomicFileService.ReadValidatedWithBackup(live, content =>
            {
                if (string.IsNullOrWhiteSpace(content)) return true;
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    return true;
                }
                catch { return false; }
            });
            Assert.AreEqual("[]", recovered);
        }
        finally
        {
            try { File.Delete(live); } catch { }
            try { File.Delete(live + ".bak"); } catch { }
            try { File.Delete(live + ".tmp"); } catch { }
        }
    }

    [TestMethod]
    public void AtomicWrite_CreatesBackup_AndKeepsValidJson()
    {
        string live = TempFile("atomic");
        try
        {
            AtomicFileService.WriteAllTextAtomic(live, "{\"v\":1}");
            AtomicFileService.WriteAllTextAtomic(live, "{\"v\":2}");
            Assert.IsTrue(File.Exists(live + ".bak"));
            string content = File.ReadAllText(live);
            using var doc = JsonDocument.Parse(content);
            Assert.AreEqual(2, doc.RootElement.GetProperty("v").GetInt32());
        }
        finally
        {
            try { File.Delete(live); } catch { }
            try { File.Delete(live + ".bak"); } catch { }
            try { File.Delete(live + ".tmp"); } catch { }
        }
    }

    [TestMethod]
    public void PriorityNormalize_UnknownBecomesNormal_RealTimeGated()
    {
        Assert.AreEqual("Normal", PriorityService.Normalize("nonsense", allowRealtime: true));
        Assert.AreEqual("High", PriorityService.Normalize("RealTime", allowRealtime: false));
        Assert.AreEqual("RealTime", PriorityService.Normalize("RealTime", allowRealtime: true));
        Assert.AreEqual("BelowNormal", PriorityService.Normalize("Poniżej Normalnego", allowRealtime: true));
    }

    [TestMethod]
    public void ExecutablePath_Normalization_IsStable()
    {
        string? a = ProfileService.NormalizeExecutablePath(@"C:\Apps\..\Apps\app.exe");
        string? b = ProfileService.NormalizeExecutablePath(@"c:\apps\app.exe");
        Assert.IsNotNull(a);
        Assert.IsNotNull(b);
        Assert.IsTrue(a.Equals(b, StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(ProfileService.NormalizeExecutablePath("   "));
    }
}
