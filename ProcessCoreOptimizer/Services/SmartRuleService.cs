using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcessCoreOptimizer.WPF.Services
{
    /// <summary>
    /// Loads, validates and safely saves automation rules (smartrules.json).
    /// Mirrors the ProfileService durability pattern (atomic write + .bak fallback).
    /// </summary>
    public class SmartRuleService
    {
        private static readonly ILogger _logger = LoggerService.Instance;
        private readonly string _filePath = AppPaths.GetUserDataFilePath("smartrules.json");

        private static JsonSerializerOptions JsonOptions
        {
            get
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                options.Converters.Add(new JsonStringEnumConverter());
                return options;
            }
        }

        public List<SmartRule> LoadRules()
        {
            AppPaths.MigrateLegacyFileIfNeeded("smartrules.json");

            if (!File.Exists(_filePath) && !File.Exists(_filePath + ".bak"))
                return new List<SmartRule>();

            try
            {
                string? jsonContent = AtomicFileService.ReadValidatedWithBackup(_filePath, content =>
                {
                    if (string.IsNullOrWhiteSpace(content)) return true;
                    try
                    {
                        _ = JsonSerializer.Deserialize<List<SmartRule>>(content, JsonOptions);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (string.IsNullOrWhiteSpace(jsonContent)) return new List<SmartRule>();
                var rules = JsonSerializer.Deserialize<List<SmartRule>>(jsonContent, JsonOptions) ?? new List<SmartRule>();
                var sanitized = SanitizeRules(rules).ToList();
                if (!RulesEquivalent(rules, sanitized))
                    SaveRules(sanitized);
                return sanitized;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Smart rules load failed, starting empty: {ex.Message}");
                return new List<SmartRule>();
            }
        }

        public void SaveRules(IEnumerable<SmartRule> rules)
        {
            TrySaveRules(rules, out _);
        }

        /// <summary>
        /// Atomically saves rules (.bak fallback via AtomicFileService).
        /// Returns null on success, or a short error otherwise — the caller
        /// must NOT report success when this returns non-null.
        /// </summary>
        public bool TrySaveRules(IEnumerable<SmartRule> rules, out string? error)
        {
            error = null;
            var sanitized = SanitizeRules(rules).ToList();
            try
            {
                string json = JsonSerializer.Serialize(sanitized, JsonOptions);
                _ = JsonSerializer.Deserialize<List<SmartRule>>(json, JsonOptions);
                AtomicFileService.WriteAllTextAtomic(_filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.Error($"Smart rules save failed: {ex.Message}");
                return false;
            }
        }

        public static IEnumerable<SmartRule> SanitizeRules(IEnumerable<SmartRule> rules)
        {
            return rules
                .Where(r => r != null)
                .Select(SanitizeRule)
                .Where(r => !string.IsNullOrWhiteSpace(r.ProcessName));
        }

        private static SmartRule SanitizeRule(SmartRule rule)
        {
            rule.Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id;
            rule.ProcessName = ProfileService.NormalizeProcessName(rule.ProcessName);
            rule.ExecutablePath = ProfileService.NormalizeExecutablePath(rule.ExecutablePath);
            if (!Enum.IsDefined(rule.Trigger)) rule.Trigger = RuleTrigger.OnLaunch;
            if (!Enum.IsDefined(rule.Action)) rule.Action = RuleAction.ApplyProfile;
            rule.DelaySeconds = Math.Clamp(rule.DelaySeconds, 0, 300);
            rule.CooldownSeconds = Math.Clamp(rule.CooldownSeconds, 0, 3600);
            rule.CreatedAt = rule.CreatedAt == default ? DateTime.UtcNow : rule.CreatedAt;
            rule.UpdatedAt = rule.UpdatedAt == default ? DateTime.UtcNow : rule.UpdatedAt;
            return rule;
        }

        private static bool RulesEquivalent(IEnumerable<SmartRule> a, IEnumerable<SmartRule> b)
        {
            var la = a.ToList();
            var lb = b.ToList();
            if (la.Count != lb.Count) return false;
            foreach (var x in la)
            {
                if (!lb.Any(y => y.Id == x.Id
                    && y.ProcessName == x.ProcessName
                    && (y.ExecutablePath ?? "") == (x.ExecutablePath ?? "")
                    && y.Trigger == x.Trigger && y.Action == x.Action
                    && y.ProfileId == x.ProfileId && y.DelaySeconds == x.DelaySeconds
                    && y.CooldownSeconds == x.CooldownSeconds && y.Priority == x.Priority
                    && y.IsEnabled == x.IsEnabled)) return false;
            }

            return true;
        }

        public static SmartRule? FindById(IEnumerable<SmartRule> rules, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return rules.FirstOrDefault(r => r != null && string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static List<SmartRule> RemoveById(IEnumerable<SmartRule> rules, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return rules.ToList();
            return rules.Where(r => r == null || !string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
