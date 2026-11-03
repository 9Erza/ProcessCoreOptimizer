using System.Windows.Controls;

namespace ProcessCoreOptimizer.WPF.Views
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Brings the existing About section into view and moves keyboard focus
        /// to its header (used by the sidebar Developer Hub "About" row).
        /// </summary>
        public void ScrollToAbout()
        {
            AboutHeader.BringIntoView();
            AboutHeader.Focus();
        }
    }
}
