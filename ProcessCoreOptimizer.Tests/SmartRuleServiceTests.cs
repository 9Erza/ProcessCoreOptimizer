using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class SmartRuleServiceTests
{
    private static SmartRule Rule(string name, string? path = null, bool enabled = true)
    {
        return new SmartRule
        {
            ProcessName = name,
            ExecutablePath = path,
            IsEnabled = enabled,
            DelaySeconds = 500,
            CooldownSeconds = 9999,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    [TestMethod]
    public void Sanitize_ClampsAndNormalizes()
    {
        var rules = new[] { Rule("MyApp.EXE", "  ") };
        var sane = SmartRuleService.SanitizeRules(rules).ToList();
        Assert.AreEqual(1, sane.Count);
        Assert.AreEqual("MyApp", sane[0].ProcessName);
        Assert.IsNull(sane[0].ExecutablePath);
        Assert.AreEqual(300, sane[0].DelaySeconds);
        Assert.AreEqual(3600, sane[0].CooldownSeconds);
    }

    [TestMethod]
    public void Sanitize_DropsEmptyNames()
    {
        var sane = SmartRuleService.SanitizeRules(new[] { Rule("   "), Rule("ok") }).ToList();
        Assert.AreEqual(1, sane.Count);
    }

    [TestMethod]
    public void FindAndRemove_ById()
    {
        var first = Rule("a");
        var second = Rule("a", @"C:\x.exe");
        var rules = new List<SmartRule> { first, second };

        Assert.AreSame(second, SmartRuleService.FindById(rules, second.Id));

        var rest = SmartRuleService.RemoveById(rules, first.Id);
        Assert.AreEqual(1, rest.Count);
        Assert.AreEqual(second.Id, rest[0].Id);

        Assert.AreEqual(2, SmartRuleService.RemoveById(rules, null).Count);
        Assert.IsNull(SmartRuleService.FindById(rules, "missing"));
    }
}
