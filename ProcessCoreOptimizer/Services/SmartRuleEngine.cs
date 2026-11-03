using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// One engine evaluation outcome for a single rule instance.
    /// Ready ones are applied by the caller; unready ones explain why not.
    /// </summary>
    public sealed class RuleDecision
    {
        public string RuleId { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public ProcessInstanceKey Key { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public RuleAction Action { get; set; }
        public bool Ready { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime EvaluatedAtUtc { get; set; }
    }

    /// <summary>
    /// Evaluates automation rules against the scans the app already performs.
    /// No timers, no extra process enumeration: the caller passes the current
    /// group snapshot list (UI scan or watcher scan) plus the foreground PID.
    /// All runtime state is in-memory (pending launches, foreground stability,
    /// cooldowns, overrides, seen keys, recent decisions). Identity is re-verified
    /// at apply time by the existing OS layer; stale keys yield safe skips.
    /// </summary>
    public sealed class SmartRuleEngine
    {
        private readonly HashSet<ProcessInstanceKey> _seen = new();
        private readonly Dictionary<ProcessInstanceKey, DateTime> _pendingLaunch = new();
        private readonly Dictionary<string, DateTime> _lastApplyUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _overridesUtc = new(StringComparer.OrdinalIgnoreCase);
        private ProcessInstanceKey? _foregroundKey;
        private DateTime _foregroundSinceUtc;

        private readonly List<RuleDecision> _recent = new();
        private const int MaxRecent = 50;

        public IReadOnlyList<RuleDecision> RecentDecisions
        {
            get
            {
                lock (_recent) return _recent.ToList();
            }
        }

        public DateTime? LastEvaluateAtUtc { get; private set; }
        public string LastSummary { get; private set; } = string.Empty;

        /// <summary>
        /// Overrides (pauses) one rule until <paramref name="untilUtc"/>. Safe
        /// temporary override: expires on its own, nothing persists.
        /// </summary>
        public void OverrideRule(string ruleId, DateTime untilUtc)
        {
            if (string.IsNullOrWhiteSpace(ruleId)) return;
            lock (_recent) _overridesUtc[ruleId] = untilUtc;
        }

        public void ClearOverride(string ruleId)
        {
            lock (_recent) _overridesUtc.Remove(ruleId);
        }

        public bool IsOverridden(string ruleId, DateTime utcNow)
        {
            lock (_recent)
            {
                return _overridesUtc.TryGetValue(ruleId, out DateTime until) && until > utcNow;
            }
        }

        /// <summary>
        /// Drops all runtime state (seen launches, pending delays, cooldowns,
        /// overrides, recent decisions). Used by factory reset so wiped rules
        /// leave no ghost state in memory. Thread-safe.
        /// </summary>
        public void Clear()
        {
            lock (_recent)
            {
                _seen.Clear();
                _pendingLaunch.Clear();
                _lastApplyUtc.Clear();
                _overridesUtc.Clear();
                _recent.Clear();
                _foregroundKey = null;
            }

            LastEvaluateAtUtc = null;
            LastSummary = "cleared";
        }

        public IReadOnlyList<RuleDecision> Evaluate(
            IReadOnlyList<ProcessGroupSnapshot> groups,
            int? foregroundPid,
            DateTime utcNow,
            IReadOnlyList<SmartRule> rules,
            bool automationPaused)
        {
            var decisions = new List<RuleDecision>();
            LastEvaluateAtUtc = utcNow;

            var active = new HashSet<ProcessInstanceKey>();
            foreach (var g in groups)
                foreach (var k in g.Instances)
                    active.Add(k);

            lock (_recent)
            {
                _seen.RemoveWhere(k => !active.Contains(k));
                foreach (var dead in _pendingLaunch.Keys.Where(k => !active.Contains(k)).ToList())
                    _pendingLaunch.Remove(dead);
                foreach (var expired in _overridesUtc.Where(kv => kv.Value <= utcNow).Select(kv => kv.Key).ToList())
                    _overridesUtc.Remove(expired);

                if (automationPaused)
                {
                    LastSummary = "paused";
                    return decisions;
                }

                var ordered = rules
                    .Where(r => r != null && r.IsEnabled && !string.IsNullOrWhiteSpace(r.ProcessName))
                    .OrderBy(r => r.Priority)
                    .ThenBy(r => r.UpdatedAt)
                    .ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ProcessInstanceKey? fgKey = null;
                if (foregroundPid.HasValue)
                {
                    foreach (var g in groups)
                    {
                        var hit = g.Instances.FirstOrDefault(k => k.ProcessId == foregroundPid.Value);
                        if (hit.ProcessId != 0) { fgKey = hit; break; }
                    }
                }

                if (fgKey != _foregroundKey)
                {
                    _foregroundKey = fgKey;
                    _foregroundSinceUtc = utcNow;
                }

                foreach (var rule in ordered)
                {
                    if (_overridesUtc.TryGetValue(rule.Id, out DateTime until) && until > utcNow)
                    {
                        Record(decisions, rule, default, false, $"overridden until {until:HH:mm:ss}");
                        continue;
                    }

                    string name = ProfileService.NormalizeProcessName(rule.ProcessName);
                    string? rulePath = ProfileService.NormalizeExecutablePath(rule.ExecutablePath);

                    foreach (var g in groups)
                    {
                        if (!string.Equals(ProfileService.NormalizeProcessName(g.Name), name, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (rulePath != null && !string.Equals(ProfileService.NormalizeExecutablePath(g.ExecutablePath), rulePath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (var key in g.Instances)
                        {
                            EvaluateInstance(decisions, rule, g.Name, key, fgKey, utcNow);
                        }
                    }
                }

                int ready = decisions.Count(d => d.Ready);
                LastSummary = $"{ordered.Count} rule(s), {decisions.Count} match(es), {ready} ready";
            }

            return decisions;
        }

        private void EvaluateInstance(
            List<RuleDecision> decisions,
            SmartRule rule,
            string groupName,
            ProcessInstanceKey key,
            ProcessInstanceKey? fgKey,
            DateTime utcNow)
        {
            if (_lastApplyUtc.TryGetValue(rule.Id + "|" + key.ProcessId + "|" + key.StartTimeUtc.Ticks, out DateTime last)
                && (utcNow - last).TotalSeconds < rule.CooldownSeconds)
            {
                Record(decisions, rule, key, false, $"cooldown ({rule.CooldownSeconds}s)");
                return;
            }

            if (rule.Trigger == RuleTrigger.OnLaunch)
            {
                if (!_seen.Contains(key))
                {
                    _seen.Add(key);
                    if (rule.DelaySeconds > 0)
                    {
                        _pendingLaunch[key] = utcNow.AddSeconds(rule.DelaySeconds);
                        Record(decisions, rule, key, false, $"launch detected, applies in {rule.DelaySeconds}s");
                        return;
                    }

                    Record(decisions, rule, key, true, "launch detected, no delay");
                    return;
                }

                if (_pendingLaunch.TryGetValue(key, out DateTime due))
                {
                    if (utcNow >= due)
                    {
                        _pendingLaunch.Remove(key);
                        Record(decisions, rule, key, true, "launch delay elapsed");
                    }
                    else
                    {
                        Record(decisions, rule, key, false, $"launch pending ({(due - utcNow).TotalSeconds:F0}s left)");
                    }

                    return;
                }

                return;
            }

            // OnForeground: the foreground PID must be stable for DelaySeconds
            // (debounce against fast Alt+Tab). DelaySeconds 0 still requires
            // the current tick to actually be the foreground instance.
            if (fgKey == null || fgKey.Value.ProcessId == 0 || !fgKey.Value.Equals(key))
                return;

            double stable = (utcNow - _foregroundSinceUtc).TotalSeconds;
            int need = Math.Max(rule.DelaySeconds, 0);
            if (stable >= need)
                Record(decisions, rule, key, true, $"foreground stable {stable:F0}s");
            else
                Record(decisions, rule, key, false, $"foreground stabilizing ({stable:F0}/{need}s)");
        }

        public void MarkApplied(string ruleId, ProcessInstanceKey key, DateTime utcNow)
        {
            lock (_recent)
            {
                _lastApplyUtc[ruleId + "|" + key.ProcessId + "|" + key.StartTimeUtc.Ticks] = utcNow;
            }
        }

        private void Record(List<RuleDecision> decisions, SmartRule rule, ProcessInstanceKey key, bool ready, string reason)
        {
            var d = new RuleDecision
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                Key = key,
                ProcessName = rule.ProcessName,
                Action = rule.Action,
                Ready = ready,
                Reason = reason,
                EvaluatedAtUtc = DateTime.UtcNow
            };
            decisions.Add(d);
            _recent.Add(d);
            while (_recent.Count > MaxRecent) _recent.RemoveAt(0);
        }
    }
}
