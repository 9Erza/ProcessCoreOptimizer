using System.Collections.Specialized;
using System.Windows.Controls;

namespace ProcessCoreOptimizer.WPF.Views
{
    public partial class DiagnosticsView : System.Windows.Controls.UserControl
    {
        public DiagnosticsView()
        {
            InitializeComponent();
            ((INotifyCollectionChanged)DiagLogConsole.Items).CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    DiagLogConsole.ScrollIntoView(e.NewItems[0]);
                }
            };
        }
    }
}
