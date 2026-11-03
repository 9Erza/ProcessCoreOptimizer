using ProcessCoreOptimizer.WPF.Services;
using ProcessCoreOptimizer.WPF.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace ProcessCoreOptimizer.WPF.Views
{
    /// <summary>
    /// PCO-styled update dialog (dark theme, blue accent, original PCO icon).
    /// Opens the GitHub release page on user click only: never downloads,
    /// never runs installers, never bypasses SmartScreen.
    /// </summary>
    public partial class UpdateAvailableDialog : Window
    {
        private readonly string _releaseUrl;

        private UpdateAvailableDialog(string latestVersion, string currentVersion, string releaseUrl, bool isPolish)
        {
            InitializeComponent();
            _releaseUrl = releaseUrl;
            var texts = UpdateDialogTextProvider.ForLanguage(isPolish);
            Title = texts.Title;
            DialogTitleText.Text = texts.Title;
            DialogHeadingText.Text = texts.Heading;
            LatestVersionText.Text = $"v{latestVersion.TrimStart('v', 'V')}";
            CurrentVersionText.Text = string.Format(texts.CurrentVersion, currentVersion.TrimStart('v', 'V'));
            OpenReleaseButton.Content = texts.OpenRelease;
            LaterButton.Content = texts.Later;
        }

        /// <summary>
        /// Presents the single PCO-styled update dialog. Shared by the automatic
        /// check and the manual Settings check so no duplicate modal exists.
        /// Must be called on the UI thread.
        /// </summary>
        public static void Present(Window? owner, string latestVersion, string currentVersion, string releaseUrl, bool isPolish)
        {
            var dialog = new UpdateAvailableDialog(latestVersion, currentVersion, releaseUrl, isPolish)
            {
                Owner = owner != null && owner.IsVisible ? owner : null
            };
            dialog.ShowDialog();
        }

        private void OpenReleaseButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateService.OpenReleasePage(_releaseUrl);
            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // DragMove can throw if the mouse state changes during window chrome processing.
                }
            }
        }
    }
}
