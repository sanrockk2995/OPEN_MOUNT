using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using OpenMount.Core.Manager;
using OpenMount.Core.Storage;
using OpenMount.Core.Storage.GoogleDrive;
using OpenMount.Core.Storage.Local;
using OpenMount.Core.Storage.S3;

namespace OpenMount.Windows
{
    public class DriveViewModel : INotifyPropertyChanged
    {
        private string _status = "Disconnected";
        private bool _isMounted;
        private bool _isBusy;
        private string? _errorMessage;

        public DriveConfig Config { get; set; }

        public string DriveLetter => Config.DriveLetter;
        public string Label => Config.Label;
        public string ProviderType => Config.ProviderType;

        public string ProviderDisplay => Config.ProviderType.ToLowerInvariant() switch
        {
            "gdrive" or "google" => "Google Drive",
            "s3" => "Amazon S3 / MinIO",
            "local" => "Local Folder",
            _ => Config.ProviderType
        };

        public string ProviderDetails => Config.ProviderType.ToLowerInvariant() switch
        {
            "gdrive" or "google" => $"User: {Config.GetSetting("user") ?? "default"}",
            "s3" => $"Bucket: {Config.GetSetting("bucket")}",
            "local" => Config.GetSetting("path") ?? "Not configured",
            _ => string.Empty
        };

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsMounted
        {
            get => _isMounted;
            set { _isMounted = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        private ulong _totalBytes;
        private ulong _freeBytes;
        private string _capacityText = string.Empty;
        private string _capacityDetails = string.Empty;
        private double _usedPercentage;
        private bool _hasCapacity;

        public ulong TotalBytes
        {
            get => _totalBytes;
            private set { _totalBytes = value; OnPropertyChanged(); }
        }

        public ulong FreeBytes
        {
            get => _freeBytes;
            private set { _freeBytes = value; OnPropertyChanged(); }
        }

        public double UsedPercentage
        {
            get => _usedPercentage;
            private set { _usedPercentage = value; OnPropertyChanged(); OnPropertyChanged(nameof(CapacityBarColor)); }
        }

        public string CapacityText
        {
            get => _capacityText;
            private set { _capacityText = value; OnPropertyChanged(); }
        }

        public string CapacityDetails
        {
            get => _capacityDetails;
            private set { _capacityDetails = value; OnPropertyChanged(); }
        }

        public bool HasCapacity
        {
            get => _hasCapacity;
            private set { _hasCapacity = value; OnPropertyChanged(); }
        }

        public string CapacityBarColor
        {
            get
            {
                if (_usedPercentage >= 90.0) return "#DC3545"; // Red / Critical
                if (_usedPercentage >= 80.0) return "#FD7E14"; // Orange / Warning
                return "#0078D4"; // Accent Blue
            }
        }

        public static string FormatBytes(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024.0 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024.0;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public async Task RefreshCapacityAsync()
        {
            if (!IsMounted)
            {
                HasCapacity = false;
                CapacityText = string.Empty;
                CapacityDetails = string.Empty;
                UsedPercentage = 0;
                return;
            }

            try
            {
                var vol = await App.Manager.GetVolumeInfoAsync(DriveLetter);
                if (vol != null && vol.TotalSize > 0)
                {
                    TotalBytes = vol.TotalSize;
                    FreeBytes = vol.FreeSize;
                    ulong used = TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;
                    UsedPercentage = TotalBytes > 0 ? ((double)used / TotalBytes) * 100.0 : 0.0;

                    CapacityText = $"{FormatBytes(used)} / {FormatBytes(TotalBytes)}";
                    CapacityDetails = $"{FormatBytes(FreeBytes)} free ({UsedPercentage:0.#}%)";
                    HasCapacity = true;
                }
            }
            catch { }
        }

        public DriveViewModel(DriveConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            IsMounted = App.Manager.IsMounted(config.DriveLetter);
            Status = IsMounted ? "Mounted" : "Disconnected";
            if (IsMounted)
            {
                _ = RefreshCapacityAsync();
            }
        }

        public async Task ConnectAsync()
        {
            if (IsBusy || IsMounted) return;

            IsBusy = true;
            Status = "Connecting...";
            ErrorMessage = null;

            try
            {
                IStorageProvider provider = await CreateStorageProviderAsync(Config);
                await App.Manager.MountAsync(Config, provider);
                IsMounted = true;
                Status = "Mounted";
                await RefreshCapacityAsync();
            }
            catch (Exception ex)
            {
                Status = "Error";
                ErrorMessage = ex.Message;
                MessageBox.Show($"Failed to mount drive {DriveLetter}:\n{ex.Message}", "OpenMount Mount Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (IsBusy || !IsMounted) return;

            IsBusy = true;
            Status = "Disconnecting...";

            try
            {
                await App.Manager.UnmountAsync(DriveLetter);
                IsMounted = false;
                Status = "Disconnected";
                ErrorMessage = null;
                await RefreshCapacityAsync();
            }
            catch (Exception ex)
            {
                Status = "Error";
                ErrorMessage = ex.Message;
                MessageBox.Show($"Failed to unmount drive {DriveLetter}:\n{ex.Message}", "OpenMount Unmount Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void OpenInExplorer()
        {
            if (!IsMounted) return;
            try
            {
                string path = DriveLetter.TrimEnd('\\') + "\\";
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open Explorer:\n{ex.Message}", "OpenMount", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static async Task<IStorageProvider> CreateStorageProviderAsync(DriveConfig config)
        {
            string type = config.ProviderType.ToLowerInvariant();
            if (type == "local")
            {
                string path = config.GetSetting("path") ?? throw new InvalidOperationException("Local path is not configured.");
                return new LocalStorageProvider(path, config.Label);
            }
            else if (type == "s3")
            {
                var s3Config = new S3StorageConfig
                {
                    BucketName = config.GetSetting("bucket") ?? string.Empty,
                    AccessKey = config.GetSetting("key") ?? string.Empty,
                    SecretKey = config.GetSetting("secret") ?? string.Empty,
                    ServiceUrl = config.GetSetting("endpoint") ?? "https://s3.amazonaws.com",
                    Region = config.GetSetting("region") ?? "us-east-1"
                };
                return new S3StorageProvider(s3Config, displayName: config.Label);
            }
            else if (type == "gdrive" || type == "google")
            {
                string clientId = config.GetSetting("client_id") ?? string.Empty;
                string clientSecret = config.GetSetting("client_secret") ?? string.Empty;
                string user = config.GetSetting("user") ?? "default";

                var gConfig = new GoogleDriveOAuthConfig
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    UserIdentifier = user
                };

                var authService = new GoogleDriveAuthService(gConfig);
                var driveService = await authService.CreateDriveServiceAsync();
                return new GoogleDriveStorageProvider(gConfig, driveService, disposeService: true, displayName: config.Label);
            }

            throw new NotSupportedException($"Storage provider '{config.ProviderType}' is not supported.");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
