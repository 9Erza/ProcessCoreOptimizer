using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class LocalizationParityTests
{
    private static HashSet<string> ExtractKeys(string path)
    {
        string content = File.ReadAllText(path);
        return new HashSet<string>(
            Regex.Matches(content, @"x:Key=""([^""]+)""").Select(m => m.Groups[1].Value));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "version.txt")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "Repo root with version.txt not found.");
        return dir!.FullName;
    }

    [TestMethod]
    public void EnglishAndPolishDictionaries_HaveSameKeys()
    {
        string root = FindRepoRoot();
        var en = ExtractKeys(Path.Combine(root, "ProcessCoreOptimizer", "Resources", "Strings.en.xaml"));
        var pl = ExtractKeys(Path.Combine(root, "ProcessCoreOptimizer", "Resources", "Strings.pl.xaml"));

        Assert.IsTrue(en.Count > 50, "EN dictionary looks truncated.");
        CollectionAssert.AreEquivalent(en.ToList(), pl.ToList(),
            "Missing/extra keys: EN-only: " + string.Join(",", en.Except(pl))
            + " | PL-only: " + string.Join(",", pl.Except(en)));
    }

    [TestMethod]
    public void NewNavigationKeys_ExistInBothLanguages()
    {
        string root = FindRepoRoot();
        var en = ExtractKeys(Path.Combine(root, "ProcessCoreOptimizer", "Resources", "Strings.en.xaml"));
        foreach (string key in new[]
        {
            "StrNavCpuTopology", "StrDiagnostics", "StrProcessFilter", "StrProfileFilter",
            "StrProcessDetails", "StrNoSelection", "StrCorePresets", "StrTopologyNote",
            "StrConflictsTitle", "StrNoConflicts", "StrRecentOps", "StrWatcherTitle",
            "StrCaptureLive", "StrDuplicateProfile", "StrExportProfiles", "StrImportProfiles",
            "StrColStatus", "StrUnknown", "StrApplyPreset"
        })
        {
            Assert.IsTrue(en.Contains(key), $"Missing EN key: {key}");
        }
    }
}
