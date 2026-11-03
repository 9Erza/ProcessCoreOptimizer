using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ProcessCoreOptimizer.WPF.Views
{
    /// <summary>
    /// Modal editor for a single profile draft. Validates via
    /// <see cref="ProfileStudioValidator"/>; writes back to <see cref="Draft"/>
    /// only when the user confirms with valid data.
    /// </summary>
    public partial class ProfileEditorWindow : Window
    {
        private readonly bool _isNew;
        private readonly bool _isPolish;

        public ProcessProfile Draft { get; }

        public ProfileEditorWindow(ProcessProfile draft, bool isNew, bool allowRealtime, bool isPolish, IReadOnlyList<ProcessProfile>? siblings = null)
        {
            InitializeComponent();
            Draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _isNew = isNew;
            _isPolish = isPolish;

            NameBox.Text = draft.ProcessName ?? string.Empty;
            NameBox.IsEnabled = isNew;
            PathBox.Text = draft.ExecutablePath ?? string.Empty;
            MaskBox.Text = ProfileStudioValidator.FormatMaskHex(draft.AffinityMask == 0 ? 0xFF : draft.AffinityMask);
            if (!draft.ApplyCoreOptimization) MaskBox.Text = ProfileStudioValidator.FormatMaskHex(0);

            PriorityBox.ItemsSource = allowRealtime
                ? PriorityService.AllPriorityNames
                : PriorityService.SafePriorityNames;
            PriorityBox.SelectedItem = PriorityService.Normalize(draft.Priority, allowRealtime);

            ModeBox.SelectedIndex = draft.OptimizationMode == OptimizationMode.CpuSets ? 1 : 0;
            ApplyPriorityBox.IsChecked = draft.ApplyPriority;
            ApplyCoresBox.IsChecked = draft.ApplyCoreOptimization;
            EnabledBox.IsChecked = draft.IsEnabled;
            NotesBox.Text = draft.Notes ?? string.Empty;

            int shared = siblings?.Count(s => s.Id != draft.Id
                && string.Equals(ProfileService.NormalizeProcessName(s.ProcessName), ProfileService.NormalizeProcessName(draft.ProcessName), StringComparison.OrdinalIgnoreCase)) ?? 0;
            if (shared > 0)
            {
                ConflictHint.Text = isPolish
                    ? $"{shared} profil(e) dzieli tę nazwę procesu. Ustaw ścieżkę EXE, aby profil był path-bound i niezależny."
                    : $"{shared} profile(s) share this process name. Set the EXE path to make this profile path-bound and independent.";
                ConflictHint.Visibility = Visibility.Visible;
            }
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
            var result = ProfileStudioValidator.Validate(
                NameBox.Text, MaskBox.Text,
                ApplyCoresBox.IsChecked == true, ApplyPriorityBox.IsChecked == true);
            if (!result.Ok)
            {
                ShowError(_isPolish ? result.ErrorPl : result.ErrorEn);
                return;
            }

            if (_isNew)
            {
                Draft.ProcessName = NameBox.Text.Trim();
                Draft.CreatedAt = DateTime.UtcNow;
            }

            string path = PathBox.Text?.Trim() ?? string.Empty;
            Draft.ExecutablePath = string.IsNullOrEmpty(path) ? null : path;
            Draft.AffinityMask = result.Mask;
            Draft.Priority = (PriorityBox.SelectedItem as string) ?? "Normal";
            Draft.OptimizationMode = ModeBox.SelectedIndex == 1 ? OptimizationMode.CpuSets : OptimizationMode.Affinity;
            Draft.ApplyPriority = ApplyPriorityBox.IsChecked == true;
            Draft.ApplyCoreOptimization = ApplyCoresBox.IsChecked == true;
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
