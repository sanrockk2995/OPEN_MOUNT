using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OpenMount.Core.Manager;
using OpenMount.Core.Security;
using Xunit;

namespace OpenMount.Tests
{
    public class DpapiConfigStoreTests : IDisposable
    {
        private readonly string _tempConfigFile;

        public DpapiConfigStoreTests()
        {
            _tempConfigFile = Path.Combine(Path.GetTempPath(), "OpenMount_Tests_Config_" + Guid.NewGuid().ToString("N") + ".dat");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_tempConfigFile))
                {
                    File.Delete(_tempConfigFile);
                }
            }
            catch { }
        }

        [Fact]
        public async Task Test_SaveAndLoadDrives_EncryptedRoundTrip()
        {
            var store = new DpapiConfigStore(_tempConfigFile);

            var drive1 = new DriveConfig
            {
                DriveLetter = "Z:",
                Label = "Local Work",
                ProviderType = "local"
            };
            drive1.SetSetting("path", @"C:\Users\User\Documents");

            var drive2 = new DriveConfig
            {
                DriveLetter = "Y:",
                Label = "Cloud S3 Backup",
                ProviderType = "s3"
            };
            drive2.SetSetting("bucket", "production-backup-2026");
            drive2.SetSetting("key", "AKIASECRETKEY123456");
            drive2.SetSetting("secret", "VerySecretPasswordNotPlaintext");

            var originalList = new List<DriveConfig> { drive1, drive2 };

            // Save encrypted
            await store.SaveDrivesAsync(originalList);

            Assert.True(File.Exists(_tempConfigFile));

            // Verify raw bytes on disk are encrypted (do not contain plaintext passwords or bucket names)
            byte[] rawBytes = await File.ReadAllBytesAsync(_tempConfigFile);
            string rawContent = Encoding.UTF8.GetString(rawBytes);

            Assert.DoesNotContain("production-backup-2026", rawContent);
            Assert.DoesNotContain("VerySecretPasswordNotPlaintext", rawContent);

            // Load and decrypt
            var loadedList = await store.LoadDrivesAsync();

            Assert.Equal(2, loadedList.Count);
            Assert.Equal("Z:", loadedList[0].DriveLetter);
            Assert.Equal(@"C:\Users\User\Documents", loadedList[0].GetSetting("path"));

            Assert.Equal("Y:", loadedList[1].DriveLetter);
            Assert.Equal("production-backup-2026", loadedList[1].GetSetting("bucket"));
            Assert.Equal("VerySecretPasswordNotPlaintext", loadedList[1].GetSetting("secret"));
        }
    }
}
