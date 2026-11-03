using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ProcessCoreOptimizer.WPF.Logging;
using ProcessCoreOptimizer.WPF.Models;
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
        private System.Drawing.Icon? _trayIcon;
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
            this.ContentRendered += MainWindow_ContentRendered;
        }

        /// <summary>
        /// Configures the System Tray (NotifyIcon) appearance, context menu, and localization.
        /// Loads the original PCO icon from embedded resources first, falls back to
        /// the executable's associated icon, and finally to a system icon.
        /// The created icon is owned here and disposed on close (no GDI leak,
        /// no redundant NotifyIcon instances: exactly one per process lifetime).
        /// </summary>
        private void InitializeSystemTray()
        {
            _trayIcon = LoadTrayIcon();
            _notifyIcon.Icon = _trayIcon ?? System.Drawing.SystemIcons.Application;

            _notifyIcon.Text = "Process Core Optimizer";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, args) => ShowApplication();

            RebuildTrayMenu();

            _notifyIcon.BalloonTipClicked += (s, args) => ShowApplication();
        }

        private static System.Drawing.Icon? LoadTrayIcon()
        {
            try
            {
                var stream = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Images/processcoreoptimizer.ico"))?.Stream;
                if (stream != null)
                {
                    using (stream)
                    {
                        return new System.Drawing.Icon(stream);
                    }
                }
            }
            catch
            {
                // Fall through to executable-based lookup.
            }

            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                string? exePath = null;
                try { exePath = process.MainModule?.FileName; } catch { exePath = null; }
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    return System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch
            {
                // Final fallback is applied by the caller.
            }

            return null;
        }

        /// <summary>
        /// Rebuilds tray menu labels after a language change (menu texts are
        /// otherwise frozen at startup language).
        /// </summary>
        public void ApplyTrayLanguage()
        {
            try
            {
                RebuildTrayMenu();
            }
            catch
            {
                // Tray menu refresh is best-effort; the app keeps working.
            }
        }

        private void RebuildTrayMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            string openText = _viewModel.AppSettings.Language == "pl" ? "Otwórz Process Core Optimizer" : "Open Process Core Optimizer";
            string exitText = _viewModel.AppSettings.Language == "pl" ? "Zakończ" : "Exit";

            menu.Items.Add(openText, null, (s, e) => ShowApplication());
            menu.Items.Add(exitText, null, (s, e) => CloseApplication());

            var previous = _notifyIcon.ContextMenuStrip;
            _notifyIcon.ContextMenuStrip = menu;
            previous?.Dispose();
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
            switch (Services.StartupWindowBehavior.Resolve(
                _viewModel.AppSettings.StartupWindowMode,
                ProcessCoreOptimizer.WPF.App.StartupOptions.StartMinimized,
                ProcessCoreOptimizer.WPF.App.StartupOptions.StartToTray))
            {
                case Services.StartupWindowAction.HideToTray:
                    this.WindowState = WindowState.Minimized;
                    this.Hide();
                    break;
                case Services.StartupWindowAction.ShowMinimized:
                    this.WindowState = WindowState.Minimized;
                    break;
                default:
                    break;
            }
        }

        private void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            LoggerService.Instance.Info($"First-run check: NeedsWizard={_viewModel.NeedsFirstRunWizard}.");
            if (!_viewModel.NeedsFirstRunWizard) return;
            try
            {
                // The wizard must be visible even for Tray startup: bring the
                // main window forward first so the modal has a visible owner.
                if (!IsVisible)
                {
                    Show();
                    WindowState = WindowState.Normal;
                }

                LoggerService.Instance.Info("First-run: creating wizard window.");
                var wizard = new FirstRunWindow { Owner = this };
                LoggerService.Instance.Info("First-run: showing wizard dialog.");
                bool? result = wizard.ShowDialog();
                LoggerService.Instance.Info($"First-run: dialog closed with result={result}.");
                _viewModel.ReloadSettingsFromDisk();

                // Respect a Tray choice made inside the wizard (or --tray flag).
                if (_viewModel.AppSettings.StartupWindowMode == Models.StartupWindowMode.Tray
                    || ProcessCoreOptimizer.WPF.App.StartupOptions.StartToTray)
                {
                    WindowState = WindowState.Minimized;
                    Hide();
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Error("First-run wizard failed.", ex);
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "ProcessCoreOptimizer", "wizard-error.txt"), ex.ToString()); } catch { }
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
                try { _trayIcon?.Dispose(); } catch { }
                _trayIcon = null;
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