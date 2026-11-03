using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class SmartRuleEngineTests
{
    private static ProcessGroupSnapshot Group(string name, params ProcessInstanceKey[] keys)
    {
        return new ProcessGroupSnapshot
        {
            Name = name,
            FirstProcessId = keys.Length > 0 ? keys[0].ProcessId : 0,
            InstanceCount = keys.Length,
            Instances = new List<ProcessInstanceKey>(keys)
        };
    }

    private static SmartRule Rule(string name, RuleTrigger trigger = RuleTrigger.OnLaunch, int delay = 0, int cooldown = 0)
    {
        return new SmartRule
        {
            ProcessName = name,
            Trigger = trigger,
            Action = RuleAction.ApplyProfile,
            DelaySeconds = delay,
            CooldownSeconds = cooldown,
            Priority = 100,
            IsEnabled = true
        };
    }

    [TestMethod]
    public void LaunchRule_FiresOncePerInstance()
    {
        var engine = new SmartRuleEngine();
        var key = new ProcessInstanceKey(111, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var groups = new[] { Group("myapp", key) };
        var rules = new[] { Rule("myapp") };
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var first = engine.Evaluate(groups, null, now, rules, automationPaused: false);
        Assert.AreEqual(1, first.Count);
        Assert.IsTrue(first[0].Ready);
        Assert.IsTrue(first[0].Reason.Contains("launch"));

        // Second tick: already seen, no delay pending → silent (no duplicates).
        var second = engine.Evaluate(groups, null, now.AddSeconds(2), rules, automationPaused: false);
        Assert.AreEqual(0, second.Count);
    }

    [TestMethod]
    public void LaunchRule_WithDelay_PendsThenFires()
    {
        var engine = new SmartRuleEngine();
        var key = new ProcessInstanceKey(112, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var groups = new[] { Group("myapp", key) };
        var rules = new[] { Rule("myapp", delay: 10) };
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var pending = engine.Evaluate(groups, null, now, rules, automationPaused: false);
        Assert.AreEqual(1, pending.Count);
        Assert.IsFalse(pending[0].Ready);

        var fired = engine.Evaluate(groups, null, now.AddSeconds(11), rules, automationPaused: false);
        Assert.AreEqual(1, fired.Count);
        Assert.IsTrue(fired[0].Ready);
        Assert.IsTrue(fired[0].Reason.Contains("elapsed"));
    }

    [TestMethod]
    public void ForegroundRule_RequiresStability()
    {
        var engine = new SmartRuleEngine();
        var key = new ProcessInstanceKey(113, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var groups = new[] { Group("myapp", key) };
        var rules = new[] { Rule("myapp", RuleTrigger.OnForeground, delay: 3) };
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var stabilizing = engine.Evaluate(groups, 113, now, rules, automationPaused: false);
        Assert.AreEqual(1, stabilizing.Count);
        Assert.IsFalse(stabilizing[0].Ready);

        var stable = engine.Evaluate(groups, 113, now.AddSeconds(4), rules, automationPaused: false);
        Assert.AreEqual(1, stable.Count);
        Assert.IsTrue(stable[0].Ready);
        Assert.IsTrue(stable[0].Reason.Contains("stable"));
    }

    [TestMethod]
    public void ForegroundRule_AltTab_ResetsStability()
    {
        var engine = new SmartRuleEngine();
        var key = new ProcessInstanceKey(114, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var groups = new[] { Group("myapp", key) };
        var rules = new[] { Rule("myapp", RuleTrigger.OnForeground, delay: 3) };
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        engine.Evaluate(groups, 114, now, rules, automationPaused: false);
        // Fast Alt+Tab away and back: stability clock restarts.
        var back = engine.Evaluate(groups, 114, now.AddSeconds(10), rules, automationPaused: false);
        var away = engine.Evaluate(groups, 999, now.AddSeconds(11), rules, automationPaused: false);
        Assert.AreEqual(0, away.Count);
        var reenter = engine.Evaluate(groups, 114, now.AddSeconds(12), rules, automationPaused: false);
        Assert.AreEqual(1, reenter.Count);
        Assert.IsFalse(reenter[0].Ready);
    }

    [TestMethod]
    public void Cooldown_SuppressesReapply()
    {
        var engine = new SmartRuleEngine();
        var key = new ProcessInstanceKey(115, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var rules = new[] { Rule("myapp", cooldown: 60) };
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var first = engine.Evaluate(new[] { Group("myapp", key) }, null, now, rules, automationPaused: false);
        Assert.IsTrue(first[0].Ready);
        engine.MarkApplied(rules[0].Id, key, now);

        // Instance briefly disappears (seen-set pruned), then reappears:
        // cooldown recorded by (rule, pid, start) suppresses the re-apply.
        engine.Evaluate(Array.Empty<ProcessGroupSnapshot>(), null, now.AddSeconds(5), rules, automationPaused: false);
        var again = engine.Evaluate(new[] { Group("myapp", key) }, null, now.AddSeconds(6), rules, automationPaused: false);
        Assert.AreEqual(1, again.Count);
        Assert.IsFalse(again[0].Ready);
        Assert.IsTrue(again[0].Reason.Contains("cooldown"));

        // After the cooldown window the same instance may fire again.
        var later = engine.Evaluate(new[] { Group("myapp", key) }, null, now.AddSeconds(61), rules, automationPaused: false);
        Assert.AreEqual(1, later.Count);
        Assert.IsTrue(later[0].Ready);
    }

    [TestMethod]
    public void PausedAutomation_ProducesNothing()
    {
        var engine = new SmartRuleEngine();
        var key = new ProcessInstanceKey(116, DateTime.UtcNow);
        var d = engine.Evaluate(new[] { Group("myapp", key) }, null, DateTime.UtcNow, new[] { Rule("myapp") }, automationPaused: true);
        Assert.AreEqual(0, d.Count);
        Assert.AreEqual("paused", engine.LastSummary);
    }

    [TestMethod]
    public void Override_SuppressesRuleWithReason()
    {
        var engine = new SmartRuleEngine();
        var rule = Rule("myapp");
        var key = new ProcessInstanceKey(117, DateTime.UtcNow);
        var now = DateTime.UtcNow;
        engine.OverrideRule(rule.Id, now.AddMinutes(5));

        var d = engine.Evaluate(new[] { Group("myapp", key) }, null, now, new[] { rule }, automationPaused: false);
        Assert.AreEqual(1, d.Count);
        Assert.IsFalse(d[0].Ready);
        Assert.IsTrue(d[0].Reason.Contains("overridden"));

        engine.ClearOverride(rule.Id);
        var d2 = engine.Evaluate(new[] { Group("myapp", key) }, null, now, new[] { rule }, automationPaused: false);
        Assert.IsTrue(d2[0].Ready);
    }

    [TestMethod]
    public void PathBoundRule_IgnoresOtherPaths()
    {
        var engine = new SmartRuleEngine();
        var rule = Rule("myapp");
        rule.ExecutablePath = @"C:\A\myapp.exe";
        var now = DateTime.UtcNow;
        var groups = new[]
        {
            new ProcessGroupSnapshot
            {
                Name = "myapp",
                FirstProcessId = 118,
                InstanceCount = 1,
                ExecutablePath = @"C:\B\myapp.exe",
                Instances = new List<ProcessInstanceKey> { new ProcessInstanceKey(118, now) }
            }
        };

        var d = engine.Evaluate(groups, null, now, new[] { rule }, automationPaused: false);
        Assert.AreEqual(0, d.Count);
    }
}
