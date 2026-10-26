using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenMount.Core.Cache
{
    public class CacheEntry
    {
        public string VirtualPath { get; set; } = string.Empty;
        public string LocalFilePath { get; set; } = string.Empty;
        public long RemoteSize { get; set; }
        public DateTimeOffset RemoteModifiedUtc { get; set; }
        public bool IsFullyCached { get; set; }
        public bool IsDirty { get; set; }
        public DateTime LastAccessTimeUtc { get; set; } = DateTime.UtcNow;
        public readonly object SyncRoot = new object();
    }

    public class FileCacheManager
    {
        private readonly string _cacheDir;
        private readonly long _maxCacheBytes;
        private readonly ConcurrentDictionary<string, CacheEntry> _entries =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        // Files up to 64 MB are downloaded completely to local disk on first read.
        // Larger files stream on-demand using HTTP Range requests without blocking.
        public const long FullCacheSizeThreshold = 64 * 1024 * 1024;

        public string CacheDirectory => _cacheDir;

        public FileCacheManager(string? driveId = null, string? customCacheDir = null, long maxCacheBytes = 10L * 1024 * 1024 * 1024)
        {
            _maxCacheBytes = maxCacheBytes;
            string baseDir = !string.IsNullOrWhiteSpace(customCacheDir)
                ? customCacheDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenMount", "cache");

            string safeId = string.IsNullOrWhiteSpace(driveId) ? "default" : SanitizeId(driveId);
            _cacheDir = Path.Combine(baseDir, safeId);

            if (!Directory.Exists(_cacheDir))
            {
                Directory.CreateDirectory(_cacheDir);
            }
        }

        private static string SanitizeId(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : "drive";
        }

        public string GetLocalPathForVirtual(string virtualPath)
        {
            string norm = NormalizeVirtualPath(virtualPath);
            using var sha = SHA256.Create();
            byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(norm.ToLowerInvariant()));
            string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            string ext = Path.GetExtension(virtualPath);
            if (ext.Length > 10) ext = ".dat";

            string prefix = hash.Substring(0, 2);
            string subDir = Path.Combine(_cacheDir, prefix);
            if (!Directory.Exists(subDir))
            {
                Directory.CreateDirectory(subDir);
            }

            return Path.Combine(subDir, $"{hash}{ext}");
        }

        public CacheEntry GetOrCreateEntry(string virtualPath, long remoteSize = 0, DateTimeOffset remoteModified = default)
        {
            string norm = NormalizeVirtualPath(virtualPath);
            return _entries.GetOrAdd(norm, p =>
            {
                string localPath = GetLocalPathForVirtual(p);
                bool exists = File.Exists(localPath);
                long localLen = exists ? new System.IO.FileInfo(localPath).Length : 0;
                bool isFull = exists && remoteSize > 0 && localLen == remoteSize;

                return new CacheEntry
                {
                    VirtualPath = p,
                    LocalFilePath = localPath,
                    RemoteSize = remoteSize,
                    RemoteModifiedUtc = remoteModified,
                    IsFullyCached = isFull,
                    LastAccessTimeUtc = DateTime.UtcNow
                };
            });
        }

        /// <summary>
        /// Ensures a full copy of the file exists in local cache, downloading it from remote if necessary.
        /// </summary>
        public async Task<string> EnsureFileCachedAsync(
            string virtualPath,
            long remoteSize,
            DateTimeOffset remoteModified,
            Func<long, long?, CancellationToken, Task<Stream>> openRemoteStream,
            CancellationToken ct = default)
        {
            var entry = GetOrCreateEntry(virtualPath, remoteSize, remoteModified);
            entry.LastAccessTimeUtc = DateTime.UtcNow;

            lock (entry.SyncRoot)
            {
                if (entry.IsFullyCached && File.Exists(entry.LocalFilePath))
                {
                    return entry.LocalFilePath;
                }
            }

            string tempDownloading = entry.LocalFilePath + ".dl_" + Guid.NewGuid().ToString("N");
            try
            {
                using (var remoteStream = await openRemoteStream(0, null, ct).ConfigureAwait(false))
                using (var localFile = new FileStream(tempDownloading, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    await remoteStream.CopyToAsync(localFile, 65536, ct).ConfigureAwait(false);
                    await localFile.FlushAsync(ct).ConfigureAwait(false);
                }

                lock (entry.SyncRoot)
                {
                    if (File.Exists(entry.LocalFilePath))
                    {
                        File.Delete(entry.LocalFilePath);
                    }
                    File.Move(tempDownloading, entry.LocalFilePath);
                    entry.IsFullyCached = true;
                    entry.RemoteSize = new System.IO.FileInfo(entry.LocalFilePath).Length;
                    entry.RemoteModifiedUtc = remoteModified;
                }

                _ = Task.Run(() => TrimCacheIfNeeded());
                return entry.LocalFilePath;
            }
            catch
            {
                if (File.Exists(tempDownloading))
                {
                    try { File.Delete(tempDownloading); } catch { }
                }
                throw;
            }
        }

        /// <summary>
        /// Creates a local staging file for writing a new or updated file.
        /// If the file already exists on remote and has content, it copies existing content first to support partial writes/appends.
        /// </summary>
        public async Task<string> PrepareStagingFileAsync(
            string virtualPath,
            long existingRemoteSize,
            DateTimeOffset remoteModified,
            Func<long, long?, CancellationToken, Task<Stream>>? openRemoteStream,
            CancellationToken ct = default)
        {
            var entry = GetOrCreateEntry(virtualPath, existingRemoteSize, remoteModified);
            entry.LastAccessTimeUtc = DateTime.UtcNow;

            lock (entry.SyncRoot)
            {
                if (entry.IsDirty && File.Exists(entry.LocalFilePath))
                {
                    return entry.LocalFilePath;
                }
            }

            if (existingRemoteSize > 0 && openRemoteStream != null)
            {
                return await EnsureFileCachedAsync(virtualPath, existingRemoteSize, remoteModified, openRemoteStream, ct).ConfigureAwait(false);
            }

            // Fresh empty staging file
            lock (entry.SyncRoot)
            {
                using var fs = new FileStream(entry.LocalFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                entry.IsFullyCached = true;
                entry.IsDirty = true;
                entry.RemoteSize = 0;
            }

            return entry.LocalFilePath;
        }

        public void Invalidate(string virtualPath)
        {
            string norm = NormalizeVirtualPath(virtualPath);
            if (_entries.TryRemove(norm, out var entry))
            {
                lock (entry.SyncRoot)
                {
                    if (!entry.IsDirty && File.Exists(entry.LocalFilePath))
                    {
                        try { File.Delete(entry.LocalFilePath); } catch { }
                    }
                }
            }
        }

        public void InvalidateAll()
        {
            _entries.Clear();
        }

        private void TrimCacheIfNeeded()
        {
            try
            {
                var dir = new DirectoryInfo(_cacheDir);
                if (!dir.Exists) return;

                var files = dir.GetFiles("*", SearchOption.AllDirectories);
                long totalBytes = 0;
                foreach (var f in files)
                {
                    totalBytes += f.Length;
                }

                if (totalBytes <= _maxCacheBytes) return;

                // Sort by LastAccessTime ascending (oldest first)
                Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));

                long targetBytes = (long)(_maxCacheBytes * 0.8); // Trim to 80%
                foreach (var f in files)
                {
                    if (totalBytes <= targetBytes) break;

                    // Do not delete files currently marked dirty or downloading
                    if (f.Extension.StartsWith(".dl_") || f.Extension == ".tmp") continue;

                    long len = f.Length;
                    try
                    {
                        f.Delete();
                        totalBytes -= len;
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static string NormalizeVirtualPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";
            string clean = path.Replace('/', '\\');
            return clean.StartsWith("\\") ? clean : "\\" + clean;
        }
    }
}
