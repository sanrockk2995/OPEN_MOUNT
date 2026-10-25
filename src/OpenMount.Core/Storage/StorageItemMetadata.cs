using System;
using System.IO;

namespace OpenMount.Core.Storage
{
    public class StorageVolumeInfo
    {
        public ulong TotalSize { get; set; }
        public ulong FreeSize { get; set; }
        public string VolumeLabel { get; set; } = "OpenMount";
        public string FileSystemName { get; set; } = "NTFS";
    }

    public class StorageItemMetadata
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
        public DateTimeOffset CreatedTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastModifiedTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastAccessTime { get; set; } = DateTimeOffset.UtcNow;
        public FileAttributes Attributes { get; set; } = FileAttributes.Normal;
        public string? ETag { get; set; }
    }
}
