using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ProcessCoreOptimizer.WPF.ViewModels;

namespace ProcessCoreOptimizer.WPF.Views
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private MainViewModel _viewModel;
        private bool _isExplicitClose = false;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            _notifyIcon = new System.Windows.Forms.NotifyIcon();

            try
            {

                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(process.MainModule.FileName);
                }
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon.Text = "Process Core Optimizer";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, args) => ShowApplication();

            _notifyIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();

            string openText = _viewModel.AppSettings.Language == "pl" ? "Otwórz Process Core Optimizer" : "Open Process Core Optimizer";
            string exitText = _viewModel.AppSettings.Language == "pl" ? "Zakończ" : "Exit";

            _notifyIcon.ContextMenuStrip.Items.Add(openText, null, (s, e) => ShowApplication());
            _notifyIcon.ContextMenuStrip.Items.Add(exitText, null, (s, e) => CloseApplication());

            ((System.Collections.Specialized.INotifyCollectionChanged)LogConsole.Items).CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    LogConsole.ScrollIntoView(e.NewItems[0]);
                }
            };

            this.Loaded += MainWindow_Loaded;
        }

        #region CUSTOM TITLE BAR CONTROLS
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
            }
            else
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        #endregion

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel.AppSettings.StartMinimized)
            {
                this.WindowState = WindowState.Minimized;
                if (_viewModel.AppSettings.MinimizeToTray)
                {
                    this.Hide();
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitClose && _viewModel.AppSettings.CloseToTray)
            {
                e.Cancel = true;
                this.Hide();
                _notifyIcon.ShowBalloonTip(2000, "Process Core Optimizer is running", "The application was minimized to the system tray.", System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                base.OnClosing(e);
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized && _viewModel.AppSettings.MinimizeToTray)
            {
                this.Hide();
            }

            if (MaximizeIconBtn != null)
            {
                MaximizeIconBtn.Content = this.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            }

            base.OnStateChanged(e);
        }

        private void ShowApplication()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void CloseApplication()
        {
            _isExplicitClose = true;
            System.Windows.Application.Current.Shutdown();
        }
    }
}