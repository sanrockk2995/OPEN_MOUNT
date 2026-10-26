using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace OpenMount.Core.Cache
{
    public class FolderProperties
    {
        public string Path { get; set; } = string.Empty;
        public long TotalSizeBytes { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsExpired(TimeSpan ttl) => (DateTime.UtcNow - CachedAtUtc) > ttl;
    }

    public class FolderPropertiesCache
    {
        private readonly ConcurrentDictionary<string, FolderProperties> _cache =
            new ConcurrentDictionary<string, FolderProperties>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        public void UpdateFolder(string folderPath, long totalBytes, int fileCount, int dirCount)
        {
            string clean = NormalizePath(folderPath);
            _cache[clean] = new FolderProperties
            {
                Path = clean,
                TotalSizeBytes = totalBytes,
                FileCount = fileCount,
                DirectoryCount = dirCount,
                CachedAtUtc = DateTime.UtcNow
            };
        }

        public void Update(string folderPath, IEnumerable<OpenMount.Core.Storage.StorageItemMetadata> items)
        {
            long bytes = 0;
            int files = 0;
            int dirs = 0;
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item.IsDirectory)
                    {
                        dirs++;
                    }
                    else
                    {
                        files++;
                        bytes += item.Size;
                    }
                }
            }
            UpdateFolder(folderPath, bytes, files, dirs);
        }

        public bool TryGetProperties(string folderPath, out FolderProperties? properties)
        {
            string clean = NormalizePath(folderPath);
            if (_cache.TryGetValue(clean, out var p) && !p.IsExpired(DefaultTtl))
            {
                properties = p;
                return true;
            }
            properties = null;
            return false;
        }

        public void Invalidate(string path)
        {
            string clean = NormalizePath(path);
            _cache.TryRemove(clean, out _);

            // Invalidate parent folders recursively
            string parent = GetParentPath(clean);
            while (!string.IsNullOrEmpty(parent) && parent != "\\")
            {
                _cache.TryRemove(parent, out _);
                parent = GetParentPath(parent);
            }
        }

        public void Clear()
        {
            _cache.Clear();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";
            string clean = path.Replace('/', '\\');
            return clean.StartsWith("\\") ? clean : "\\" + clean;
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "\\") return string.Empty;
            int idx = path.LastIndexOf('\\');
            if (idx <= 0) return "\\";
            return path.Substring(0, idx);
        }
    }
}
