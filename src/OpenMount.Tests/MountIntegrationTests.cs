using System;
using System.IO;
using System.Threading.Tasks;
using OpenMount.Core.Manager;
using OpenMount.Core.Storage.Local;
using Xunit;

namespace OpenMount.Tests
{
    public class MountIntegrationTests : IDisposable
    {
        private readonly string _testFolder;
        private readonly DriveManager _manager;
        private string? _mountedDriveLetter;

        public MountIntegrationTests()
        {
            _testFolder = Path.Combine(Path.GetTempPath(), "OpenMount_E2E_Backing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testFolder);
            _manager = new DriveManager();
        }

        public void Dispose()
        {
            try
            {
                if (!string.IsNullOrEmpty(_mountedDriveLetter))
                {
                    _manager.UnmountAsync(_mountedDriveLetter).GetAwaiter().GetResult();
                }
            }
            catch { }

            try
            {
                _manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { }

            try
            {
                if (Directory.Exists(_testFolder))
                {
                    Directory.Delete(_testFolder, true);
                }
            }
            catch { }
        }

        private static string FindAvailableDriveLetter()
        {
            for (char c = 'Z'; c >= 'G'; c--)
            {
                string letter = $"{c}:";
                if (!DosDeviceHelper.IsDriveInUse(letter))
                {
                    return letter;
                }
            }
            throw new InvalidOperationException("No free drive letters available for integration test.");
        }

        [Fact]
        public async Task Test_EndToEnd_MountFilesystemOperationsAndUnmount()
        {
            if (!OpenMount.Core.FileSystem.WinFspInstaller.IsInstalled())
            {
                // Skip live driver mount in environments without WinFsp kernel driver
                return;
            }

            string driveLetter = FindAvailableDriveLetter();
            _mountedDriveLetter = driveLetter;

            var config = new DriveConfig
            {
                DriveLetter = driveLetter,
                Label = "E2E Test Drive",
                ProviderType = "local"
            };

            var provider = new LocalStorageProvider(_testFolder, "E2E Test Store");

            // 1. Mount virtual drive letter
            var context = await _manager.MountAsync(config, provider);
            Assert.NotNull(context);
            Assert.True(_manager.IsMounted(driveLetter));

            string root = driveLetter + "\\";

            // Allow brief settle time for Windows shell
            await Task.Delay(250);

            try
            {
                // 2. Write a file to the mounted drive letter
                string filePath = Path.Combine(root, "openmount_test.txt");
                string expectedContent = "OpenMount end-to-end Windows drive letter test successful!";
                File.WriteAllText(filePath, expectedContent);

                Assert.True(File.Exists(filePath));

                // 3. Read content back through Windows filesystem
                string actualContent = File.ReadAllText(filePath);
                Assert.Equal(expectedContent, actualContent);

                // 4. Directory creation and file moving
                string subDir = Path.Combine(root, "folder_a");
                Directory.CreateDirectory(subDir);
                Assert.True(Directory.Exists(subDir));

                string movedPath = Path.Combine(subDir, "renamed.txt");
                File.Move(filePath, movedPath);
                Assert.False(File.Exists(filePath));
                Assert.True(File.Exists(movedPath));

                // 5. Enumerate files via DirectoryInfo
                var dirInfo = new DirectoryInfo(subDir);
                var files = dirInfo.GetFiles();
                Assert.Single(files);
                Assert.Equal("renamed.txt", files[0].Name);

                // 6. Delete file
                File.Delete(movedPath);
                Assert.False(File.Exists(movedPath));

                // 7. Remove directory
                Directory.Delete(subDir);
                Assert.False(Directory.Exists(subDir));
            }
            finally
            {
                // 8. Cleanly unmount
                await _manager.UnmountAsync(driveLetter);
                _mountedDriveLetter = null;
                Assert.False(_manager.IsMounted(driveLetter));
            }
        }

        [Fact]
        public async Task Test_EndToEnd_RandomAccessSeekAndMultipleHandles()
        {
            if (!OpenMount.Core.FileSystem.WinFspInstaller.IsInstalled())
            {
                // Skip live driver mount in environments without WinFsp kernel driver
                return;
            }

            string driveLetter = FindAvailableDriveLetter();
            _mountedDriveLetter = driveLetter;

            var config = new DriveConfig
            {
                DriveLetter = driveLetter,
                Label = "E2E Seek Test",
                ProviderType = "local"
            };

            var provider = new LocalStorageProvider(_testFolder, "E2E Seek Store");
            var context = await _manager.MountAsync(config, provider);
            Assert.NotNull(context);

            string root = driveLetter + "\\";
            await Task.Delay(250);

            try
            {
                string filePath = Path.Combine(root, "seek_test.bin");

                // 1. Write file with multiple concurrent FileStream handles (FileShare.ReadWrite)
                using (var fs1 = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
                using (var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    byte[] initial = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                    fs1.Write(initial, 0, initial.Length);
                    fs1.Flush();

                    // Random-access write at offset 2 via handle 2
                    fs2.Seek(2, SeekOrigin.Begin);
                    fs2.Write(new byte[] { 99, 100 }, 0, 2);
                    fs2.Flush();

                    // Truncate via handle 1
                    fs1.SetLength(4);
                    fs1.Flush();
                }

                // 2. Read back and verify bytes
                byte[] result = File.ReadAllBytes(filePath);
                Assert.Equal(4, result.Length);
                Assert.Equal(new byte[] { 1, 2, 99, 100 }, result);
            }
            finally
            {
                await _manager.UnmountAsync(driveLetter);
                _mountedDriveLetter = null;
                Assert.False(_manager.IsMounted(driveLetter));
            }
        }
    }
}
