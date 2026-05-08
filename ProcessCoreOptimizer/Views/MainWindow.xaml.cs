using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ProcessCoreOptimizer.WPF.ViewModels;

namespace ProcessCoreOptimizer.WPF.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml, handling custom window chrome, 
    /// system tray integration, and application lifecycle events.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Fields

        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private readonly MainViewModel _viewModel;
        private bool _isExplicitClose = false;

        #endregion

        #region Constructor & Initialization

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            // Initialize System Tray Icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            InitializeSystemTray();

            // Auto-scroll logic for the console log ListBox
            ((System.Collections.Specialized.INotifyCollectionChanged)LogConsole.Items).CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    LogConsole.ScrollIntoView(e.NewItems[0]);
                }
            };

            this.Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// Configures the System Tray (NotifyIcon) appearance, context menu, and localization.
        /// </summary>
        private void InitializeSystemTray()
        {
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
        }

        #endregion

        #region Custom Title Bar Controls

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
            this.WindowState = this.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Window Lifecycle & Tray Management

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            bool startHidden = _viewModel.AppSettings.StartMinimized || ProcessCoreOptimizer.WPF.App.StartupOptions.StartMinimized || ProcessCoreOptimizer.WPF.App.StartupOptions.StartToTray;

            if (startHidden)
            {
                this.WindowState = WindowState.Minimized;
                if (_viewModel.AppSettings.MinimizeToTray || ProcessCoreOptimizer.WPF.App.StartupOptions.StartToTray)
                {
                    this.Hide();
                }
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized && _viewModel.AppSettings.MinimizeToTray)
            {
                this.Hide();
            }

            // Update maximize/restore icon based on current window state
            if (MaximizeIconBtn != null)
            {
                MaximizeIconBtn.Content = this.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            }

            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitClose && _viewModel.AppSettings.CloseToTray)
            {
                e.Cancel = true;
                this.Hide();

                string balloonTitle = "Process Core Optimizer";
                string balloonText = _viewModel.AppSettings.Language == "pl"
                    ? "Aplikacja została zminimalizowana do zasobnika systemowego."
                    : "The application was minimized to the system tray.";

                _notifyIcon.ShowBalloonTip(2000, balloonTitle, balloonText, System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _viewModel.Dispose();
                base.OnClosing(e);
            }
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

        #endregion
    }
}