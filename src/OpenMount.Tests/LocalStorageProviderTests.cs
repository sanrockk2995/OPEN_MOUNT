using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMount.Core.Storage.Local;
using Xunit;

namespace OpenMount.Tests
{
    public class LocalStorageProviderTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly LocalStorageProvider _provider;

        public LocalStorageProviderTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "OpenMount_Tests_Local_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
            _provider = new LocalStorageProvider(_testRoot, "Test Local Store");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, recursive: true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task Test_VolumeInfo_ReturnsValidSizes()
        {
            var vol = await _provider.GetVolumeInfoAsync();
            Assert.NotNull(vol);
            Assert.True(vol.TotalSize > 0);
            Assert.Equal("Test Local Store", vol.VolumeLabel);
        }

        [Fact]
        public async Task Test_CreateAndReadFile_RoundTrip()
        {
            string vPath = @"\test_file.txt";
            string content = "Hello OpenMount! High performance virtual filesystem.";
            byte[] bytes = Encoding.UTF8.GetBytes(content);

            using (var ms = new MemoryStream(bytes))
            {
                await _provider.CreateFileAsync(vPath, ms);
            }

            var meta = await _provider.GetMetadataAsync(vPath);
            Assert.NotNull(meta);
            Assert.False(meta.IsDirectory);
            Assert.Equal(bytes.Length, meta.Size);

            using var readStream = await _provider.OpenReadAsync(vPath, 0, null);
            using var reader = new StreamReader(readStream);
            string readContent = await reader.ReadToEndAsync();
            Assert.Equal(content, readContent);
        }

        [Fact]
        public async Task Test_PartialRead_RangeSlice()
        {
            string vPath = @"\numbers.dat";
            byte[] numbers = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

            using (var ms = new MemoryStream(numbers))
            {
                await _provider.CreateFileAsync(vPath, ms);
            }

            // Read range offset 10, length 20
            using var readStream = await _provider.OpenReadAsync(vPath, 10, 20);
            byte[] buffer = new byte[20];
            int read = await readStream.ReadAsync(buffer, 0, 20);

            Assert.Equal(20, read);
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal((byte)(10 + i), buffer[i]);
            }
        }

        [Fact]
        public async Task Test_DirectoryOperations_CreateListDelete()
        {
            string dirPath = @"\documents\subfolder";
            await _provider.CreateDirectoryAsync(dirPath);

            var meta = await _provider.GetMetadataAsync(dirPath);
            Assert.NotNull(meta);
            Assert.True(meta.IsDirectory);

            // Add files inside
            byte[] dummy = Encoding.UTF8.GetBytes("sample text");
            using (var ms = new MemoryStream(dummy))
            {
                await _provider.CreateFileAsync(dirPath + @"\file1.txt", ms);
            }

            var list = await _provider.ListDirectoryAsync(dirPath);
            Assert.Single(list);
            Assert.Equal("file1.txt", list[0].Name);

            // Move file
            await _provider.MoveAsync(dirPath + @"\file1.txt", dirPath + @"\renamed.txt");
            var listAfterMove = await _provider.ListDirectoryAsync(dirPath);
            Assert.Single(listAfterMove);
            Assert.Equal("renamed.txt", listAfterMove[0].Name);

            // Delete file and dir
            await _provider.DeleteFileAsync(dirPath + @"\renamed.txt");
            await _provider.DeleteDirectoryAsync(dirPath, recursive: false);

            var metaAfterDelete = await _provider.GetMetadataAsync(dirPath);
            Assert.Null(metaAfterDelete);
        }

        [Fact]
        public async Task Test_LargeFile_SequentialAndSeek()
        {
            string vPath = @"\large_5mb.bin";
            int size = 5 * 1024 * 1024; // 5 MB
            byte[] data = new byte[size];
            new Random(42).NextBytes(data);

            using (var ms = new MemoryStream(data))
            {
                await _provider.CreateFileAsync(vPath, ms);
            }

            var meta = await _provider.GetMetadataAsync(vPath);
            Assert.NotNull(meta);
            Assert.Equal(size, meta.Size);

            // Seek near end and verify bytes
            long offset = size - 1000;
            using var stream = await _provider.OpenReadAsync(vPath, offset, 1000);
            byte[] readBack = new byte[1000];
            int read = await stream.ReadAsync(readBack, 0, 1000);
            Assert.Equal(1000, read);

            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal(data[offset + i], readBack[i]);
            }
        }
    }
}
