using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

/// <summary>
/// Reliability pass: per-rule/per-instance launch state, full-vs-targeted
/// snapshots, deterministic precedence, controlled retry. Each test targets a
/// concrete finding from the independent review.
/// </summary>
[TestClass]
public sealed class SmartRuleEngineReliabilityTests
{
    private static readonly DateTime T0 = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ProcessInstanceKey Key(int pid, int startSec = 0) =>
        new(pid, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(startSec + pid));

    private static ProcessGroupSnapshot Group(string name, string? path, params ProcessInstanceKey[] keys) =>
        new()
        {
            Name = name,
            FirstProcessId = keys.Length > 0 ? keys[0].ProcessId : 0,
            InstanceCount = keys.Length,
            ExecutablePath = path,
            Instances = new List<ProcessInstanceKey>(keys)
        };

    private static SmartRule Rule(string id, string name, RuleTrigger trigger = RuleTrigger.OnLaunch,
        int delay = 0, int cooldown = 0, int priority = 100, string? path = null) =>
        new()
        {
            Id = id,
            Name = id,
            ProcessName = name,
            ExecutablePath = path,
            Trigger = trigger,
            Action = RuleAction.ApplyProfile,
            DelaySeconds = delay,
            CooldownSeconds = cooldown,
            Priority = priority,
            IsEnabled = true,
            UpdatedAt = T0
        };

    // P0-2 + P0-4: two OnLaunch rules for one instance are BOTH evaluated
    // (independent per-rule state), but exactly one wins the instance.
    [TestMethod]
    public void TwoLaunchRules_SameInstance_BothEvaluatedOneWins()
    {
        var engine = new SmartRuleEngine();
        var key = Key(111);
        var groups = new[] { Group("myapp", null, key) };
        var rules = new[] { Rule("ra", "myapp"), Rule("rb", "myapp") };

        var d = engine.Evaluate(groups, null, T0, rules, automationPaused: false);
        Assert.AreEqual(2, d.Count, "Each rule must be evaluated independently.");
        Assert.AreEqual(1, d.Count(x => x.Ready), "Exactly one rule may win per instance.");
        var loser = d.Single(x => !x.Ready);
        Assert.IsTrue(loser.Superseded);
        Assert.IsTrue(loser.Reason.Contains(d.Single(x => x.Ready).RuleId));
    }

    // P0-2: independent delays per rule.
    [TestMethod]
    public void LaunchRules_IndependentDelays()
    {
        var engine = new SmartRuleEngine();
        var key = Key(112);
        var groups = new[] { Group("myapp", null, key) };
        var rules = new[] { Rule("ra", "myapp", delay: 0), Rule("rb", "myapp", delay: 10) };

        var tick1 = engine.Evaluate(groups, null, T0, rules, automationPaused: false);
        Assert.IsTrue(tick1.Single(x => x.RuleId == "ra").Ready);
        Assert.IsFalse(tick1.Single(x => x.RuleId == "rb").Ready);

        var tick2 = engine.Evaluate(groups, null, T0.AddSeconds(11), rules, automationPaused: false);
        Assert.IsTrue(tick2.Single(x => x.RuleId == "rb").Ready, "Delayed rule must fire after its own delay.");
    }

    // P0-2: cooldown of one rule must not suppress another rule or instance.
    [TestMethod]
    public void Cooldowns_ArePerRule()
    {
        var engine = new SmartRuleEngine();
        var key1 = Key(113);
        var key2 = Key(114);
        var rules = new[] { Rule("ra", "myapp", cooldown: 600), Rule("rb", "myapp") };

        engine.Evaluate(new[] { Group("myapp", null, key1) }, null, T0, rules, automationPaused: false);
        engine.MarkApplied("ra", key1, T0);

        // New instance arrives; old one stays. A's cooldown is bound to (ra, key1):
        // it must leak neither to rule B nor to the fresh instance. Both rules are
        // evaluated for key2, but precedence leaves a single winner.
        var d = engine.Evaluate(new[] { Group("myapp", null, key1, key2) }, null, T0.AddSeconds(1), rules, automationPaused: false);
        var forKey2 = d.Where(x => x.Key.Equals(key2)).ToList();
        Assert.AreEqual(2, forKey2.Count, "Both rules must evaluate the fresh instance.");
        Assert.AreEqual(1, forKey2.Count(x => x.Ready));
        Assert.AreEqual("ra", forKey2.Single(x => x.Ready).RuleId, "Id tiebreak: ra wins.");
        Assert.IsTrue(d.Where(x => x.Key.Equals(key1)).All(x => !x.Ready));
    }

