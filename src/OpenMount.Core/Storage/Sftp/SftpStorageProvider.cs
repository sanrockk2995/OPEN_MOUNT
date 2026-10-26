using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace OpenMount.Core.Storage.Sftp
{
    public class SftpStorageProvider : IStorageProvider
    {
        private readonly SftpStorageConfig _config;
        private readonly SftpClient _client;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public string ProviderId => "sftp";
        public string DisplayName { get; }

        public SftpStorageProvider(SftpStorageConfig config, string? displayName = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            DisplayName = displayName ?? "SFTP";

            var authMethods = new List<AuthenticationMethod>();
            if (!string.IsNullOrEmpty(_config.Password))
            {
                authMethods.Add(new PasswordAuthenticationMethod(_config.Username, _config.Password));
            }

            if (!string.IsNullOrEmpty(_config.PrivateKeyPath) && File.Exists(_config.PrivateKeyPath))
            {
                var keyFile = string.IsNullOrEmpty(_config.PrivateKeyPassphrase)
                    ? new PrivateKeyFile(_config.PrivateKeyPath)
                    : new PrivateKeyFile(_config.PrivateKeyPath, _config.PrivateKeyPassphrase);
                authMethods.Add(new PrivateKeyAuthenticationMethod(_config.Username, keyFile));
            }

            var connInfo = new ConnectionInfo(_config.Host, _config.Port, _config.Username, authMethods.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _client = new SftpClient(connInfo);
        }

        private string ToSftpPath(string path)
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
                        await Task.Run(() => _client.Connect(), ct).ConfigureAwait(false);
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

        public async Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);

            ulong totalSize = 10UL * 1024 * 1024 * 1024 * 1024;
            ulong freeSize = 5UL * 1024 * 1024 * 1024 * 1024;

            try
            {
                await _semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var status = _client.GetStatus(ToSftpPath("\\"));
                    if (status != null && status.BlockSize > 0)
                    {
                        totalSize = status.TotalBlocks * status.BlockSize;
                        freeSize = status.AvailableBlocks * status.BlockSize;
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch { }

            string label = DisplayName;
            if (label.Length > 31) label = label.Substring(0, 31);

            return new StorageVolumeInfo
            {
                TotalSize = totalSize,
                FreeSize = freeSize,
                VolumeLabel = label,
                FileSystemName = "NTFS"
            };
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
            string sftpPath = ToSftpPath(clean);

            try
            {
                await _semaphore.WaitAsync(ct).ConfigureAwait(false);
                SftpFileAttributes attrs;
                try
                {
                    attrs = _client.GetAttributes(sftpPath);
                }
                finally
                {
                    _semaphore.Release();
                }

                string name = Path.GetFileName(clean);
                return new StorageItemMetadata
                {
                    Path = clean,
                    Name = name,
                    Size = attrs.IsDirectory ? 0 : attrs.Size,
                    IsDirectory = attrs.IsDirectory,
                    CreatedTime = attrs.LastWriteTimeUtc,
                    LastAccessTime = attrs.LastAccessTimeUtc,
                    LastModifiedTime = attrs.LastWriteTimeUtc,
                    Attributes = attrs.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal
                };
            }
            catch (Renci.SshNet.Common.SftpPathNotFoundException)
            {
                return null;
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
            string sftpPath = ToSftpPath(clean);

            var results = new List<StorageItemMetadata>();

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            IEnumerable<ISftpFile> items;
            try
            {
                items = _client.ListDirectory(sftpPath).ToList();
            }
            finally
            {
                _semaphore.Release();
            }

            foreach (var item in items)
            {
                if (item.Name == "." || item.Name == "..") continue;

                string itemPath = clean == "\\" ? $"\\{item.Name}" : $"{clean}\\{item.Name}";
                results.Add(new StorageItemMetadata
                {
                    Path = itemPath,
                    Name = item.Name,
                    Size = item.IsDirectory ? 0 : item.Attributes.Size,
                    IsDirectory = item.IsDirectory,
                    CreatedTime = item.Attributes.LastWriteTimeUtc,
                    LastAccessTime = item.Attributes.LastAccessTimeUtc,
                    LastModifiedTime = item.Attributes.LastWriteTimeUtc,
                    Attributes = item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal
                });
            }

            return results;
        }

        public async Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            string sftpPath = ToSftpPath(clean);

            SftpFileStream stream;
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                stream = _client.Open(sftpPath, FileMode.Open, FileAccess.Read);
                if (offset > 0)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                }
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
            string sftpPath = ToSftpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var target = _client.Create(sftpPath);
                await contentStream.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
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
            string sftpPath = ToSftpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _client.CreateDirectory(sftpPath);
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
            string sftpPath = ToSftpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _client.DeleteFile(sftpPath);
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
            string sftpPath = ToSftpPath(clean);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (recursive)
                {
                    DeleteDirectoryRecursive(sftpPath);
                }
                else
                {
                    _client.DeleteDirectory(sftpPath);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void DeleteDirectoryRecursive(string path)
        {
            foreach (var file in _client.ListDirectory(path))
            {
                if (file.Name == "." || file.Name == "..") continue;
                if (file.IsDirectory)
                {
                    DeleteDirectoryRecursive(file.FullName);
                }
                else
                {
                    _client.DeleteFile(file.FullName);
                }
            }
            _client.DeleteDirectory(path);
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            string cleanSrc = NormalizePath(sourcePath);
            string cleanDst = NormalizePath(destinationPath);
            await EnsureConnectedAsync(ct).ConfigureAwait(false);

            string sftpSrc = ToSftpPath(cleanSrc);
            string sftpDst = ToSftpPath(cleanDst);

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _client.RenameFile(sftpSrc, sftpDst);
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

        public ValueTask DisposeAsync()
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
            _client.Dispose();
            _semaphore.Dispose();
            return ValueTask.CompletedTask;
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
