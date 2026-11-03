using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ProcessCoreOptimizer.WPF.Views
{
    /// <summary>
    /// Modal editor for a single automation rule draft. Writes back to
    /// <see cref="Draft"/> only on valid confirm; cancel leaves it untouched.
    /// </summary>
    public partial class RuleEditorWindow : Window
    {
        private readonly bool _isNew;
        private readonly bool _isPolish;
        private readonly List<ProcessProfile> _profiles;

        public SmartRule Draft { get; }

        public RuleEditorWindow(SmartRule draft, bool isNew, IEnumerable<ProcessProfile> profiles, bool isPolish)
        {
            InitializeComponent();
            Draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _isNew = isNew;
            _isPolish = isPolish;
            _profiles = profiles?.ToList() ?? new List<ProcessProfile>();

            NameBox.Text = draft.Name ?? string.Empty;
            ProcessBox.Text = draft.ProcessName ?? string.Empty;
            ProcessBox.IsEnabled = isNew;
            PathBox.Text = draft.ExecutablePath ?? string.Empty;
            TriggerBox.SelectedIndex = draft.Trigger == RuleTrigger.OnForeground ? 1 : 0;
            ActionBox.SelectedIndex = draft.Action == RuleAction.RestoreSession ? 1 : 0;
            DelayBox.Text = draft.DelaySeconds.ToString();
            CooldownBox.Text = draft.CooldownSeconds.ToString();
            PriorityBox.Text = draft.Priority.ToString();
            EnabledBox.IsChecked = draft.IsEnabled;
            NotesBox.Text = draft.Notes ?? string.Empty;

            ProfileBox.ItemsSource = _profiles
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.ExecutablePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(p => new ComboBoxItem
                {
                    Content = string.IsNullOrWhiteSpace(p.ExecutablePath)
                        ? $"{p.DisplayName}  —  {(p.IsEnabled ? "enabled" : "disabled")}"
                        : $"{p.DisplayName}  [{p.ExecutablePath}]  —  {(p.IsEnabled ? "enabled" : "disabled")}",
                    Tag = p.Id
                }).ToList();
            if (!string.IsNullOrWhiteSpace(draft.ProfileId))
            {
                foreach (ComboBoxItem item in ProfileBox.Items)
                {
                    if (string.Equals(item.Tag as string, draft.ProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        ProfileBox.SelectedItem = item;
                        break;
                    }
                }
            }

            UpdateTriggerHint();
            TriggerBox.SelectionChanged += (s, e) => UpdateTriggerHint();
        }

        private void UpdateTriggerHint()
        {
            bool fg = TriggerBox.SelectedIndex == 1;
            TriggerHint.Text = _isPolish
                ? (fg ? "Aktywne okno: reguła odpala, gdy PID jest stabilnie na pierwszym planie (debounce na szybki Alt+Tab)."
                      : "Uruchomienie: reguła odpala raz dla nowej instancji (para PID + czas startu).")
                : (fg ? "Foreground: fires when the PID is stably in the foreground (debounces fast Alt+Tab)."
                      : "Launch: fires once per new instance (PID + start time pair).");
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(this) == true)
                    PathBox.Text = dialog.FileName;
            }
            catch (Exception ex)
            {
                ShowError(_isPolish ? $"Nie można otworzyć okna wyboru pliku: {ex.Message}" : $"Could not open the file picker: {ex.Message}");
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProfileService.NormalizeProcessName(ProcessBox.Text)))
            {
                ShowError(_isPolish ? "Nazwa procesu jest wymagana." : "Process name is required.");
                return;
            }

            if (!int.TryParse(DelayBox.Text?.Trim(), out int delay) || delay < 0 || delay > 300)
            {
                ShowError(_isPolish ? "Opóźnienie: liczba 0–300 s." : "Delay: a number 0–300 s.");
                return;
            }

            if (!int.TryParse(CooldownBox.Text?.Trim(), out int cooldown) || cooldown < 0 || cooldown > 3600)
            {
                ShowError(_isPolish ? "Cooldown: liczba 0–3600 s." : "Cooldown: a number 0–3600 s.");
                return;
            }

            if (!int.TryParse(PriorityBox.Text?.Trim(), out int priority))
            {
                ShowError(_isPolish ? "Priorytet: liczba całkowita (mniejsza wygrywa)." : "Priority: an integer (lower wins).");
                return;
            }

            bool restore = ActionBox.SelectedIndex == 1;
            string? profileId = (ProfileBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (!restore && string.IsNullOrWhiteSpace(profileId))
            {
                ShowError(_isPolish ? "Wybierz profil do zastosowania." : "Select a profile to apply.");
                return;
            }

            if (_isNew)
            {
                Draft.ProcessName = ProcessBox.Text.Trim();
                Draft.CreatedAt = DateTime.UtcNow;
            }

            Draft.Name = NameBox.Text?.Trim() ?? string.Empty;
            string path = PathBox.Text?.Trim() ?? string.Empty;
            Draft.ExecutablePath = string.IsNullOrEmpty(path) ? null : path;
            Draft.Trigger = TriggerBox.SelectedIndex == 1 ? RuleTrigger.OnForeground : RuleTrigger.OnLaunch;
            Draft.Action = restore ? RuleAction.RestoreSession : RuleAction.ApplyProfile;
            Draft.ProfileId = restore ? null : profileId;
            Draft.DelaySeconds = delay;
            Draft.CooldownSeconds = cooldown;
            Draft.Priority = priority;
            Draft.IsEnabled = EnabledBox.IsChecked == true;
            Draft.Notes = NotesBox.Text?.Trim() ?? string.Empty;
            Draft.UpdatedAt = DateTime.UtcNow;

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
