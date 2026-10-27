using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;
using OpenMount.Core.Manager;
using OpenMount.Core.Storage.GoogleDrive;

namespace OpenMount.Windows
{
    public partial class AddEditDriveDialog : Window
    {
        public DriveConfig Config { get; private set; }
        private bool _isEditing;

        public AddEditDriveDialog(DriveConfig? existingConfig = null)
        {
            InitializeComponent();
            ApplyDialogIcon();
            _isEditing = existingConfig != null;
            Config = existingConfig ?? new DriveConfig();

            TxtHeader.Text = LanguageManager.Get("Loc_DialogTitleAdd");
            Title = TxtHeader.Text + " - OpenMount";

            PopulateDriveLetters();
            LoadExistingConfig();
            LoadAdvancedOptions();
        }

        private void LoadAdvancedOptions()
        {
            if (Config.MountAsNetworkDrive)
            {
                RbMountNetwork.IsChecked = true;
            }
            else
            {
                RbMountLocal.IsChecked = true;
            }

            ChkFilePreview.IsChecked = Config.EnableFilePreview;
            ChkInstantProperties.IsChecked = Config.InstantProperties;
            ChkAsyncUpload.IsChecked = Config.AsyncUpload;
            ChkMaintainDateModified.IsChecked = Config.MaintainDateModified;
            ChkMonitorProcessAccess.IsChecked = Config.MonitorProcessAccess;
            ChkQuickRemoteOperations.IsChecked = Config.QuickRemoteOperations;

            switch (Config.ConflictResolution)
            {
                case ConflictResolutionMode.RenameSource:
                    RbRenameSource.IsChecked = true;
                    break;
                case ConflictResolutionMode.Skip:
                    RbSkip.IsChecked = true;
                    break;
                default:
                    RbOverwrite.IsChecked = true;
                    break;
            }
        }

        private void ApplyDialogIcon()
        {
            try
            {
                if (Owner?.Icon != null)
                {
                    Icon = Owner.Icon;
                    return;
                }

                var iconUri = new Uri("pack://application:,,,/OpenMount.Windows;component/app.ico", UriKind.Absolute);
                Icon = BitmapFrame.Create(iconUri);
            }
            catch { }
        }

        private void PopulateDriveLetters()
        {
            CmbDriveLetter.Items.Clear();
            var letters = new List<string>();

            for (char c = 'Z'; c >= 'D'; c--)
            {
                string letter = $"{c}:";
                bool inUse = DosDeviceHelper.IsDriveInUse(letter);
                if (!inUse || (_isEditing && string.Equals(Config.DriveLetter, letter, StringComparison.OrdinalIgnoreCase)))
                {
                    letters.Add(letter);
                }
            }

            foreach (var l in letters)
            {
                CmbDriveLetter.Items.Add(l);
            }

            if (CmbDriveLetter.Items.Count > 0)
            {
                CmbDriveLetter.SelectedItem = _isEditing ? Config.DriveLetter : CmbDriveLetter.Items[0];
            }
        }

