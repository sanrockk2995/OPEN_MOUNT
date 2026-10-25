using System;
using System.IO;

namespace OpenMount.Core.FileSystem
{
    public class VirtualFileNode
    {
        public string Path { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public FileAttributes Attributes { get; set; }
        public DateTimeOffset CreationTime { get; set; }
        public DateTimeOffset LastAccessTime { get; set; }
        public DateTimeOffset LastWriteTime { get; set; }
        public bool DeleteOnClose { get; set; }
        public int OpenCount;
        public readonly object LockObj = new object();

        public VirtualFileNode(string path, bool isDirectory, long size = 0, FileAttributes attributes = FileAttributes.Normal)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            IsDirectory = isDirectory;
            Size = size;
            Attributes = isDirectory ? (attributes | FileAttributes.Directory) : attributes;
            CreationTime = DateTimeOffset.UtcNow;
            LastAccessTime = DateTimeOffset.UtcNow;
            LastWriteTime = DateTimeOffset.UtcNow;
        }

        public ulong AllocationSize => IsDirectory ? 0 : (ulong)((Size + 4095) / 4096 * 4096);
    }
}