    // P0-2: deleting a rule drops its pending state.
    [TestMethod]
    public void DeletedRule_StatePruned()
    {
        var engine = new SmartRuleEngine();
        var key = Key(114);
        var groups = new[] { Group("myapp", null, key) };
        var delayed = Rule("ra", "myapp", delay: 60);

        engine.Evaluate(groups, null, T0, new[] { delayed }, automationPaused: false);
        // Rule deleted: pending state must go, nothing fires for other instances either.
        var d = engine.Evaluate(new[] { Group("other", null, Key(998)) }, null, T0.AddSeconds(1),
            Array.Empty<SmartRule>(), automationPaused: false);
        Assert.AreEqual(0, d.Count);

        // Re-adding the same rule id treats the still-running instance as new.
        var d2 = engine.Evaluate(groups, null, T0.AddSeconds(2), new[] { delayed }, automationPaused: false);
        Assert.AreEqual(1, d2.Count);
        Assert.IsFalse(d2[0].Ready, "Re-added delayed rule pends again from scratch.");
    }

    // P0-2: restart (new start time) fires again; PID reuse is not confused.
    [TestMethod]
    public void RestartAndPidReuse_FireAsNewInstances()
    {
        var engine = new SmartRuleEngine();
        var rules = new[] { Rule("ra", "myapp") };

        engine.Evaluate(new[] { Group("myapp", null, Key(115, startSec: 0)) }, null, T0, rules, automationPaused: false);
        var again = engine.Evaluate(new[] { Group("myapp", null, Key(115, startSec: 0)) }, null, T0.AddSeconds(2), rules, automationPaused: false);
        Assert.AreEqual(0, again.Count, "Same instance must not refire.");

        var restart = engine.Evaluate(new[] { Group("myapp", null, Key(115, startSec: 3600)) }, null, T0.AddSeconds(4), rules, automationPaused: false);
        Assert.AreEqual(1, restart.Count);
        Assert.IsTrue(restart[0].Ready, "PID reuse with a new start time is a new instance.");
    }

    // P0-2: pause observes; resume does not refire.
    [TestMethod]
    public void PauseObserves_ResumeDoesNotRefire()
    {
        var engine = new SmartRuleEngine();
        var key = Key(116);
        var groups = new[] { Group("myapp", null, key) };
        var rules = new[] { Rule("ra", "myapp") };

        var paused = engine.Evaluate(groups, null, T0, rules, automationPaused: true);
        Assert.AreEqual(0, paused.Count);
        var resumed = engine.Evaluate(groups, null, T0.AddSeconds(5), rules, automationPaused: false);
        Assert.AreEqual(0, resumed.Count, "Instance observed during pause must not refire after resume.");
    }

    // P0-3: a targeted snapshot must not prune state built from a full one.
    [TestMethod]
    public void TargetedScan_DoesNotPrunePendingOrSeen()
    {
        var engine = new SmartRuleEngine();
        var key = Key(117);
        var full = new[] { Group("myapp", null, key) };
        var targetedOther = new[] { Group("other", null, Key(997)) };
        var rules = new[] { Rule("ra", "myapp", delay: 10) };

        var t1 = engine.Evaluate(full, null, T0, rules, automationPaused: false, RuleScanKind.Full);
        Assert.IsFalse(t1.Single().Ready, "Pending.");

        var t2 = engine.Evaluate(targetedOther, null, T0.AddSeconds(5), rules, automationPaused: false, RuleScanKind.Targeted);
        Assert.AreEqual(0, t2.Count, "Targeted scan without the instance changes nothing.");

        var t3 = engine.Evaluate(full, null, T0.AddSeconds(11), rules, automationPaused: false, RuleScanKind.Full);
        Assert.IsTrue(t3.Single().Ready, "Pending delay survives unrelated targeted snapshots.");
    }

    // P0-3: foreground stability survives targeted scans missing the fg process.
    [TestMethod]
    public void TargetedScan_PreservesForegroundStability()
    {
        var engine = new SmartRuleEngine();
        var key = Key(118);
        var full = new[] { Group("myapp", null, key) };
        var targetedOther = new[] { Group("other", null, Key(996)) };
        var rules = new[] { Rule("ra", "myapp", RuleTrigger.OnForeground, delay: 5) };

        engine.Evaluate(full, 118, T0, rules, automationPaused: false, RuleScanKind.Full);
        engine.Evaluate(targetedOther, 118, T0.AddSeconds(3), rules, automationPaused: false, RuleScanKind.Targeted);
        var d = engine.Evaluate(full, 118, T0.AddSeconds(6), rules, automationPaused: false, RuleScanKind.Full);
        Assert.IsTrue(d.Single().Ready, "Stability clock must not reset on partial snapshots.");
    }

