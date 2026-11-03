using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ProfileMatchExplainerTests
{
    private static ProcessProfile P(string name, string? path = null, bool enabled = true, DateTime? updated = null)
    {
        return new ProcessProfile
        {
            ProcessName = name,
            ExecutablePath = path,
            IsEnabled = enabled,
            UpdatedAt = updated ?? DateTime.UtcNow
        };
    }

    [TestMethod]
    public void NoMatch_ExplainsAbsence()
    {
        string en = ProfileMatchExplainer.Explain("ghost", null, new List<ProcessProfile> { P("other") }, polish: false);
        Assert.IsTrue(en.Contains("No enabled profile"));
        string pl = ProfileMatchExplainer.Explain("ghost", null, new List<ProcessProfile> { P("other") }, polish: true);
        Assert.IsTrue(pl.Contains("Brak pasującego"));
    }

    [TestMethod]
    public void DisabledMatch_IsReported()
    {
        string en = ProfileMatchExplainer.Explain("myapp", null, new List<ProcessProfile> { P("myapp", enabled: false) }, polish: false);
        Assert.IsTrue(en.Contains("disabled"));
    }

    [TestMethod]
    public void SingleMatch_NamesWinner()
    {
        var p = P("myapp");
        string en = ProfileMatchExplainer.Explain("myapp", null, new List<ProcessProfile> { p }, polish: false);
        Assert.IsTrue(en.Contains(p.DisplayName));
        Assert.IsTrue(en.Contains("only matching"));
    }

    [TestMethod]
    public void PathBound_WinsOverNameOnly()
    {
        var nameOnly = P("myapp", updated: DateTime.UtcNow);
        var bound = P("myapp", @"C:\A\myapp.exe", updated: DateTime.UtcNow.AddDays(-1));
        string en = ProfileMatchExplainer.Explain("myapp", @"C:\A\myapp.exe",
            new List<ProcessProfile> { nameOnly, bound }, polish: false);
        Assert.IsTrue(en.Contains("path-bound wins"));
    }

    [TestMethod]
    public void Newest_WinsAmongNameOnly()
    {
        var old = P("myapp", updated: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var fresh = P("myapp", updated: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        string en = ProfileMatchExplainer.Explain("myapp", null,
            new List<ProcessProfile> { old, fresh }, polish: false);
        Assert.IsTrue(en.Contains("newest"));
    }
}
