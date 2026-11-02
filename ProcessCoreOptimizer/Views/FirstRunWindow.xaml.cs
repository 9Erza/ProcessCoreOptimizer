using ProcessCoreOptimizer.WPF.ViewModels;
using System.Windows;

namespace ProcessCoreOptimizer.WPF.Views
{
    public partial class FirstRunWindow : Window
    {
        public FirstRunWindow()
        {
            InitializeComponent();
            var vm = new FirstRunViewModel();
            vm.RequestClose += Close;
            DataContext = vm;
        }

        public FirstRunWindow(FirstRunViewModel viewModel)
        {
            InitializeComponent();
            viewModel.RequestClose += Close;
            DataContext = viewModel;
        }
    }
}
