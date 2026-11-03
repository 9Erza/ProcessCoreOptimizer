using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Run.Text defaults to TwoWay. Binding it to a read-only CLR property throws
/// InvalidOperationException at attach time and kills the surrounding panel
/// (found live: details panel stayed collapsed on every selection because of
/// StartTimeLocalText; same trap in rule-activity feeds via Key.ProcessId).
/// Every Run data-binding must be explicit Mode=OneWay.
/// </summary>
[TestClass]
public sealed class XamlBindingModeTests
{
    [TestMethod]
    public void AllRunDataBindings_AreExplicitOneWay()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "version.txt")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Repo root not found.");
        string views = Path.Combine(dir!.FullName, "ProcessCoreOptimizer", "Views");

        var offenders = Directory.GetFiles(views, "*.xaml")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"<Run\b[^>]*>")
                .Select(m => (File: Path.GetFileName(f), Tag: m.Value)))
            .Where(x => x.Tag.Contains("{Binding") && !x.Tag.Contains("DynamicResource")
                && !Regex.IsMatch(x.Tag, @"Mode\s*=\s*OneWay"))
            .Select(x => $"{x.File}: {x.Tag.Trim()}")
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "Run.Text data bindings without Mode=OneWay (TwoWay crashes on read-only sources):\n"
            + string.Join("\n", offenders));
    }
}
