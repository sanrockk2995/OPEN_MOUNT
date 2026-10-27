using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using OpenMount.Core.FileSystem;
using OpenMount.Core.Manager;
using OpenMount.Core.Storage.Local;
using Xunit;

namespace OpenMount.Tests
{
    public class NetworkDriveMountTests : IDisposable
    {
        private readonly string _tempFolder;

        public NetworkDriveMountTests()
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), "OpenMount_NetTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempFolder);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, true);
                }
            }
            catch { }
        }

        [Fact]
        public void DriveConfig_NetworkDriveDefaults_AreCorrect()
        {
            var config = new DriveConfig();
            Assert.True(config.MountAsNetworkDrive, "Cloud drives should default to Network locations mounting.");
            Assert.Null(config.NetworkShareHost);
        }

        [Fact]
        public void DriveIconHelper_RegisterAndUnregister_ManipulatesRegistryCleanly()
        {
            string testLetter = "T:";
            string dummyIcon = Path.Combine(_tempFolder, "dummy.ico");
            File.WriteAllText(dummyIcon, "dummy icon content");

            try
            {
                DriveIconHelper.RegisterDriveIcon(testLetter, dummyIcon, "Test Cloud Drive");

                // Verify registry key created in HKCU
                using (var baseKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\T"))
                {
                    Assert.NotNull(baseKey);
                    using var iconKey = baseKey!.OpenSubKey("DefaultIcon");
                    Assert.NotNull(iconKey);
                    Assert.Equal(dummyIcon, iconKey!.GetValue(null));

                    using var labelKey = baseKey.OpenSubKey("DefaultLabel");
                    Assert.NotNull(labelKey);
                    Assert.Equal("Test Cloud Drive", labelKey!.GetValue(null));
                }

                // Unregister and verify cleanup
                DriveIconHelper.UnregisterDriveIcon(testLetter);
                using (var afterKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\T"))
                {
                    Assert.Null(afterKey);
                }
            }
            finally
            {
                DriveIconHelper.UnregisterDriveIcon(testLetter);
            }
        }

        [Fact]
        public void DriveIconHelper_NetworkMount_SetsMountPoints2AndAppDrivesRegistryCleanly()
        {
            string testLetter = "Q:";
            string dummyIcon = Path.Combine(_tempFolder, "net_icon.ico");
            File.WriteAllText(dummyIcon, "net icon");
            string testHost = "OpenMount-TestHost";
            string testShare = "TestShare";
            string testLabel = "Google Drive (No UNC Prefix)";

            try
            {
                DriveIconHelper.RegisterDriveIcon(testLetter, dummyIcon, testLabel, testHost, testShare);

                // 1. Verify Classes\Applications\Explorer.exe\Drives\Q\DefaultIcon
                using (var appKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications\Explorer.exe\Drives\Q\DefaultIcon"))
                {
                    Assert.NotNull(appKey);
                    Assert.Equal(dummyIcon, appKey!.GetValue(null));
                }

                // 2. Verify MountPoints2\##OpenMount-TestHost#TestShare _LabelFromReg
                using (var mpKey = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2\##{testHost}#{testShare}"))
                {
                    Assert.NotNull(mpKey);
                    Assert.Equal(testLabel, mpKey!.GetValue("_LabelFromReg"));
                }

                // 3. Unregister and verify cleanup
                DriveIconHelper.UnregisterDriveIcon(testLetter, testHost, testShare);

                using (var afterApp = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications\Explorer.exe\Drives\Q"))
                {
                    Assert.Null(afterApp);
                }
                using (var afterMp = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2\##{testHost}#{testShare}"))
                {
                    Assert.Null(afterMp);
                }
            }
            finally
            {
                DriveIconHelper.UnregisterDriveIcon(testLetter, testHost, testShare);
            }
        }

        [Fact]
        public void DriveIconHelper_ResolveProviderIcon_ReturnsNullOrValidPath()
        {
            string? icon = DriveIconHelper.ResolveProviderIcon("gdrive");
            if (icon != null)
            {
                string checkPath = icon.Contains(',') ? icon.Split(',')[0].Trim('\"') : icon.Trim('\"');
                Assert.True(File.Exists(checkPath));
            }

            string custom = Path.Combine(_tempFolder, "custom.ico");
            File.WriteAllText(custom, "icon");
            string? resolved = DriveIconHelper.ResolveProviderIcon("s3", custom);
            Assert.Equal(custom, resolved);
        }

        [Fact]
        public async Task NetworkDrive_EndToEnd_MountsAsDriveRemoteAndUnmounts()
        {
            if (!WinFspInstaller.IsInstalled())
            {
                return;
            }

            // Find an available drive letter
            string? targetLetter = null;
            for (char c = 'W'; c >= 'H'; c--)
            {
                string candidate = $"{c}:";
                if (!DosDeviceHelper.IsDriveInUse(candidate))
                {
                    targetLetter = candidate;
                    break;
                }
            }

            if (targetLetter == null) return;

            var manager = new DriveManager();
            var config = new DriveConfig
            {
                DriveLetter = targetLetter,
                Label = "OpenMount Network Test",
                ProviderType = "local",
                MountAsNetworkDrive = true
            };

            var provider = new LocalStorageProvider(_tempFolder, "Net Store");
            var context = await manager.MountAsync(config, provider);

            Assert.NotNull(context);
            Assert.True(manager.IsMounted(targetLetter));

            await Task.Delay(300);

            try
            {
                // Verify Win32 Drive Type is DRIVE_REMOTE (4)
                uint driveType = (uint)GetDriveType(targetLetter + "\\");
                Assert.Equal(4u, driveType); // 4 = DRIVE_REMOTE (Network location)

                // Write and read file
                string testFile = Path.Combine(targetLetter + "\\", "test_net.txt");
                File.WriteAllText(testFile, "Hello from network location virtual drive!");

                Assert.True(File.Exists(testFile));
                string readText = File.ReadAllText(testFile);
                Assert.Equal("Hello from network location virtual drive!", readText);
            }
            finally
            {
                await manager.UnmountAsync(targetLetter);
                Assert.False(manager.IsMounted(targetLetter));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern int GetDriveType(string lpRootPathName);
    }
}
