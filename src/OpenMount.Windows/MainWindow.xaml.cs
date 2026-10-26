using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using OpenMount.Core.FileSystem;
using OpenMount.Core.Manager;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace OpenMount.Windows
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<DriveViewModel> Drives { get; } = new ObservableCollection<DriveViewModel>();
        private NotifyIcon? _trayIcon;
        private ToolStripMenuItem? _trayAutoStartItem;
        private bool _isExplicitExit = false;
        private bool _hasShownTrayBalloon = false;

        public MainWindow()
        {
            InitializeComponent();
            LstDrives.ItemsSource = Drives;

            InitializeTrayIcon();
            ChkAutoStart.IsChecked = StartupHelper.IsRunOnStartup();
            UpdateWinFspState();

            // Load drives and trigger auto-mount immediately on creation (works in background on boot)
            _ = LoadDrivesAsync();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == App.WM_SHOW_OPENMOUNT)
            {
                RestoreFromTray();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void InitializeTrayIcon()
        {
            Icon icon = LoadAppIcon();

            _trayIcon = new NotifyIcon
            {
                Text = "OpenMount - Virtual Cloud Storage",
                Visible = true,
                Icon = icon
            };

            var contextMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Open OpenMount", null, (s, e) => RestoreFromTray())
            {
                Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
            };
            contextMenu.Items.Add(openItem);

            _trayAutoStartItem = new ToolStripMenuItem("Run on Windows Startup", null, (s, e) =>
            {
                bool newState = !StartupHelper.IsRunOnStartup();
                StartupHelper.SetRunOnStartup(newState);
                if (_trayAutoStartItem != null)
                {
                    _trayAutoStartItem.Checked = newState;
                }
                ChkAutoStart.IsChecked = newState;
            })
            {
                Checked = StartupHelper.IsRunOnStartup()
            };
            contextMenu.Items.Add(_trayAutoStartItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Mount All", null, async (s, e) => await MountAllDrivesAsync());
            contextMenu.Items.Add("Unmount All", null, async (s, e) => await UnmountAllDrivesAsync());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath))
                {
                    return new System.Drawing.Icon(iconPath);
                }

                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (extracted != null) return extracted;
                }
            }
            catch { }

            return System.Drawing.SystemIcons.Application;
        }

        private async Task LoadDrivesAsync()
        {
            Drives.Clear();
            try
            {
                var configs = await App.ConfigStore.LoadDrivesAsync();
                foreach (var cfg in configs)
                {
                    var vm = new DriveViewModel(cfg);
                    Drives.Add(vm);

                    if (cfg.AutoMount && !vm.IsMounted)
                    {
                        // Background mount with retry for startup boot resiliency
                        _ = MountWithRetryAsync(vm);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading stored configurations:\n{ex.Message}", "OpenMount", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            UpdateUIState();
        }

        private void UpdateWinFspState()
        {
            bool installed = WinFspInstaller.IsInstalled();
            if (installed)
            {
                PnlWinFspMissingBanner.Visibility = Visibility.Collapsed;
                TxtWinFspStatus.Text = "WinFsp Virtual Filesystem Active";
                TxtWinFspStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
                BtnStatusInstallWinFsp.Visibility = Visibility.Collapsed;
            }
            else
            {
                PnlWinFspMissingBanner.Visibility = Visibility.Visible;
                TxtWinFspStatus.Text = "WinFsp Driver Missing";
                TxtWinFspStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
                BtnStatusInstallWinFsp.Visibility = Visibility.Visible;
            }
        }

        private async void BtnInstallWinFsp_Click(object sender, RoutedEventArgs e)
        {
            await EnsureWinFspInstalledAsync();
        }

        public async Task<bool> EnsureWinFspInstalledAsync()
        {
            if (WinFspInstaller.IsInstalled())
            {
                UpdateWinFspState();
                return true;
            }

            try
            {
                TxtStatusSummary.Text = "Installing WinFsp kernel driver...";
                var (success, message) = await WinFspInstaller.InstallAsync(silent: false);
                UpdateWinFspState();

                if (success)
                {
                    MessageBox.Show(
                        "WinFsp has been installed successfully. Virtual cloud drive mounting is now active.",
                        "OpenMount",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await MountAllDrivesAsync();
                    return true;
                }
                else
                {
                    MessageBox.Show(
                        $"WinFsp installation did not complete:\n{message}",
                        "OpenMount",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing WinFsp:\n{ex.Message}", "OpenMount", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                UpdateUIState();
            }
        }

        private async Task MountWithRetryAsync(DriveViewModel vm, int maxRetries = 3)
        {
            if (!WinFspInstaller.IsInstalled()) return;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await vm.ConnectAsync();
                    if (vm.IsMounted)
                    {
                        UpdateUIState();
                        return;
                    }
                }
                catch
                {
                    // Network might still be initializing on boot
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(2500 * attempt);
                }
            }
        }

        private async Task SaveDrivesAsync()
        {
            var configs = Drives.Select(d => d.Config).ToList();
            await App.ConfigStore.SaveDrivesAsync(configs);
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            PnlEmptyState.Visibility = Drives.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int mountedCount = Drives.Count(d => d.IsMounted);
            TxtStatusSummary.Text = $"{Drives.Count} drive(s) configured | {mountedCount} active";
        }

        private void ChkAutoStart_Click(object sender, RoutedEventArgs e)
        {
            bool enable = ChkAutoStart.IsChecked == true;
            StartupHelper.SetRunOnStartup(enable);
            if (_trayAutoStartItem != null)
            {
                _trayAutoStartItem.Checked = enable;
            }
        }

        private async void BtnAddDrive_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddEditDriveDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                var vm = new DriveViewModel(dialog.Config);
                Drives.Add(vm);
                await SaveDrivesAsync();

                // Connect immediately after adding
                await vm.ConnectAsync();
                UpdateUIState();
            }
        }

        private async void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is DriveViewModel vm)
            {
                if (vm.IsMounted)
                {
                    MessageBox.Show("Please disconnect the drive before editing its settings.", "OpenMount", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new AddEditDriveDialog(vm.Config);
                dialog.Owner = this;
                if (dialog.ShowDialog() == true)
                {
                    await SaveDrivesAsync();
                    await LoadDrivesAsync();
                }
            }
        }

        private async void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is DriveViewModel vm)
            {
                if (vm.IsMounted)
                {
                    await vm.DisconnectAsync();
                }

                var confirm = MessageBox.Show($"Remove drive configuration for {vm.DriveLetter} ({vm.Label})?", "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    Drives.Remove(vm);
                    await SaveDrivesAsync();
                }
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is DriveViewModel vm)
            {
                if (!WinFspInstaller.IsInstalled())
                {
                    var confirm = MessageBox.Show(
                        "WinFsp kernel driver is required to mount this virtual drive.\nWould you like OpenMount to install WinFsp now?",
                        "WinFsp Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        bool installed = await EnsureWinFspInstalledAsync();
                        if (!installed) return;
                    }
                    else
                    {
                        return;
                    }
                }

                await vm.ConnectAsync();
                UpdateUIState();
            }
        }

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is DriveViewModel vm)
            {
                await vm.DisconnectAsync();
                UpdateUIState();
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is DriveViewModel vm)
            {
                vm.OpenInExplorer();
            }
        }

        private async void BtnMountAll_Click(object sender, RoutedEventArgs e)
        {
            if (!WinFspInstaller.IsInstalled())
            {
                var confirm = MessageBox.Show(
                    "WinFsp kernel driver is required to mount virtual drives.\nWould you like OpenMount to install WinFsp now?",
                    "WinFsp Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    bool installed = await EnsureWinFspInstalledAsync();
                    if (!installed) return;
                }
                else
                {
                    return;
                }
            }

            await MountAllDrivesAsync();
        }

        private async void BtnUnmountAll_Click(object sender, RoutedEventArgs e)
        {
            await UnmountAllDrivesAsync();
        }

        private async Task MountAllDrivesAsync()
        {
            foreach (var drive in Drives.Where(d => !d.IsMounted))
            {
                await drive.ConnectAsync();
            }
            UpdateUIState();
        }

        private async Task UnmountAllDrivesAsync()
        {
            foreach (var drive in Drives.Where(d => d.IsMounted))
            {
                await drive.DisconnectAsync();
            }
            UpdateUIState();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            Focus();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                if (!_hasShownTrayBalloon)
                {
                    _trayIcon?.ShowBalloonTip(2000, "OpenMount", "OpenMount continues running in the system tray. Right-click tray icon to Exit.", ToolTipIcon.Info);
                    _hasShownTrayBalloon = true;
                }
            }
        }

        private void ExitApplication()
        {
            _isExplicitExit = true;
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            Application.Current.Shutdown();
        }
    }
}
