using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fsp.Interop;
using Moq;
using OpenMount.Core.Cache;
using OpenMount.Core.FileSystem;
using OpenMount.Core.Storage;
using Xunit;

namespace OpenMount.Tests
{
    public class FileCacheManagerAndStreamingTests : IDisposable
    {
        private readonly string _testCacheDir;

        public FileCacheManagerAndStreamingTests()
        {
            _testCacheDir = Path.Combine(Path.GetTempPath(), "OpenMountTests", "cache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testCacheDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testCacheDir))
                {
                    Directory.Delete(_testCacheDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task FileCacheManager_DownloadsAndCaches_SeekableFileStream()
        {
            var cacheManager = new FileCacheManager("test-drive", _testCacheDir);
            string virtualPath = "\\test_document.txt";
            byte[] remoteBytes = Encoding.UTF8.GetBytes("Hello World from Virtual Cloud Storage! This must support random seeking.");
            int downloadCount = 0;

            Func<long, long?, CancellationToken, Task<Stream>> remoteStreamFactory = (offset, length, ct) =>
            {
                downloadCount++;
                return Task.FromResult<Stream>(new MemoryStream(remoteBytes));
            };

            // First access: downloads and creates local cache
            string localPath1 = await cacheManager.EnsureFileCachedAsync(
                virtualPath,
                remoteBytes.Length,
                DateTimeOffset.UtcNow,
                remoteStreamFactory);

            Assert.True(File.Exists(localPath1));
            Assert.Equal(1, downloadCount);

            // Verify the local file supports random seeking
            using (var fs = new FileStream(localPath1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Assert.True(fs.CanSeek);
                fs.Seek(6, SeekOrigin.Begin);
                byte[] buf = new byte[5];
                int read = fs.Read(buf, 0, 5);
                Assert.Equal(5, read);
                Assert.Equal("World", Encoding.UTF8.GetString(buf));
            }

            // Second access: cache hit, remote stream should NOT be called again
            string localPath2 = await cacheManager.EnsureFileCachedAsync(
                virtualPath,
                remoteBytes.Length,
                DateTimeOffset.UtcNow,
                remoteStreamFactory);

            Assert.Equal(localPath1, localPath2);
            Assert.Equal(1, downloadCount); // downloadCount must remain 1
        }

        [Fact]
        public void VirtualFileSystem_Open_IsLazy_AndDoesNotFetchRemoteStream()
        {
            var mockStorage = new Mock<IStorageProvider>();
            mockStorage.Setup(s => s.ProviderId).Returns("mock");
            mockStorage.Setup(s => s.GetMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StorageItemMetadata
                {
                    Path = "\\document.pdf",
                    Name = "document.pdf",
                    Size = 1024 * 1024,
                    IsDirectory = false
                });

            var cacheManager = new FileCacheManager("mock-drive", _testCacheDir);
            var vfs = new VirtualFileSystem(mockStorage.Object, null, cacheManager);

            int status = vfs.Open(
                "\\document.pdf",
                0,
                0x80000000, // GENERIC_READ
                out object fileNode,
                out object fileDesc,
                out _,
                out _);

            Assert.Equal(0, status); // STATUS_SUCCESS
            Assert.NotNull(fileNode);
            Assert.NotNull(fileDesc);

            // Verify OpenReadAsync was NOT called during Open (lazy evaluation)
            mockStorage.Verify(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void VirtualFileSystem_Read_RandomSeek_SucceedsWithoutException()
        {
            byte[] fileData = Encoding.UTF8.GetBytes("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

            var mockStorage = new Mock<IStorageProvider>();
            mockStorage.Setup(s => s.ProviderId).Returns("mock");
            mockStorage.Setup(s => s.GetMetadataAsync("\\data.bin", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StorageItemMetadata
                {
                    Path = "\\data.bin",
                    Name = "data.bin",
                    Size = fileData.Length,
                    IsDirectory = false
                });

            // Return non-seekable stream (simulating HTTP network response)
            mockStorage.Setup(s => s.OpenReadAsync("\\data.bin", It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new NonSeekableStream(new MemoryStream(fileData)));

            var cacheManager = new FileCacheManager("mock-drive", _testCacheDir);
            var vfs = new VirtualFileSystem(mockStorage.Object, null, cacheManager);

            int openStatus = vfs.Open(
                "\\data.bin",
                0,
                0x80000000,
                out object fileNode,
                out object fileDesc,
                out _,
                out _);

            Assert.Equal(0, openStatus);

            // Read at offset 10, length 10 ("ABCDEFGHIJ")
            IntPtr buffer = Marshal.AllocHGlobal(10);
            try
            {
                int readStatus = vfs.Read(
                    fileNode,
                    fileDesc,
                    buffer,
                    10, // offset
                    10, // length
                    out uint bytesTransferred);

                Assert.Equal(0, readStatus); // STATUS_SUCCESS
                Assert.Equal(10u, bytesTransferred);

                byte[] readBytes = new byte[10];
                Marshal.Copy(buffer, readBytes, 0, 10);
                Assert.Equal("ABCDEFGHIJ", Encoding.UTF8.GetString(readBytes));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                vfs.Close(fileNode, fileDesc);
            }
        }

        [Fact]
        public void VirtualFileSystem_WriteAndCleanup_UploadsModifiedContentToStorage()
        {
            var mockStorage = new Mock<IStorageProvider>();
            mockStorage.Setup(s => s.ProviderId).Returns("mock");

            byte[]? uploadedBytes = null;
            mockStorage.Setup(s => s.CreateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback<string, Stream, CancellationToken>((path, stream, ct) =>
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    uploadedBytes = ms.ToArray();
                })
                .Returns(Task.CompletedTask);

            var cacheManager = new FileCacheManager("mock-drive", _testCacheDir);
            var vfs = new VirtualFileSystem(mockStorage.Object, null, cacheManager);

            // Create new file
            int createStatus = vfs.Create(
                "\\new_file.txt",
                0,
                0x40000000, // GENERIC_WRITE
                (uint)FileAttributes.Normal,
                Array.Empty<byte>(),
                0,
                out object fileNode,
                out object fileDesc,
                out _,
                out _);

            Assert.Equal(0, createStatus);

            string contentToWrite = "Content created in OpenMount virtual cloud drive!";
            byte[] bytesToWrite = Encoding.UTF8.GetBytes(contentToWrite);
            IntPtr buffer = Marshal.AllocHGlobal(bytesToWrite.Length);
            try
            {
                Marshal.Copy(bytesToWrite, 0, buffer, bytesToWrite.Length);

                int writeStatus = vfs.Write(
                    fileNode,
                    fileDesc,
                    buffer,
                    0,
                    (uint)bytesToWrite.Length,
                    false,
                    false,
                    out uint written,
                    out _);

                Assert.Equal(0, writeStatus);
                Assert.Equal((uint)bytesToWrite.Length, written);

                // Call Cleanup (simulates application closing the handle after save)
                vfs.Cleanup(fileNode, fileDesc, "\\new_file.txt", 0);

                // Verify content was uploaded to remote storage
                Assert.NotNull(uploadedBytes);
                Assert.Equal(contentToWrite, Encoding.UTF8.GetString(uploadedBytes));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                vfs.Close(fileNode, fileDesc);
            }
        }

        /// <summary>
        /// A stream wrapper where CanSeek is false, exactly simulating an HTTP response stream from HttpClient.
        /// </summary>
        private class NonSeekableStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("HTTP streams cannot seek!");
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
