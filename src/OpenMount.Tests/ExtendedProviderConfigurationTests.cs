using System;
using System.IO;
using System.Threading.Tasks;
using OpenMount.Core.Manager;
using OpenMount.Core.Security;
using OpenMount.Core.Storage.Ftp;
using OpenMount.Core.Storage.PCloud;
using OpenMount.Core.Storage.Sftp;
using OpenMount.Core.Storage.WebDav;
using Xunit;

namespace OpenMount.Tests
{
    public class ExtendedProviderConfigurationTests
    {
        [Fact]
        public void Test_WebDav_Config_Instantiation()
        {
            var config = new WebDavStorageConfig
            {
                ServerUrl = "https://dav.box.com/dav",
                Username = "user@domain.com",
                Password = "mypassword",
                RootPath = "/backup"
            };

            var provider = new WebDavStorageProvider(config, "Box WebDAV");
            Assert.Equal("webdav", provider.ProviderId);
            Assert.Equal("Box WebDAV", provider.DisplayName);
        }

        [Fact]
        public void Test_Sftp_Config_Instantiation()
        {
            var config = new SftpStorageConfig
            {
                Host = "sftp.myserver.com",
                Port = 2222,
                Username = "deploy",
                Password = "sshpassword",
                RootPath = "/var/www"
            };

            var provider = new SftpStorageProvider(config, "Production Server");
            Assert.Equal("sftp", provider.ProviderId);
            Assert.Equal("Production Server", provider.DisplayName);
        }

        [Fact]
        public void Test_Ftp_Config_Instantiation()
        {
            var config = new FtpStorageConfig
            {
                Host = "ftp.backup.net",
                Port = 21,
                Username = "ftpuser",
                Password = "ftppassword",
                Encryption = "Explicit",
                RootPath = "/storage"
            };

            var provider = new FtpStorageProvider(config, "NAS Backup");
            Assert.Equal("ftp", provider.ProviderId);
            Assert.Equal("NAS Backup", provider.DisplayName);
        }

        [Fact]
        public void Test_PCloud_Config_Instantiation()
        {
            var config = new PCloudStorageConfig
            {
                Username = "user@pcloud.com",
                Password = "pcloudpassword",
                Location = "EU",
                RootPath = "/Drive"
            };

            var provider = new PCloudStorageProvider(config, "pCloud Drive");
            Assert.Equal("pcloud", provider.ProviderId);
            Assert.Equal("pCloud Drive", provider.DisplayName);
        }

        [Fact]
        public async Task Test_ExtendedProviders_DpapiRoundTrip()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "OpenMount_Test_DpapiExt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string filePath = Path.Combine(tempDir, "drives.dat");

            try
            {
                var store = new DpapiConfigStore(filePath);

                var webdav = new DriveConfig { DriveLetter = "X:", Label = "My WebDAV", ProviderType = "webdav" };
                webdav.SetSetting("url", "https://nextcloud.example.com/remote.php/webdav");
                webdav.SetSetting("user", "nc_user");
                webdav.SetSetting("pass", "nc_pass");

                var sftp = new DriveConfig { DriveLetter = "W:", Label = "My SFTP", ProviderType = "sftp" };
                sftp.SetSetting("host", "192.168.1.100");
                sftp.SetSetting("port", "22");
                sftp.SetSetting("user", "root");
                sftp.SetSetting("pass", "rootpass");

                var ftp = new DriveConfig { DriveLetter = "V:", Label = "My FTP", ProviderType = "ftp" };
                ftp.SetSetting("host", "ftp.example.org");
                ftp.SetSetting("encryption", "Explicit");

                var pcloud = new DriveConfig { DriveLetter = "U:", Label = "My pCloud", ProviderType = "pcloud" };
                pcloud.SetSetting("location", "EU");
                pcloud.SetSetting("user", "test@pcloud.com");

                await store.SaveDrivesAsync(new[] { webdav, sftp, ftp, pcloud });

                var loaded = await store.LoadDrivesAsync();
                Assert.Equal(4, loaded.Count);

                var loadedWebdav = Assert.Single(loaded, d => d.ProviderType == "webdav");
                Assert.Equal("https://nextcloud.example.com/remote.php/webdav", loadedWebdav.GetSetting("url"));

                var loadedSftp = Assert.Single(loaded, d => d.ProviderType == "sftp");
                Assert.Equal("192.168.1.100", loadedSftp.GetSetting("host"));
                Assert.Equal("22", loadedSftp.GetSetting("port"));

                var loadedFtp = Assert.Single(loaded, d => d.ProviderType == "ftp");
                Assert.Equal("Explicit", loadedFtp.GetSetting("encryption"));

                var loadedPCloud = Assert.Single(loaded, d => d.ProviderType == "pcloud");
                Assert.Equal("EU", loadedPCloud.GetSetting("location"));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
