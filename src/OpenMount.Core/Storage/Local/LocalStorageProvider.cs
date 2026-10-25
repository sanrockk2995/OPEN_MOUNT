using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenMount.Core.Storage.Local
{
    public class LocalStorageProvider : IStorageProvider
    {
        private readonly string _rootDirectory;

        public string ProviderId => "local";
        public string DisplayName { get; }

        public LocalStorageProvider(string rootDirectory, string displayName = "Local Virtual Mount")
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentNullException(nameof(rootDirectory));

            _rootDirectory = Path.GetFullPath(rootDirectory);
            DisplayName = displayName;
        }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            if (!Directory.Exists(_rootDirectory))
            {
                Directory.CreateDirectory(_rootDirectory);
            }
            return Task.CompletedTask;
        }

        private string ResolveLocalPath(string virtualPath)
        {
            string clean = virtualPath.TrimStart('\\', '/').Replace('/', '\\');
            return Path.Combine(_rootDirectory, clean);
        }

        private string ToVirtualPath(string fullLocalPath)
        {
            if (fullLocalPath.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                string rel = fullLocalPath.Substring(_rootDirectory.Length);
                if (string.IsNullOrEmpty(rel)) return "\\";
                return rel.StartsWith("\\") ? rel : "\\" + rel;
            }
            return "\\" + Path.GetFileName(fullLocalPath);
        }

        public Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default)
        {
            try
            {
                string root = Path.GetPathRoot(_rootDirectory) ?? "C:\\";
                var drive = new DriveInfo(root);
                return Task.FromResult(new StorageVolumeInfo
                {
                    TotalSize = (ulong)drive.TotalSize,
                    FreeSize = (ulong)drive.AvailableFreeSpace,
                    VolumeLabel = DisplayName,
                    FileSystemName = "NTFS"
                });
            }
            catch
            {
                return Task.FromResult(new StorageVolumeInfo
                {
                    TotalSize = 100UL * 1024 * 1024 * 1024,
                    FreeSize = 50UL * 1024 * 1024 * 1024,
                    VolumeLabel = DisplayName,
                    FileSystemName = "NTFS"
                });
            }
        }

        public Task<StorageItemMetadata?> GetMetadataAsync(string path, CancellationToken ct = default)
        {
            string local = ResolveLocalPath(path);
            if (File.Exists(local))
            {
                var fi = new FileInfo(local);
                return Task.FromResult<StorageItemMetadata?>(new StorageItemMetadata
                {
                    Path = path,
                    Name = fi.Name,
                    Size = fi.Length,
                    IsDirectory = false,
                    CreatedTime = fi.CreationTimeUtc,
                    LastModifiedTime = fi.LastWriteTimeUtc,
                    LastAccessTime = fi.LastAccessTimeUtc,
                    Attributes = fi.Attributes
                });
            }

            if (Directory.Exists(local))
            {
                var di = new DirectoryInfo(local);
                return Task.FromResult<StorageItemMetadata?>(new StorageItemMetadata
                {
                    Path = path,
                    Name = di.Name,
                    Size = 0,
                    IsDirectory = true,
                    CreatedTime = di.CreationTimeUtc,
                    LastModifiedTime = di.LastWriteTimeUtc,
                    LastAccessTime = di.LastAccessTimeUtc,
                    Attributes = di.Attributes | FileAttributes.Directory
                });
            }

            return Task.FromResult<StorageItemMetadata?>(null);
        }

        public Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct = default)
        {
            string local = ResolveLocalPath(path);
            var list = new List<StorageItemMetadata>();

            if (!Directory.Exists(local))
                return Task.FromResult<IReadOnlyList<StorageItemMetadata>>(list);

            var di = new DirectoryInfo(local);
            foreach (var entry in di.EnumerateFileSystemInfos())
            {
                bool isDir = (entry.Attributes & FileAttributes.Directory) != 0;
                long size = isDir ? 0 : ((FileInfo)entry).Length;
                string vPath = ToVirtualPath(entry.FullName);

                list.Add(new StorageItemMetadata
                {
                    Path = vPath,
                    Name = entry.Name,
                    Size = size,
                    IsDirectory = isDir,
                    CreatedTime = entry.CreationTimeUtc,
                    LastModifiedTime = entry.LastWriteTimeUtc,
                    LastAccessTime = entry.LastAccessTimeUtc,
                    Attributes = entry.Attributes
                });
            }

            return Task.FromResult<IReadOnlyList<StorageItemMetadata>>(list);
        }

        public Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct = default)
        {
            string local = ResolveLocalPath(path);
            if (!File.Exists(local))
                throw new FileNotFoundException("File not found", local);

            var fs = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (offset > 0)
            {
                fs.Seek(offset, SeekOrigin.Begin);
            }

            Stream s = fs;
            if (length.HasValue)
            {
                s = new SlicedStream(fs, length.Value);
            }

            return Task.FromResult(s);
        }

        public async Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct = default)
        {
            string local = ResolveLocalPath(path);
            string? dir = Path.GetDirectoryName(local);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var fs = new FileStream(local, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await contentStream.CopyToAsync(fs, 81920, ct).ConfigureAwait(false);
            }
        }

        public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            string local = ResolveLocalPath(path);
            if (!Directory.Exists(local))
            {
                Directory.CreateDirectory(local);
            }
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            string local = ResolveLocalPath(path);
            if (File.Exists(local))
            {
                File.Delete(local);
            }
            return Task.CompletedTask;
        }

        public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            string local = ResolveLocalPath(path);
            if (Directory.Exists(local))
            {
                Directory.Delete(local, recursive);
            }
            return Task.CompletedTask;
        }

        public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            string srcLocal = ResolveLocalPath(sourcePath);
            string dstLocal = ResolveLocalPath(destinationPath);

            string? parent = Path.GetDirectoryName(dstLocal);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (File.Exists(srcLocal))
            {
                if (File.Exists(dstLocal))
                    File.Delete(dstLocal);
                File.Move(srcLocal, dstLocal);
            }
            else if (Directory.Exists(srcLocal))
            {
                if (Directory.Exists(dstLocal))
                    Directory.Delete(dstLocal, true);
                Directory.Move(srcLocal, dstLocal);
            }
            else
            {
                throw new FileNotFoundException("Source item does not exist", srcLocal);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    internal class SlicedStream : Stream
    {
        private readonly Stream _underlying;
        private readonly long _maxBytes;
        private long _bytesRead;

        public SlicedStream(Stream underlying, long maxBytes)
        {
            _underlying = underlying;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => _underlying.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => Math.Min(_maxBytes, _underlying.Length);
        public override long Position
        {
            get => _bytesRead;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _underlying.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_bytesRead >= _maxBytes) return 0;
            int toRead = (int)Math.Min(count, _maxBytes - _bytesRead);
            int n = _underlying.Read(buffer, offset, toRead);
            _bytesRead += n;
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_bytesRead >= _maxBytes) return 0;
            int toRead = (int)Math.Min(count, _maxBytes - _bytesRead);
            int n = await _underlying.ReadAsync(buffer, offset, toRead, cancellationToken).ConfigureAwait(false);
            _bytesRead += n;
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _underlying.Dispose();
            base.Dispose(disposing);
        }
    }
}
