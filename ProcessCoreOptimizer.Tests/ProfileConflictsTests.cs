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
public sealed class ProfileConflictsTests
{
    private static ProcessProfile Make(string name, long mask, string? path = null, bool enabled = true, DateTime? updated = null) => new()
    {
        ProcessName = name,
        AffinityMask = mask,
        ApplyPriority = true,
        ApplyCoreOptimization = true,
        ExecutablePath = path,
        Priority = "Normal",
        IsEnabled = enabled,
        UpdatedAt = updated ?? DateTime.UtcNow
    };

    [TestMethod]
    public void SameNameDifferentMasks_IsConflict_NewestWins()
    {
        var old = Make("game", 0x1, updated: DateTime.UtcNow.AddHours(-1));
        var cur = Make("game", 0x3, updated: DateTime.UtcNow);
        var conflicts = ProfileConflicts.FindConflicts(new[] { old, cur });
        Assert.AreEqual(1, conflicts.Count);
        Assert.AreEqual("game", conflicts[0].ProcessName);
        Assert.AreEqual(0x3, conflicts[0].Winner!.AffinityMask);
        Assert.IsFalse(string.IsNullOrWhiteSpace(conflicts[0].Reason));
    }

    [TestMethod]
    public void PathBoundVsNameOnly_IsConflict_PathBoundWins()
    {
        var nameOnly = Make("app", 0x1, updated: DateTime.UtcNow);
        var pathBound = Make("app", 0x2, @"C:\Apps\app.exe", updated: DateTime.UtcNow.AddHours(-1));
        var conflicts = ProfileConflicts.FindConflicts(new[] { nameOnly, pathBound });
        Assert.AreEqual(1, conflicts.Count);
        Assert.AreEqual(@"C:\Apps\app.exe", conflicts[0].Winner!.ExecutablePath);
    }

    [TestMethod]
    public void IdenticalSettings_NoConflict()
    {
        var a = Make("game", 0x1);
        var b = Make("game", 0x1);
        Assert.AreEqual(0, ProfileConflicts.FindConflicts(new[] { a, b }).Count);
    }

    [TestMethod]
    public void DisabledProfiles_Ignored()
    {
        var a = Make("game", 0x1);
        var b = Make("game", 0x2, enabled: false);
        Assert.AreEqual(0, ProfileConflicts.FindConflicts(new[] { a, b }).Count);
    }

    [TestMethod]
    public void DifferentNames_NoConflict()
    {
        Assert.AreEqual(0, ProfileConflicts.FindConflicts(new[] { Make("a", 0x1), Make("b", 0x2) }).Count);
    }

    [TestMethod]
    public void Duplicate_GetsFreshIdentity_DisabledCopy()
    {
        var src = Make("game", 0x3, @"C:\G\game.exe");
        var copy = ProfileService.DuplicateProfile(src);
        Assert.AreNotEqual(src.Id, copy.Id);
        Assert.AreEqual(src.ProcessName, copy.ProcessName);
        Assert.AreEqual(src.AffinityMask, copy.AffinityMask);
        Assert.AreEqual(src.ExecutablePath, copy.ExecutablePath);
        Assert.IsFalse(copy.IsEnabled, "Duplicates start disabled to avoid surprise auto-apply.");
        Assert.IsTrue(copy.DisplayName!.Contains("copy", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ExportImport_RoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"pco_imp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "profiles.json");
            var profiles = new[] { Make("alpha", 0x1), Make("beta", 0x2) };
            ProfileService.ExportProfiles(file, profiles);
            Assert.IsTrue(File.Exists(file));

            // Read back without touching the live user store.
            string json = File.ReadAllText(file);
            var enumOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            enumOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            var reloaded = JsonSerializer.Deserialize<List<ProcessProfile>>(json, enumOptions)!;
            var resanitized = ProfileService.SanitizeProfiles(reloaded).ToList();
            Assert.AreEqual(2, resanitized.Count);

            // Pure merge: import of an overlapping profile follows precedence, no IO.
            var existing = new[] { Make("alpha", 0x1, updated: DateTime.UtcNow.AddHours(-1)) };
            var incoming = new[] { Make("alpha", 0x7, updated: DateTime.UtcNow) };
            var merged = ProfileService.MergeImported(existing, incoming);
            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(0x7, merged[0].AffinityMask);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [TestMethod]
    public void Import_InvalidJson_ReportsError_NoThrow()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"pco_imp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "bad.json");
            File.WriteAllText(file, "{ not json");
            var svc = new ProfileService();
            var result = svc.ImportProfiles(file);
            Assert.AreEqual(0, result.Imported);
            Assert.IsTrue(result.Errors.Count > 0);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [TestMethod]
    public void Import_MissingFile_ReportsError()
    {
        var svc = new ProfileService();
        var result = svc.ImportProfiles(Path.Combine(Path.GetTempPath(), $"pco_missing_{Guid.NewGuid():N}.json"));
        Assert.AreEqual(0, result.Imported);
        Assert.IsTrue(result.Errors.Count > 0);
    }
}
