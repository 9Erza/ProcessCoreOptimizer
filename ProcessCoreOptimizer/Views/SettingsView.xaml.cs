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
        /// Focuses the About header. The category is already selected by the
        /// ViewModel, so no scrolling is needed; focus keeps keyboard users oriented.
        /// </summary>
        public void ScrollToAbout()
        {
            AboutHeader.Focus();
        }
    }
}