        private void LoadExistingConfig()
        {
            if (!_isEditing) return;

            TxtHeader.Text = LanguageManager.Get("Loc_DialogTitleEdit");
            Title = TxtHeader.Text + " - OpenMount";
            TxtLabel.Text = Config.Label;
            ChkAutoMount.IsChecked = Config.AutoMount;

            string type = Config.ProviderType.ToLowerInvariant();
            if (type == "local")
            {
                TabProviders.SelectedItem = TabLocal;
                TxtLocalPath.Text = Config.GetSetting("path") ?? "";
            }
            else if (type == "gdrive" || type == "google")
            {
                TabProviders.SelectedItem = TabGDrive;
                TxtGDriveClientId.Text = Config.GetSetting("client_id") ?? "";
                TxtGDriveClientSecret.Password = Config.GetSetting("client_secret") ?? "";
                TxtGDriveUser.Text = Config.GetSetting("user") ?? "default";
            }
            else if (type == "s3")
            {
                TabProviders.SelectedItem = TabS3;
                TxtS3Endpoint.Text = Config.GetSetting("endpoint") ?? "https://s3.amazonaws.com";
                TxtS3Bucket.Text = Config.GetSetting("bucket") ?? "";
                TxtS3AccessKey.Text = Config.GetSetting("key") ?? "";
                TxtS3SecretKey.Password = Config.GetSetting("secret") ?? "";
            }
            else if (type == "webdav")
            {
                TabProviders.SelectedItem = TabWebDav;
                TxtWebDavUrl.Text = Config.GetSetting("url") ?? "";
                TxtWebDavPath.Text = Config.GetSetting("path") ?? "/";
                TxtWebDavUser.Text = Config.GetSetting("user") ?? "";
                TxtWebDavPassword.Password = Config.GetSetting("pass") ?? "";
            }
            else if (type == "sftp")
            {
                TabProviders.SelectedItem = TabSftp;
                TxtSftpHost.Text = Config.GetSetting("host") ?? "";
                TxtSftpPort.Text = Config.GetSetting("port") ?? "22";
                TxtSftpPath.Text = Config.GetSetting("path") ?? "/";
                TxtSftpUser.Text = Config.GetSetting("user") ?? "";
                TxtSftpPassword.Password = Config.GetSetting("pass") ?? "";
                TxtSftpKeyPath.Text = Config.GetSetting("key_path") ?? "";
            }
            else if (type == "ftp" || type == "ftps")
            {
                TabProviders.SelectedItem = TabFtp;
                TxtFtpHost.Text = Config.GetSetting("host") ?? "";
                TxtFtpPort.Text = Config.GetSetting("port") ?? "21";
                TxtFtpPath.Text = Config.GetSetting("path") ?? "/";
                TxtFtpUser.Text = Config.GetSetting("user") ?? "";
                TxtFtpPassword.Password = Config.GetSetting("pass") ?? "";

                string enc = Config.GetSetting("encryption") ?? "None";
                CmbFtpEncryption.SelectedIndex = enc.ToLowerInvariant() switch
                {
                    "explicit" => 1,
                    "implicit" => 2,
                    _ => 0
                };
            }
            else if (type == "pcloud")
            {
                TabProviders.SelectedItem = TabPCloud;
                string loc = Config.GetSetting("location") ?? "US";
                CmbPCloudLocation.SelectedIndex = string.Equals(loc, "EU", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                TxtPCloudUser.Text = Config.GetSetting("user") ?? "";
                TxtPCloudPassword.Password = Config.GetSetting("pass") ?? "";
                TxtPCloudPath.Text = Config.GetSetting("path") ?? "/";
            }
        }

        private void BtnBrowseSftpKey_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog();
            dialog.Title = "Select OpenSSH or PuTTY Private Key File";
            dialog.Filter = "Key Files (*.pem;*.key;*.id_rsa;*.ppk)|*.pem;*.key;*.id_rsa;*.ppk|All Files (*.*)|*.*";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtSftpKeyPath.Text = dialog.FileName;
            }
        }

