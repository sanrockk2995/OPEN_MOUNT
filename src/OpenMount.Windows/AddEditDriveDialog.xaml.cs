using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
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
            _isEditing = existingConfig != null;
            Config = existingConfig ?? new DriveConfig();

            PopulateDriveLetters();
            LoadExistingConfig();
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

            TxtHeader.Text = "Edit Drive Configuration";
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
