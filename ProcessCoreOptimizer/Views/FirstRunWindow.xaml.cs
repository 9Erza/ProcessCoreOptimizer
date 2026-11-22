using ProcessCoreOptimizer.WPF.ViewModels;
using System.Windows;

namespace ProcessCoreOptimizer.WPF.Views
{
    public partial class FirstRunWindow : Window
    {
        public FirstRunWindow()
        {
            InitializeComponent();
            ProcessCoreOptimizer.WPF.Helpers.WindowTheme.Attach(this);
            var vm = new FirstRunViewModel();
            vm.RequestClose += Close;
            DataContext = vm;
        }

        public FirstRunWindow(FirstRunViewModel viewModel)
        {
            InitializeComponent();
            ProcessCoreOptimizer.WPF.Helpers.WindowTheme.Attach(this);
            viewModel.RequestClose += Close;
            DataContext = viewModel;
        }
    }
}
