using ProcessCoreOptimizer.WPF.Helpers;
using System;

namespace ProcessCoreOptimizer.WPF.Models
{
    public enum RuleTrigger
    {
        OnLaunch,
        OnForeground
    }

    public enum RuleAction
    {
        ApplyProfile,
        RestoreSession
    }

    /// <summary>
    /// A persisted automation rule. Config only — runtime state (pending,
    /// cooldown, overrides, history) lives in <see cref="Services.SmartRuleEngine"/>.
    /// </summary>
    public class SmartRule : ViewModelBase
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = string.Empty;
        private string _processName = string.Empty;
        private string? _executablePath;
        private RuleTrigger _trigger = RuleTrigger.OnLaunch;
        private RuleAction _action = RuleAction.ApplyProfile;
        private string? _profileId;
        private int _delaySeconds;
        private int _cooldownSeconds = 30;
        private int _priority = 100;
        private bool _isEnabled = true;
        private string _notes = string.Empty;
        private DateTime _createdAt = DateTime.UtcNow;
        private DateTime _updatedAt = DateTime.UtcNow;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
        }

        public string Name
        {
            get => string.IsNullOrWhiteSpace(_name) ? ProcessName : _name;
            set => SetProperty(ref _name, value?.Trim() ?? string.Empty);
        }

        public string ProcessName
        {
            get => _processName;
            set
            {
                string normalized = value?.Trim() ?? string.Empty;
                if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && normalized.Length > 4)
                    normalized = normalized.Substring(0, normalized.Length - 4);
                SetProperty(ref _processName, normalized);
            }
        }

        public string? ExecutablePath
        {
            get => _executablePath;
            set => SetProperty(ref _executablePath, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        public RuleTrigger Trigger
        {
            get => _trigger;
            set => SetProperty(ref _trigger, value);
        }

        public RuleAction Action
        {
            get => _action;
            set => SetProperty(ref _action, value);
        }

        public string? ProfileId
        {
            get => _profileId;
            set => SetProperty(ref _profileId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        public int DelaySeconds
        {
            get => _delaySeconds;
            set => SetProperty(ref _delaySeconds, Math.Clamp(value, 0, 300));
        }

        public int CooldownSeconds
        {
            get => _cooldownSeconds;
            set => SetProperty(ref _cooldownSeconds, Math.Clamp(value, 0, 3600));
        }

        public int Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value?.Trim() ?? string.Empty);
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value == default ? DateTime.UtcNow : value);
        }

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetProperty(ref _updatedAt, value == default ? DateTime.UtcNow : value);
        }
    }
}
