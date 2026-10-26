using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using OpenMount.Core.Cache;
using OpenMount.Core.Manager;
using OpenMount.Core.Monitoring;
using OpenMount.Core.Storage;
using OpenMount.Core.Storage.Local;
using OpenMount.Core.Sync;
using Xunit;

namespace OpenMount.Tests
{
    public class AdvancedRaiDriveFeaturesTests : IDisposable
    {
        private readonly string _tempDir;

        public AdvancedRaiDriveFeaturesTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "OpenMountTests_RaiDrive_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void FolderPropertiesCache_CalculatesAndExpiresAccurately()
        {
            var cache = new FolderPropertiesCache();

            var items = new[]
            {
                new StorageItemMetadata { Name = "doc.pdf", Path = "\\docs\\doc.pdf", Size = 1024, IsDirectory = false },
                new StorageItemMetadata { Name = "pic.png", Path = "\\docs\\pic.png", Size = 2048, IsDirectory = false },
                new StorageItemMetadata { Name = "sub", Path = "\\docs\\sub", Size = 0, IsDirectory = true }
            };

            cache.Update("\\docs", items);

            bool found = cache.TryGetProperties("\\docs", out var props);
            Assert.True(found);
            Assert.NotNull(props);
            Assert.Equal(3072, props!.TotalSizeBytes);
            Assert.Equal(2, props.FileCount);
            Assert.Equal(1, props.DirectoryCount);

            // Invalidation of subfolder also invalidates parent
            cache.Invalidate("\\docs\\sub");
            bool parentFoundAfterInvalidate = cache.TryGetProperties("\\docs", out _);
            Assert.False(parentFoundAfterInvalidate);
        }

        [Fact]
        public async Task AsyncUploadQueue_ProcessesAndDrainsSuccessfully()
        {
            string stagedFile = Path.Combine(_tempDir, "async_upload_test.dat");
            File.WriteAllText(stagedFile, "Async upload content payload");

            var mockStorage = new Mock<IStorageProvider>();
            var uploadTcs = new TaskCompletionSource<bool>();

            mockStorage.Setup(s => s.CreateFileAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
                .Returns<string, Stream, CancellationToken>((path, stream, ct) =>
                {
                    uploadTcs.TrySetResult(true);
                    return Task.CompletedTask;
                });

            bool callbackFired = false;
            await using var queue = new AsyncUploadQueue(mockStorage.Object, p => callbackFired = true, maxConcurrentWorkers: 1);

            bool enqueued = queue.Enqueue("\\remote\\file.dat", stagedFile);
            Assert.True(enqueued);

            // In-flight read-your-own-writes check
            bool hasInFlight = queue.TryGetInFlightLocalPath("\\remote\\file.dat", out var inFlightPath);
            Assert.True(hasInFlight);
            Assert.Equal(stagedFile, inFlightPath);

            var completedTask = await Task.WhenAny(uploadTcs.Task, Task.Delay(5000));
            Assert.Equal(uploadTcs.Task, completedTask);

            // Give worker loop a tick to finalize
            await Task.Delay(100);
            Assert.True(callbackFired);
        }

        [Fact]
        public void ProcessAccessMonitor_RecordsAndLimitsRecentEvents()
        {
            var monitor = new ProcessAccessMonitor(isEnabled: true);

            for (int i = 1; i <= 250; i++)
            {
                monitor.RecordAccess(processId: 1000 + (i % 5), filePath: $"\\folder\\file{i}.txt", operation: "Read");
            }

            var events = monitor.GetRecentEvents();
            Assert.True(events.Length <= 200, "Event buffer should be bounded to 200 items maximum.");
            Assert.NotEmpty(events);

            int currentPid = Environment.ProcessId;
            string selfName = monitor.GetProcessName(currentPid);
            Assert.NotEmpty(selfName);

            Assert.False(monitor.IsPreviewProcess(currentPid));
        }

        [Fact]
        public async Task LocalStorageProvider_QuickCopy_AllConflictModesWork()
        {
            var provider = new LocalStorageProvider(_tempDir);
            await provider.InitializeAsync();

            string src = "\\source.txt";
            string dst = "\\dest.txt";

            using (var srcStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Original data")))
            {
                await provider.CreateFileAsync(src, srcStream);
            }

            // 1. Copy fresh
            await provider.CopyAsync(src, dst, ConflictResolutionMode.OverwriteTarget);
            var dstMeta = await provider.GetMetadataAsync(dst);
            Assert.NotNull(dstMeta);
            Assert.Equal(13, dstMeta!.Size);

            // 2. Conflict: Skip
            using (var newSrcStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Modified data 1234567890")))
            {
                await provider.CreateFileAsync(src, newSrcStream);
            }
            await provider.CopyAsync(src, dst, ConflictResolutionMode.Skip);
            var skippedMeta = await provider.GetMetadataAsync(dst);
            Assert.Equal(13, skippedMeta!.Size); // Still original 13 bytes

            // 3. Conflict: OverwriteTarget
            await provider.CopyAsync(src, dst, ConflictResolutionMode.OverwriteTarget);
            var overwrittenMeta = await provider.GetMetadataAsync(dst);
            Assert.Equal(24, overwrittenMeta!.Size); // Overwritten with 24 bytes

            // 4. Conflict: RenameSource
            await provider.CopyAsync(src, dst, ConflictResolutionMode.RenameSource);
            var renamedMeta = await provider.GetMetadataAsync("\\dest (1).txt");
            Assert.NotNull(renamedMeta);
            Assert.Equal(24, renamedMeta!.Size);

            await provider.DisposeAsync();
        }

        [Fact]
        public async Task LocalStorageProvider_SetLastModifiedTimeAsync_PersistsTime()
        {
            var provider = new LocalStorageProvider(_tempDir);
            await provider.InitializeAsync();

            string testFile = "\\time_test.txt";
            using (var ms = new MemoryStream(new byte[] { 1, 2, 3 }))
            {
                await provider.CreateFileAsync(testFile, ms);
            }

            var targetTime = new DateTimeOffset(2025, 5, 20, 14, 30, 0, TimeSpan.Zero);
            await provider.SetLastModifiedTimeAsync(testFile, targetTime);

            var meta = await provider.GetMetadataAsync(testFile);
            Assert.NotNull(meta);
            Assert.Equal(targetTime.ToUnixTimeSeconds(), meta!.LastModifiedTime.ToUnixTimeSeconds());

            await provider.DisposeAsync();
        }

        [Fact]
        public void DriveConfig_RaiDriveOptionsDefaults_AreAccurate()
        {
            var cfg = new DriveConfig();
            Assert.True(cfg.EnableFilePreview);
            Assert.False(cfg.InstantProperties);
            Assert.False(cfg.AsyncUpload);
            Assert.True(cfg.MaintainDateModified);
            Assert.False(cfg.MonitorProcessAccess);
            Assert.True(cfg.QuickRemoteOperations);
            Assert.Equal(ConflictResolutionMode.OverwriteTarget, cfg.ConflictResolution);
        }
    }
}
