using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
            Services.StartupProfiler.Mark("MainWindow ctor start");
            InitializeComponent();
            Helpers.WindowTheme.Attach(this);
            Services.StartupProfiler.Mark("MainWindow InitializeComponent done");
            _viewModel = new MainViewModel();
            Services.StartupProfiler.Mark("MainWindow VM created");
            this.DataContext = _viewModel;
            _viewModel.AboutRequested += (s, e) =>
            {
                try { SettingsViewControl?.ScrollToAbout(); } catch { }
            };
            // Deferred update: present once when the user opens/foregrounds PCO.
            this.Activated += (s, e) =>
            {
                try { _viewModel.TryPresentPendingUpdate(); } catch { }
            };

            // Initialize System Tray Icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            InitializeSystemTray();

            // Full log history lives in the Diagnostics tab (DiagLogConsole);
            // the sidebar mini console shows only the latest message.

            this.Loaded += MainWindow_Loaded;
            this.Loaded += (s, e) => Services.StartupProfiler.Mark("MainWindow Loaded");
            this.SizeChanged += (s, e) =>
            {
                _viewModel.SetNarrowLayout(ActualWidth < 1100);
                if (WindowState == WindowState.Maximized) UpdateMaximizedInset();
            };
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

        // Caption is provided by WindowChrome (drag, double-click maximize, Aero Snap
        // and system menu are native). Only the three caption buttons live here.

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            this.WindowState = this.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            // base raises SourceInitialized, where WindowChrome installs its own hook;
            // hooks added afterwards run first, so the snap-layout hit test wins.
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(SnapLayoutHook);
            }
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCMOUSEMOVE = 0x00A0;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONUP = 0x00A2;
        private const int WM_NCMOUSELEAVE = 0x02A2;
        private const int HTMAXBUTTON = 9;

        /// <summary>
        /// Reports the custom maximize button as HTMAXBUTTON so Windows 11 shows the
        /// Snap Layouts flyout on hover. Clicks on it then arrive as non-client
        /// messages, so hover/press visuals and the toggle are handled here.
        /// </summary>
        private IntPtr SnapLayoutHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                switch (msg)
                {
                    case WM_NCHITTEST:
                        if (IsOverMaximizeButton(lParam))
                        {
                            SetMaximizeHover(true);
                            handled = true;
                            return new IntPtr(HTMAXBUTTON);
                        }
                        SetMaximizeHover(false);
                        break;
                    case WM_NCLBUTTONDOWN:
                        if (wParam.ToInt32() == HTMAXBUTTON)
                        {
                            handled = true;
                            return IntPtr.Zero;
                        }
                        break;
                    case WM_NCLBUTTONUP:
                        if (wParam.ToInt32() == HTMAXBUTTON)
                        {
                            handled = true;
                            ToggleMaximize();
                            return IntPtr.Zero;
                        }
                        break;
                    case WM_NCMOUSEMOVE:
                        if (wParam.ToInt32() != HTMAXBUTTON) SetMaximizeHover(false);
                        break;
                    case WM_NCMOUSELEAVE:
                        SetMaximizeHover(false);
                        break;
                }
            }
            catch
            {
                // Never let chrome cosmetics break window message processing.
            }

            return IntPtr.Zero;
        }

        private bool IsOverMaximizeButton(IntPtr lParam)
        {
            if (MaximizeIconBtn == null || !MaximizeIconBtn.IsVisible) return false;
            long value = lParam.ToInt64();
            int x = unchecked((short)(value & 0xFFFF));
            int y = unchecked((short)((value >> 16) & 0xFFFF));
            System.Windows.Point local = MaximizeIconBtn.PointFromScreen(new System.Windows.Point(x, y));
            return local.X >= 0 && local.Y >= 0
                && local.X < MaximizeIconBtn.ActualWidth && local.Y < MaximizeIconBtn.ActualHeight;
        }

        private bool _maximizeHover;

        private void SetMaximizeHover(bool hover)
        {
            if (_maximizeHover == hover || MaximizeIconBtn == null) return;
            _maximizeHover = hover;
            if (hover)
            {
                MaximizeIconBtn.Background = (System.Windows.Media.Brush)FindResource("PcoHoverBrush");
                MaximizeIconBtn.Foreground = (System.Windows.Media.Brush)FindResource("PcoTextBrightBrush");
            }
            else
            {
                MaximizeIconBtn.ClearValue(BackgroundProperty);
                MaximizeIconBtn.ClearValue(ForegroundProperty);
            }
        }

        /// <summary>
        /// A maximized WindowChrome window extends past the monitor by its resize
        /// frame; pad the content by exactly that overflow so nothing is clipped.
        /// </summary>
        private void UpdateMaximizedInset()
        {
            if (RootBorder == null) return;
            RootBorder.Margin = WindowState == WindowState.Maximized
                ? Helpers.WindowTheme.GetMaximizedOverflow(this)
                : new Thickness(0);
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
            Services.StartupProfiler.Mark("MainWindow ContentRendered (first paint)");
            if (Services.StartupProfiler.IsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(12000);
                        Services.StartupProfiler.Dump("t+12s");
                    }
                    catch { }
                });
            }
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

            UpdateMaximizedInset();

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
                Services.StartupProfiler.Dump("closing");
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