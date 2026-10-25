using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using OpenMount.Core.Manager;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace OpenMount.Windows
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<DriveViewModel> Drives { get; } = new ObservableCollection<DriveViewModel>();
        private NotifyIcon? _trayIcon;
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();
            LstDrives.ItemsSource = Drives;

            InitializeTrayIcon();
            Loaded += MainWindow_Loaded;
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "OpenMount - Virtual Cloud Storage",
                Visible = true,
                Icon = SystemIcons.Application
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show OpenMount", null, (s, e) => RestoreFromTray());
            contextMenu.Items.Add("Mount All", null, async (s, e) => await MountAllDrivesAsync());
            contextMenu.Items.Add("Unmount All", null, async (s, e) => await UnmountAllDrivesAsync());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDrivesAsync();
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
                        _ = vm.ConnectAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading stored configurations:\n{ex.Message}", "OpenMount", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            UpdateUIState();
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
            Activate();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                _trayIcon?.ShowBalloonTip(1500, "OpenMount", "OpenMount is running in the system tray.", ToolTipIcon.Info);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                Hide();
                _trayIcon?.ShowBalloonTip(1500, "OpenMount", "Minimized to tray. Right-click tray icon to Exit.", ToolTipIcon.Info);
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