        private void BtnBrowseLocal_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select a local folder to mount as a virtual drive";
            dialog.UseDescriptionForTitle = true;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtLocalPath.Text = dialog.SelectedPath;
            }
        }

        private async void BtnAuthorizeGDrive_Click(object sender, RoutedEventArgs e)
        {
            string clientId = TxtGDriveClientId.Text.Trim();
            string clientSecret = TxtGDriveClientSecret.Password.Trim();
            string user = TxtGDriveUser.Text.Trim();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                MessageBox.Show("Please enter both Google OAuth Client ID and Client Secret first.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnAuthorizeGDrive.IsEnabled = false;
            TxtGDriveAuthStatus.Text = "Waiting for browser login...";

            try
            {
                var gConfig = new GoogleDriveOAuthConfig
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    UserIdentifier = user
                };

                var authService = new GoogleDriveAuthService(gConfig);
                var cred = await authService.AuthorizeAsync();

                TxtGDriveAuthStatus.Text = "Successfully authorized!";
                TxtGDriveAuthStatus.Foreground = System.Windows.Media.Brushes.Green;
                MessageBox.Show("Google Drive authorization successful!", "OpenMount", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtGDriveAuthStatus.Text = "Authorization failed.";
                TxtGDriveAuthStatus.Foreground = System.Windows.Media.Brushes.Red;
                MessageBox.Show($"Authorization error:\n{ex.Message}", "OpenMount Auth Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnAuthorizeGDrive.IsEnabled = true;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (CmbDriveLetter.SelectedItem == null)
            {
                MessageBox.Show("Please select a drive letter.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string driveLetter = CmbDriveLetter.SelectedItem.ToString()!;
            string label = string.IsNullOrWhiteSpace(TxtLabel.Text) ? "OpenMount" : TxtLabel.Text.Trim();

            Config.DriveLetter = driveLetter;
            Config.Label = label;
            Config.AutoMount = ChkAutoMount.IsChecked == true;

            if (TabProviders.SelectedItem == TabLocal)
            {
                string path = TxtLocalPath.Text.Trim();
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    MessageBox.Show("Please select a valid existing directory for local mount.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Config.ProviderType = "local";
                Config.SetSetting("path", path);
            }
            else if (TabProviders.SelectedItem == TabGDrive)
            {
                string clientId = TxtGDriveClientId.Text.Trim();
                string clientSecret = TxtGDriveClientSecret.Password.Trim();
                string user = TxtGDriveUser.Text.Trim();

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    MessageBox.Show("Google Drive requires Client ID and Client Secret.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Config.ProviderType = "gdrive";
                Config.SetSetting("client_id", clientId);
                Config.SetSetting("client_secret", clientSecret);
                Config.SetSetting("user", user);
            }
            else if (TabProviders.SelectedItem == TabS3)
            {
                string bucket = TxtS3Bucket.Text.Trim();
                if (string.IsNullOrEmpty(bucket))
                {
                    MessageBox.Show("Please enter an S3 bucket name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Config.ProviderType = "s3";
                Config.SetSetting("bucket", bucket);
                Config.SetSetting("endpoint", TxtS3Endpoint.Text.Trim());
                Config.SetSetting("key", TxtS3AccessKey.Text.Trim());
                Config.SetSetting("secret", TxtS3SecretKey.Password.Trim());
                Config.SetSetting("region", "us-east-1");
            }
            else if (TabProviders.SelectedItem == TabWebDav)
            {
                string url = TxtWebDavUrl.Text.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show("Please enter a valid WebDAV server URL.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Config.ProviderType = "webdav";
                Config.SetSetting("url", url);
                Config.SetSetting("path", string.IsNullOrWhiteSpace(TxtWebDavPath.Text) ? "/" : TxtWebDavPath.Text.Trim());
                Config.SetSetting("user", TxtWebDavUser.Text.Trim());
                Config.SetSetting("pass", TxtWebDavPassword.Password);
            }
            else if (TabProviders.SelectedItem == TabSftp)
            {
                string host = TxtSftpHost.Text.Trim();
                if (string.IsNullOrEmpty(host))
                {
                    MessageBox.Show("Please enter an SFTP host address.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Config.ProviderType = "sftp";
                Config.SetSetting("host", host);
                Config.SetSetting("port", string.IsNullOrWhiteSpace(TxtSftpPort.Text) ? "22" : TxtSftpPort.Text.Trim());
                Config.SetSetting("path", string.IsNullOrWhiteSpace(TxtSftpPath.Text) ? "/" : TxtSftpPath.Text.Trim());
                Config.SetSetting("user", TxtSftpUser.Text.Trim());
                Config.SetSetting("pass", TxtSftpPassword.Password);
                Config.SetSetting("key_path", TxtSftpKeyPath.Text.Trim());
            }
            else if (TabProviders.SelectedItem == TabFtp)
            {
                string host = TxtFtpHost.Text.Trim();
                if (string.IsNullOrEmpty(host))
                {
                    MessageBox.Show("Please enter an FTP host address.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string enc = CmbFtpEncryption.SelectedIndex switch
                {
                    1 => "Explicit",
                    2 => "Implicit",
                    _ => "None"
                };

                Config.ProviderType = "ftp";
                Config.SetSetting("host", host);
                Config.SetSetting("port", string.IsNullOrWhiteSpace(TxtFtpPort.Text) ? "21" : TxtFtpPort.Text.Trim());
                Config.SetSetting("encryption", enc);
                Config.SetSetting("path", string.IsNullOrWhiteSpace(TxtFtpPath.Text) ? "/" : TxtFtpPath.Text.Trim());
                Config.SetSetting("user", TxtFtpUser.Text.Trim());
                Config.SetSetting("pass", TxtFtpPassword.Password);
            }
            else if (TabProviders.SelectedItem == TabPCloud)
            {
                string user = TxtPCloudUser.Text.Trim();
                if (string.IsNullOrEmpty(user))
                {
                    MessageBox.Show("Please enter your pCloud email or username.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string loc = CmbPCloudLocation.SelectedIndex == 1 ? "EU" : "US";

                Config.ProviderType = "pcloud";
                Config.SetSetting("user", user);
                Config.SetSetting("pass", TxtPCloudPassword.Password);
                Config.SetSetting("location", loc);
                Config.SetSetting("path", string.IsNullOrWhiteSpace(TxtPCloudPath.Text) ? "/" : TxtPCloudPath.Text.Trim());
            }

            Config.MountAsNetworkDrive = RbMountNetwork.IsChecked == true;
            Config.EnableFilePreview = ChkFilePreview.IsChecked == true;
            Config.InstantProperties = ChkInstantProperties.IsChecked == true;
            Config.AsyncUpload = ChkAsyncUpload.IsChecked == true;
            Config.MaintainDateModified = ChkMaintainDateModified.IsChecked == true;
            Config.MonitorProcessAccess = ChkMonitorProcessAccess.IsChecked == true;
            Config.QuickRemoteOperations = ChkQuickRemoteOperations.IsChecked == true;

            if (RbRenameSource.IsChecked == true)
                Config.ConflictResolution = ConflictResolutionMode.RenameSource;
            else if (RbSkip.IsChecked == true)
                Config.ConflictResolution = ConflictResolutionMode.Skip;
            else
                Config.ConflictResolution = ConflictResolutionMode.OverwriteTarget;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
