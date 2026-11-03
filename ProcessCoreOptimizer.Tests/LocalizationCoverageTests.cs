using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class LocalizationCoverageTests
{
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

    /// <summary>
    /// Runtime strings come from LocalizationService.ApplyLanguage (App.xaml does
    /// not merge the Strings dictionaries). Every XAML key must be assigned there,
    /// otherwise the UI renders empty labels with no error. Guards the trap hit
    /// by the 2.0 expansion (Smart Rules button invisible until wired here).
    /// </summary>
    [TestMethod]
    public void EveryXamlKey_IsAssignedInApplyLanguage()
    {
        string root = FindRepoRoot();
        string en = File.ReadAllText(Path.Combine(root, "ProcessCoreOptimizer", "Resources", "Strings.en.xaml"));
        string service = File.ReadAllText(Path.Combine(root, "ProcessCoreOptimizer", "Services", "LocalizationService.cs"));

        var keys = new HashSet<string>(
            Regex.Matches(en, @"x:Key=""([^""]+)""").Select(m => m.Groups[1].Value));
        var assigned = new HashSet<string>(
            Regex.Matches(service, @"res\[""([^""]+)""\]").Select(m => m.Groups[1].Value));

        var missing = keys.Where(k => !assigned.Contains(k)).ToList();
        Assert.AreEqual(0, missing.Count,
            "XAML keys missing a runtime res[...] assignment in LocalizationService.ApplyLanguage: "
            + string.Join(", ", missing));
    }
}
