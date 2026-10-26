using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;

namespace OpenMount.Core.Storage.Ftp
{
    public class FtpStorageProvider : IStorageProvider
    {
        private readonly FtpStorageConfig _config;
        private readonly AsyncFtpClient _client;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public string ProviderId => "ftp";
        public string DisplayName { get; }

        public FtpStorageProvider(FtpStorageConfig config, string? displayName = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            DisplayName = displayName ?? "FTP";

            var encryption = FtpEncryptionMode.None;
            if (string.Equals(_config.Encryption, "Explicit", StringComparison.OrdinalIgnoreCase))
            {
                encryption = FtpEncryptionMode.Explicit;
            }
            else if (string.Equals(_config.Encryption, "Implicit", StringComparison.OrdinalIgnoreCase))
            {
                encryption = FtpEncryptionMode.Implicit;
            }

            _client = new AsyncFtpClient(_config.Host, _config.Username, _config.Password, _config.Port);
            _client.Config.EncryptionMode = encryption;
            _client.Config.ConnectTimeout = 15000;
            _client.Config.ReadTimeout = 30000;
            _client.Config.DataConnectionType = FtpDataConnectionType.PASV;
        }

        private string ToFtpPath(string path)
        {
            string clean = path.Replace('\\', '/').Trim('/');
            string root = (_config.RootPath ?? "/").TrimEnd('/');
            if (string.IsNullOrEmpty(clean))
            {
                return string.IsNullOrEmpty(root) ? "/" : root;
            }
            return $"{root}/{clean}";
        }

        private async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (!_client.IsConnected)
            {
                await _semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!_client.IsConnected)
                    {
                        await _client.AutoConnect(ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
        }

        public Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default)
        {
            ulong totalSize = 10UL * 1024 * 1024 * 1024 * 1024;
            ulong freeSize = 5UL * 1024 * 1024 * 1024 * 1024;

            string label = DisplayName;
            if (label.Length > 31) label = label.Substring(0, 31);

            return Task.FromResult(new StorageVolumeInfo
            {
                TotalSize = totalSize,
                FreeSize = freeSize,
                VolumeLabel = label,
                FileSystemName = "NTFS"
            });
        }

        public async Task<StorageItemMetadata?> GetMetadataAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            if (clean == "\\")
            {
                return new StorageItemMetadata
                {
                    Path = "\\",
                    Name = "\\",
                    IsDirectory = true,
                    CreatedTime = DateTimeOffset.UtcNow,
                    LastAccessTime = DateTimeOffset.UtcNow,
                    LastModifiedTime = DateTimeOffset.UtcNow,
                    Attributes = FileAttributes.Directory
                };
            }

            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string ftpPath = ToFtpPath(clean);

            try
            {
                await _semaphore.WaitAsync(ct).ConfigureAwait(false);
                FtpListItem? item;
                try
                {
                    item = await _client.GetObjectInfo(ftpPath, dateModified: true, ct).ConfigureAwait(false);
                }
                finally
                {
                    _semaphore.Release();
                }

                if (item == null) return null;

                bool isDir = item.Type == FtpObjectType.Directory;
                string name = Path.GetFileName(clean);

                return new StorageItemMetadata
                {
                    Path = clean,
                    Name = name,
                    Size = isDir ? 0 : item.Size,
                    IsDirectory = isDir,
                    CreatedTime = item.Modified,
                    LastAccessTime = item.Modified,
                    LastModifiedTime = item.Modified,
                    Attributes = isDir ? FileAttributes.Directory : FileAttributes.Normal
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string ftpPath = ToFtpPath(clean);

            var results = new List<StorageItemMetadata>();

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            FtpListItem[] items;
            try
            {
                items = await _client.GetListing(ftpPath, FtpListOption.Auto, ct).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }

            foreach (var item in items)
            {
                if (item.Name == "." || item.Name == "..") continue;

                bool isDir = item.Type == FtpObjectType.Directory;
                string itemPath = clean == "\\" ? $"\\{item.Name}" : $"{clean}\\{item.Name}";

                results.Add(new StorageItemMetadata
                {
                    Path = itemPath,
                    Name = item.Name,
                    Size = isDir ? 0 : item.Size,
                    IsDirectory = isDir,
                    CreatedTime = item.Modified,
                    LastAccessTime = item.Modified,
                    LastModifiedTime = item.Modified,
                    Attributes = isDir ? FileAttributes.Directory : FileAttributes.Normal
                });
            }

            return results;
        }

        public async Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string ftpPath = ToFtpPath(clean);

            Stream stream;
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                stream = await _client.OpenRead(ftpPath, FtpDataType.Binary, offset, token: ct).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }

            if (length.HasValue)
            {
                return new BoundedStream(stream, length.Value);
            }

            return stream;
        }

        public async Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string ftpPath = ToFtpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _client.UploadStream(contentStream, ftpPath, FtpRemoteExists.Overwrite, createRemoteDir: true, token: ct).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string ftpPath = ToFtpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _client.CreateDirectory(ftpPath, ct).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string ftpPath = ToFtpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _client.DeleteFile(ftpPath, ct).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string ftpPath = ToFtpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (recursive)
                {
                    await _client.DeleteDirectory(ftpPath, ct).ConfigureAwait(false);
                }
                else
                {
                    await _client.DeleteDirectory(ftpPath, FtpListOption.Auto, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            string cleanSrc = NormalizePath(sourcePath);
            string cleanDst = NormalizePath(destinationPath);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);

            string ftpSrc = ToFtpPath(cleanSrc);
            string ftpDst = ToFtpPath(cleanDst);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bool moved = await _client.MoveFile(ftpSrc, ftpDst, FtpRemoteExists.Overwrite, ct).ConfigureAwait(false);
                if (!moved)
                {
                    await _client.MoveDirectory(ftpSrc, ftpDst, FtpRemoteExists.Overwrite, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";
            string clean = path.Replace('/', '\\');
            return clean.StartsWith("\\") ? clean : "\\" + clean;
        }

        public async ValueTask DisposeAsync()
        {
            if (_client.IsConnected)
            {
                await _client.Disconnect().ConfigureAwait(false);
            }
            _client.Dispose();
            _semaphore.Dispose();
        }

        private class BoundedStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _maxBytes;
            private long _bytesRead;

            public BoundedStream(Stream inner, long maxBytes)
            {
                _inner = inner;
                _maxBytes = maxBytes;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => Math.Min(_inner.Length, _maxBytes);
            public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_bytesRead >= _maxBytes) return 0;
                int toRead = (int)Math.Min(count, _maxBytes - _bytesRead);
                int read = _inner.Read(buffer, offset, toRead);
                _bytesRead += read;
                return read;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if (_bytesRead >= _maxBytes) return 0;
                int toRead = (int)Math.Min(count, _maxBytes - _bytesRead);
                int read = await _inner.ReadAsync(buffer, offset, toRead, ct).ConfigureAwait(false);
                _bytesRead += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