    // P0-4: exactly one winner per instance; losers name the winner.
    [TestMethod]
    public void ThreeRulesOneInstance_SingleWinnerByPrecedence()
    {
        var engine = new SmartRuleEngine();
        var key = Key(119);
        var groups = new[] { Group("myapp", @"C:\A\myapp.exe", key) };
        var a = Rule("ra", "myapp", priority: 100);
        var b = Rule("rb", "myapp", priority: 50);
        var c = Rule("rc", "myapp", priority: 50, path: @"C:\A\myapp.exe");
        c.UpdatedAt = T0.AddDays(-1); // older, but path-bound outranks recency

        var d = engine.Evaluate(groups, null, T0, new[] { a, b, c }, automationPaused: false);
        var ready = d.Where(x => x.Ready).ToList();
        Assert.AreEqual(1, ready.Count, "Exactly one rule may win per instance.");
        Assert.AreEqual("rc", ready[0].RuleId, "Path-bound wins the priority tie.");
        Assert.IsTrue(d.Where(x => !x.Ready).All(x => x.Reason.Contains("rc")),
            "Losers must name the winner (explainability == engine choice).");
    }

    // P0-4: newest wins the remaining tie.
    [TestMethod]
    public void PriorityTie_BrokenByNewestThenId()
    {
        var engine = new SmartRuleEngine();
        var key = Key(120);
        var groups = new[] { Group("myapp", null, key) };
        var old = Rule("ra", "myapp", priority: 50);
        old.UpdatedAt = T0.AddDays(-2);
        var fresh = Rule("rb", "myapp", priority: 50);
        fresh.UpdatedAt = T0.AddDays(-1);

        var d = engine.Evaluate(groups, null, T0, new[] { old, fresh }, automationPaused: false);
        Assert.AreEqual("rb", d.Single(x => x.Ready).RuleId);
    }

    // P0-5: permanent failure suppresses the same key without hot-loop decisions.
    [TestMethod]
    public void PermanentFailure_SuppressesSameKeySilently()
    {
        var engine = new SmartRuleEngine();
        var key = Key(121);
        var groups = new[] { Group("myapp", null, key) };
        var rules = new[] { Rule("ra", "myapp") };

        engine.Evaluate(groups, null, T0, rules, automationPaused: false);
        engine.ReportAttempt("ra", key, T0.AddSeconds(1), RuleAttemptOutcome.FailedPermanent);

        // Instance disappears and returns: must NOT refire (permanent for this key).
        engine.Evaluate(Array.Empty<ProcessGroupSnapshot>(), null, T0.AddSeconds(2), rules, automationPaused: false, RuleScanKind.Full);
        for (int i = 0; i < 3; i++)
        {
            var d = engine.Evaluate(groups, null, T0.AddSeconds(3 + i), rules, automationPaused: false, RuleScanKind.Full);
            Assert.AreEqual(0, d.Count(x => x.Ready), "Permanent failure must not hot-loop.");
        }
    }

    // P0-5: retryable failure refires after backoff, not immediately.
    [TestMethod]
    public void RetryableFailure_RefiresAfterBackoffOnly()
    {
        var engine = new SmartRuleEngine();
        var key = Key(122);
        var groups = new[] { Group("myapp", null, key) };
        var rules = new[] { Rule("ra", "myapp", cooldown: 60) };

        engine.Evaluate(groups, null, T0, rules, automationPaused: false);
        engine.ReportAttempt("ra", key, T0.AddSeconds(1), RuleAttemptOutcome.FailedRetryable);

        var early = engine.Evaluate(groups, null, T0.AddSeconds(5), rules, automationPaused: false, RuleScanKind.Full);
        Assert.AreEqual(0, early.Count(x => x.Ready), "Retry must wait out the backoff.");

        var late = engine.Evaluate(groups, null, T0.AddSeconds(61), rules, automationPaused: false, RuleScanKind.Full);
        Assert.AreEqual(1, late.Count(x => x.Ready), "Retryable failure refires after backoff.");
    }
}
