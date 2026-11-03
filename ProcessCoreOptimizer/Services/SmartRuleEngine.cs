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
        /// <summary>True when a higher-precedence rule won this instance (P0-4).</summary>
        public bool Superseded { get; set; }
    }

    /// <summary>Whether the evaluated snapshot covers all user processes or a subset.</summary>
    public enum RuleScanKind
    {
        /// <summary>Full user-process scan: absence means the instance exited.</summary>
        Full,
        /// <summary>
        /// Subset scan (profile/rule targets only): absence proves nothing,
        /// so presence-based state is retained, never pruned.
        /// </summary>
        Targeted
    }

    /// <summary>Post-apply outcome reported back by the caller (P0-5).</summary>
    public enum RuleAttemptOutcome
    {
        Applied,
        FailedRetryable,
        FailedPermanent
    }

    /// <summary>
    /// Evaluates automation rules against the scans the app already performs.
    /// No timers, no extra process enumeration: the caller passes the current
    /// group snapshot list (UI scan or watcher scan) plus the foreground PID.
    ///
    /// Reliability contract (post-expansion review):
    /// - OnLaunch state is keyed by (RuleId, ProcessInstanceKey): rules never
    ///   shadow each other and delays/cooldowns are independent per rule.
    /// - Targeted snapshots never prune presence state (P0-3); only Full scans do.
    /// - Exactly one rule wins per instance (P0-4): precedence is Priority asc,
    ///   then path-bound, then newest UpdatedAt, then Id. The engine deliberately
    ///   never combines actions from different rules on one instance.
    /// - Attempts are tracked separately from applications (P0-5): permanent
    ///   failures suppress the same key silently (no hot loop); retryable ones
    ///   refire after a backoff; only real success starts a cooldown.
    /// - Pause observes but emits nothing: instances seen while paused do not
    ///   refire after resume. A brand-new rule id fires for running processes
    ///   (new subscription semantics); this is defined, not accidental.
    ///
    /// Identity is re-verified at apply time by the existing OS layer; stale
    /// keys yield safe skips. All state is in-memory and bounded.
    /// </summary>
    public sealed class SmartRuleEngine
    {
        private readonly HashSet<(string RuleId, ProcessInstanceKey Key)> _seen = new();
        private readonly Dictionary<(string RuleId, ProcessInstanceKey Key), DateTime> _pendingLaunch = new();
        private readonly Dictionary<(string RuleId, ProcessInstanceKey Key), DateTime> _lastApplyUtc = new();
        private readonly Dictionary<(string RuleId, ProcessInstanceKey Key), (DateTime AtUtc, RuleAttemptOutcome Outcome)> _attempts = new();
        private readonly Dictionary<string, DateTime> _overridesUtc = new(StringComparer.OrdinalIgnoreCase);
        private ProcessInstanceKey? _foregroundKey;
        private DateTime _foregroundSinceUtc;

        private readonly List<RuleDecision> _recent = new();
        private const int MaxRecent = 50;
        private const int MaxTrackedEntries = 2000;
        private static readonly TimeSpan StateRetention = TimeSpan.FromHours(1);

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
        /// attempts, overrides, recent decisions). Used by factory reset so wiped
        /// rules leave no ghost state in memory. Thread-safe.
        /// </summary>
        public void Clear()
        {
            lock (_recent)
            {
                _seen.Clear();
                _pendingLaunch.Clear();
                _lastApplyUtc.Clear();
                _attempts.Clear();
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
            return Evaluate(groups, foregroundPid, utcNow, rules, automationPaused, RuleScanKind.Full);
        }

        public IReadOnlyList<RuleDecision> Evaluate(
            IReadOnlyList<ProcessGroupSnapshot> groups,
            int? foregroundPid,
            DateTime utcNow,
            IReadOnlyList<SmartRule> rules,
            bool automationPaused,
            RuleScanKind scanKind)
        {
            var decisions = new List<RuleDecision>();
            LastEvaluateAtUtc = utcNow;

            var active = new HashSet<ProcessInstanceKey>();
            foreach (var g in groups)
                foreach (var k in g.Instances)
                    active.Add(k);

            bool emit = !automationPaused;

            lock (_recent)
            {
                var knownIds = new HashSet<string>(
                    rules.Where(r => r != null).Select(r => r.Id), StringComparer.OrdinalIgnoreCase);

                // Unknown-rule state is always droppable (deleted rules).
                _seen.RemoveWhere(e => !knownIds.Contains(e.RuleId));
                foreach (var dead in _pendingLaunch.Keys.Where(k => !knownIds.Contains(k.RuleId)).ToList())
                    _pendingLaunch.Remove(dead);
                foreach (var dead in _lastApplyUtc.Keys.Where(k => !knownIds.Contains(k.RuleId)).ToList())
                    _lastApplyUtc.Remove(dead);
                foreach (var dead in _attempts.Keys.Where(k => !knownIds.Contains(k.RuleId)).ToList())
                    _attempts.Remove(dead);
                foreach (var expired in _overridesUtc.Where(kv => kv.Value <= utcNow || !knownIds.Contains(kv.Key)).Select(kv => kv.Key).ToList())
                    _overridesUtc.Remove(expired);

                if (scanKind == RuleScanKind.Full)
                {
                    // Presence-based state: absence in a FULL scan means exit.
                    // Time-based state (_lastApplyUtc, _attempts) expires on age,
                    // never on absence, so a flapping instance keeps its cooldown.
                    _seen.RemoveWhere(e => !active.Contains(e.Key));
                    foreach (var dead in _pendingLaunch.Keys.Where(k => !active.Contains(k.Key)).ToList())
                        _pendingLaunch.Remove(dead);

                    DateTime retentionCutoff = utcNow - StateRetention;
                    foreach (var stale in _lastApplyUtc.Where(kv => kv.Value < retentionCutoff).Select(kv => kv.Key).ToList())
                        _lastApplyUtc.Remove(stale);
                    foreach (var stale in _attempts.Where(kv => kv.Value.AtUtc < retentionCutoff).Select(kv => kv.Key).ToList())
                        _attempts.Remove(stale);

                    Cap(_seen, MaxTrackedEntries);
                    CapKeys(_pendingLaunch, MaxTrackedEntries);
                    CapKeys(_lastApplyUtc, MaxTrackedEntries);
                    CapKeys(_attempts, MaxTrackedEntries);
                }

                if (!emit)
                {
                    // Pause observes: still update _seen/_pending/fg below so the
                    // resume does not fake-launch instances that never left.
                    LastSummary = "paused";
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

                // P0-3: only a Full scan (or a found fg key) may move the clock.
                // A targeted scan that misses the fg process keeps prior stability.
                if (fgKey != null || scanKind == RuleScanKind.Full)
                {
                    if (!Nullable.Equals(fgKey, _foregroundKey))
                    {
                        _foregroundKey = fgKey;
                        _foregroundSinceUtc = utcNow;
                    }
                }

                foreach (var rule in ordered)
                {
                    if (_overridesUtc.TryGetValue(rule.Id, out DateTime until) && until > utcNow)
                    {
                        Record(decisions, rule, default, false, $"overridden until {until:HH:mm:ss}", emit);
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
                            EvaluateInstance(decisions, rule, g.Name, key, fgKey ?? _foregroundKey, utcNow, emit);
                        }
                    }
                }

                if (emit)
                {
                    ApplyPrecedence(decisions, ordered);
                    int ready = decisions.Count(d => d.Ready);
                    LastSummary = $"{ordered.Count} rule(s), {decisions.Count} match(es), {ready} ready";
                }
            }

            return decisions;
        }

        private void EvaluateInstance(
            List<RuleDecision> decisions,
            SmartRule rule,
            string groupName,
            ProcessInstanceKey key,
            ProcessInstanceKey? fgKey,
            DateTime utcNow,
            bool emit)
        {
            var id = (rule.Id, key);

            // P0-5: a permanently failed key stays suppressed (silently — the
            // outcome was already logged by the caller at attempt time).
            // A retryable failure refires only after its backoff.
            if (_attempts.TryGetValue(id, out var attempt))
            {
                if (attempt.Outcome == RuleAttemptOutcome.FailedPermanent)
                    return;

                if (attempt.Outcome == RuleAttemptOutcome.FailedRetryable)
                {
                    if ((utcNow - attempt.AtUtc).TotalSeconds < RetryBackoffSeconds(rule))
                    {
                        Record(decisions, rule, key, false, $"retry backoff ({RetryBackoffSeconds(rule)}s)", emit);
                        return;
                    }

                    _attempts.Remove(id);
                }
            }

            if (_lastApplyUtc.TryGetValue(id, out DateTime last)
                && (utcNow - last).TotalSeconds < rule.CooldownSeconds)
            {
                Record(decisions, rule, key, false, $"cooldown ({rule.CooldownSeconds}s)", emit);
                return;
            }

            if (rule.Trigger == RuleTrigger.OnLaunch)
            {
                if (!_seen.Contains(id))
                {
                    _seen.Add(id);
                    if (rule.DelaySeconds > 0)
                    {
                        _pendingLaunch[id] = utcNow.AddSeconds(rule.DelaySeconds);
                        Record(decisions, rule, key, false, $"launch detected, applies in {rule.DelaySeconds}s", emit);
                        return;
                    }

                    Record(decisions, rule, key, true, "launch detected, no delay", emit);
                    return;
                }

                if (_pendingLaunch.TryGetValue(id, out DateTime due))
                {
                    if (utcNow >= due)
                    {
                        _pendingLaunch.Remove(id);
                        Record(decisions, rule, key, true, "launch delay elapsed", emit);
                    }
                    else
                    {
                        Record(decisions, rule, key, false, $"launch pending ({(due - utcNow).TotalSeconds:F0}s left)", emit);
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
                Record(decisions, rule, key, true, $"foreground stable {stable:F0}s", emit);
            else
                Record(decisions, rule, key, false, $"foreground stabilizing ({stable:F0}/{need}s)", emit);
        }

        /// <summary>
        /// P0-4: exactly one rule wins per instance. Precedence: Priority asc,
        /// path-bound first, newest UpdatedAt, then Id. Losers are marked
        /// superseded and name the winner, so the feed matches the engine.
        /// Combining actions from different rules is deliberately unsupported.
        /// </summary>
        private static void ApplyPrecedence(List<RuleDecision> decisions, List<SmartRule> ordered)
        {
            var byRule = ordered.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var group in decisions.Where(d => d.Ready).GroupBy(d => d.Key).Where(g => g.Count() > 1))
            {
                var winner = group
                    .OrderBy(d => byRule.TryGetValue(d.RuleId, out var r) ? r.Priority : int.MaxValue)
                    .ThenByDescending(d => byRule.TryGetValue(d.RuleId, out var r) && !string.IsNullOrWhiteSpace(ProfileService.NormalizeExecutablePath(r.ExecutablePath)))
                    .ThenByDescending(d => byRule.TryGetValue(d.RuleId, out var r) ? r.UpdatedAt : DateTime.MinValue)
                    .ThenBy(d => d.RuleId, StringComparer.OrdinalIgnoreCase)
                    .First();

                foreach (var loser in group.Where(d => d.RuleId != winner.RuleId))
                {
                    loser.Ready = false;
                    loser.Superseded = true;
                    loser.Reason = $"superseded by '{winner.RuleName}' (priority {byRule[winner.RuleId].Priority})";
                }
            }
        }

        public static int RetryBackoffSeconds(SmartRule rule) =>
            Math.Max(rule?.CooldownSeconds ?? 0, 15);

        public void MarkApplied(string ruleId, ProcessInstanceKey key, DateTime utcNow)
        {
            ReportAttempt(ruleId, key, utcNow, RuleAttemptOutcome.Applied);
        }

        /// <summary>
        /// P0-5: records the real post-apply outcome. Applied starts the
        /// cooldown; retryable failure re-arms the key for a backoff-gated
        /// retry; permanent failure suppresses the key until a new instance.
        /// </summary>
        public void ReportAttempt(string ruleId, ProcessInstanceKey key, DateTime utcNow, RuleAttemptOutcome outcome)
        {
            if (string.IsNullOrWhiteSpace(ruleId)) return;
            var id = (ruleId, key);
            lock (_recent)
            {
                switch (outcome)
                {
                    case RuleAttemptOutcome.Applied:
                        _lastApplyUtc[id] = utcNow;
                        _attempts.Remove(id);
                        CapKeys(_lastApplyUtc, MaxTrackedEntries);
                        break;
                    case RuleAttemptOutcome.FailedRetryable:
                        _attempts[id] = (utcNow, outcome);
                        _seen.Remove(id);
                        break;
                    case RuleAttemptOutcome.FailedPermanent:
                        _attempts[id] = (utcNow, outcome);
                        break;
                }
            }
        }

        private void Record(List<RuleDecision> decisions, SmartRule rule, ProcessInstanceKey key, bool ready, string reason, bool emit = true)
        {
            if (!emit) return;
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

        private static void Cap(HashSet<(string RuleId, ProcessInstanceKey Key)> set, int max)
        {
            if (set.Count <= max) return;
            int remove = set.Count - max;
            foreach (var e in set.Take(remove).ToList())
                set.Remove(e);
        }

        private static void CapKeys<TValue>(Dictionary<(string RuleId, ProcessInstanceKey Key), TValue> dict, int max)
        {
            if (dict.Count <= max) return;
            int remove = dict.Count - max;
            foreach (var k in dict.Keys.Take(remove).ToList())
                dict.Remove(k);
        }
    }
}
