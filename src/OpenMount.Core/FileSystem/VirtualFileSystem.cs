using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Fsp;
using Fsp.Interop;
using OpenMount.Core.Storage;
using FileInfo = Fsp.Interop.FileInfo;

namespace OpenMount.Core.FileSystem
{
    public class VirtualFileSystem : FileSystemBase
    {
        private readonly IStorageProvider _storage;
        private readonly string _stagingDir;
        private static readonly byte[] DefaultSecurityDescriptor;

        private const uint FILE_WRITE_DATA = 0x0002;
        private const uint FILE_APPEND_DATA = 0x0004;

        static VirtualFileSystem()
        {
            var rawSd = new RawSecurityDescriptor("O:BAG:BAD:(A;;FA;;;WD)");
            DefaultSecurityDescriptor = new byte[rawSd.BinaryLength];
            rawSd.GetBinaryForm(DefaultSecurityDescriptor, 0);
        }

        public VirtualFileSystem(IStorageProvider storage, string? stagingDir = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _stagingDir = stagingDir ?? Path.Combine(Path.GetTempPath(), "OpenMount", "staging");
            if (!Directory.Exists(_stagingDir))
            {
                Directory.CreateDirectory(_stagingDir);
            }
        }

        public override int Init(object host)
        {
            if (host is FileSystemHost hostObj)
            {
                hostObj.SectorSize = 4096;
                hostObj.SectorsPerAllocationUnit = 1;
                hostObj.MaxComponentLength = 255;
                hostObj.FileSystemName = "OpenMount";
                hostObj.CaseSensitiveSearch = false;
                hostObj.CasePreservedNames = true;
                hostObj.UnicodeOnDisk = true;
                hostObj.VolumeCreationTime = (ulong)DateTime.UtcNow.ToFileTimeUtc();
                hostObj.VolumeSerialNumber = 0x4F50454E; // "OPEN"
            }
            return STATUS_SUCCESS;
        }

        public override int GetVolumeInfo(out VolumeInfo volumeInfo)
        {
            volumeInfo = default;
            try
            {
                var vol = _storage.GetVolumeInfoAsync().GetAwaiter().GetResult();
                volumeInfo.TotalSize = vol.TotalSize;
                volumeInfo.FreeSize = vol.FreeSize;
                volumeInfo.SetVolumeLabel(vol.VolumeLabel ?? "OpenMount");
                return STATUS_SUCCESS;
            }
            catch
            {
                volumeInfo.TotalSize = 100UL * 1024 * 1024 * 1024;
                volumeInfo.FreeSize = 50UL * 1024 * 1024 * 1024;
                volumeInfo.SetVolumeLabel("OpenMount");
                return STATUS_SUCCESS;
            }
        }

        public override int SetVolumeLabel(string volumeLabel, out VolumeInfo volumeInfo)
        {
            return GetVolumeInfo(out volumeInfo);
        }

        public override int GetSecurityByName(string fileName, out uint fileAttributes, ref byte[] securityDescriptor)
        {
            fileAttributes = 0;
            if (string.IsNullOrEmpty(fileName) || fileName == "\\" || fileName == "/")
            {
                fileAttributes = (uint)FileAttributes.Directory;
                securityDescriptor = DefaultSecurityDescriptor;
                return STATUS_SUCCESS;
            }

            try
            {
                var meta = _storage.GetMetadataAsync(fileName).GetAwaiter().GetResult();
                if (meta == null)
                {
                    return STATUS_OBJECT_NAME_NOT_FOUND;
                }

                fileAttributes = (uint)meta.Attributes;
                if (meta.IsDirectory)
                {
                    fileAttributes |= (uint)FileAttributes.Directory;
                }
                securityDescriptor = DefaultSecurityDescriptor;
                return STATUS_SUCCESS;
            }
            catch
            {
                return STATUS_OBJECT_NAME_NOT_FOUND;
            }
        }

        public override int Create(
            string fileName,
            uint createOptions,
            uint grantedAccess,
            uint fileAttributes,
            byte[] securityDescriptor,
            ulong allocationSize,
            out object fileNode,
            out object fileDesc,
            out FileInfo fileInfo,
            out string normalizedName)
        {
            fileNode = null!;
            fileDesc = null!;
            fileInfo = default;
            normalizedName = null!;

            bool isDir = (createOptions & FILE_DIRECTORY_FILE) != 0;
            if (isDir)
            {
                try
                {
                    _storage.CreateDirectoryAsync(fileName).GetAwaiter().GetResult();
                    var node = new VirtualFileNode(fileName, true, 0, (FileAttributes)fileAttributes);
                    var desc = new VirtualFileDesc(node, grantedAccess);
                    fileNode = node;
                    fileDesc = desc;
                    fileInfo = CreateFileInfo(node);
                    return STATUS_SUCCESS;
                }
                catch
                {
                    return STATUS_ACCESS_DENIED;
                }
            }
            else
            {
                try
                {
                    var node = new VirtualFileNode(fileName, false, 0, (FileAttributes)fileAttributes);
                    var desc = new VirtualFileDesc(node, grantedAccess);

                    string tempPath = Path.Combine(_stagingDir, Guid.NewGuid().ToString("N") + ".tmp");
                    desc.StagingFilePath = tempPath;
                    desc.StagingStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                    desc.IsModified = true;

                    fileNode = node;
                    fileDesc = desc;
                    fileInfo = CreateFileInfo(node);
                    return STATUS_SUCCESS;
                }
                catch
                {
                    return STATUS_ACCESS_DENIED;
                }
            }
        }

        public override int Open(
            string fileName,
            uint createOptions,
            uint grantedAccess,
            out object fileNode,
            out object fileDesc,
            out FileInfo fileInfo,
            out string normalizedName)
        {
            fileNode = null!;
            fileDesc = null!;
            fileInfo = default;
            normalizedName = null!;

            if (string.IsNullOrEmpty(fileName) || fileName == "\\" || fileName == "/")
            {
                var rootNode = new VirtualFileNode("\\", true);
                var rootDesc = new VirtualFileDesc(rootNode, grantedAccess);
                fileNode = rootNode;
                fileDesc = rootDesc;
                fileInfo = CreateFileInfo(rootNode);
                return STATUS_SUCCESS;
            }

            try
            {
                var meta = _storage.GetMetadataAsync(fileName).GetAwaiter().GetResult();
                if (meta == null)
                {
                    return STATUS_OBJECT_NAME_NOT_FOUND;
                }

                if (meta.IsDirectory)
                {
                    var node = new VirtualFileNode(fileName, true, 0, meta.Attributes);
                    node.CreationTime = meta.CreatedTime;
                    node.LastAccessTime = meta.LastAccessTime;
                    node.LastWriteTime = meta.LastModifiedTime;

                    var desc = new VirtualFileDesc(node, grantedAccess);
                    fileNode = node;
                    fileDesc = desc;
                    fileInfo = CreateFileInfo(node);
                    return STATUS_SUCCESS;
                }
                else
                {
                    var node = new VirtualFileNode(fileName, false, meta.Size, meta.Attributes);
                    node.CreationTime = meta.CreatedTime;
                    node.LastAccessTime = meta.LastAccessTime;
                    node.LastWriteTime = meta.LastModifiedTime;

                    var desc = new VirtualFileDesc(node, grantedAccess);

                    bool needWrite = (grantedAccess & (FILE_WRITE_DATA | FILE_APPEND_DATA)) != 0;
                    if (needWrite)
                    {
                        string tempPath = Path.Combine(_stagingDir, Guid.NewGuid().ToString("N") + ".tmp");
                        desc.StagingFilePath = tempPath;
                        var staging = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                        if (meta.Size > 0)
                        {
                            using var src = _storage.OpenReadAsync(fileName, 0, null).GetAwaiter().GetResult();
                            src.CopyTo(staging);
                            staging.Position = 0;
                        }
                        desc.StagingStream = staging;
                    }
                    else
                    {
                        desc.ReadStream = _storage.OpenReadAsync(fileName, 0, null).GetAwaiter().GetResult();
                    }

                    fileNode = node;
                    fileDesc = desc;
                    fileInfo = CreateFileInfo(node);
                    return STATUS_SUCCESS;
                }
            }
            catch
            {
                return STATUS_OBJECT_NAME_NOT_FOUND;
            }
        }

        public override int Overwrite(
            object fileNode,
            object fileDesc,
            uint fileAttributes,
            bool replaceFileAttributes,
            ulong allocationSize,
            out FileInfo fileInfo)
        {
            var node = (VirtualFileNode)fileNode;
            var desc = (VirtualFileDesc)fileDesc;

            lock (node.LockObj)
            {
                if (desc.StagingStream == null)
                {
                    string tempPath = Path.Combine(_stagingDir, Guid.NewGuid().ToString("N") + ".tmp");
                    desc.StagingFilePath = tempPath;
                    desc.StagingStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                }

                desc.StagingStream.SetLength(0);
                node.Size = 0;
                desc.IsModified = true;

                if (replaceFileAttributes)
                    node.Attributes = (FileAttributes)fileAttributes;
                else
                    node.Attributes |= (FileAttributes)fileAttributes;

                node.LastWriteTime = DateTimeOffset.UtcNow;
                fileInfo = CreateFileInfo(node);
                return STATUS_SUCCESS;
            }
        }

        public override int Read(
            object fileNode,
            object fileDesc,
            IntPtr buffer,
            ulong offset,
            uint length,
            out uint bytesTransferred)
        {
            bytesTransferred = 0;
            var node = (VirtualFileNode)fileNode;
            var desc = (VirtualFileDesc)fileDesc;

            if (offset >= (ulong)node.Size)
            {
                return STATUS_END_OF_FILE;
            }

            uint toRead = (uint)Math.Min((ulong)length, (ulong)node.Size - offset);
            byte[] temp = new byte[Math.Min(toRead, 65536)];

            lock (node.LockObj)
            {
                Stream stream = desc.GetActiveStream();
                stream.Seek((long)offset, SeekOrigin.Begin);

                uint totalRead = 0;
                while (totalRead < toRead)
                {
                    int chunk = (int)Math.Min((uint)temp.Length, toRead - totalRead);
                    int r = stream.Read(temp, 0, chunk);
                    if (r <= 0) break;

                    Marshal.Copy(temp, 0, IntPtr.Add(buffer, (int)totalRead), r);
                    totalRead += (uint)r;
                }

                bytesTransferred = totalRead;
                return STATUS_SUCCESS;
            }
        }

        public override int Write(
            object fileNode,
            object fileDesc,
            IntPtr buffer,
            ulong offset,
            uint length,
            bool writeToEndOfFile,
            bool constrainedIo,
            out uint bytesTransferred,
            out FileInfo fileInfo)
        {
            bytesTransferred = 0;
            var node = (VirtualFileNode)fileNode;
            var desc = (VirtualFileDesc)fileDesc;

            lock (node.LockObj)
            {
                if (desc.StagingStream == null)
                {
                    string tempPath = Path.Combine(_stagingDir, Guid.NewGuid().ToString("N") + ".tmp");
                    desc.StagingFilePath = tempPath;
                    var staging = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                    if (node.Size > 0 && desc.ReadStream != null)
                    {
                        desc.ReadStream.Position = 0;
                        desc.ReadStream.CopyTo(staging);
                        desc.ReadStream.Dispose();
                        desc.ReadStream = null;
                    }
                    desc.StagingStream = staging;
                }

                long writePos = writeToEndOfFile ? desc.StagingStream.Length : (long)offset;
                desc.StagingStream.Seek(writePos, SeekOrigin.Begin);

                byte[] temp = new byte[Math.Min(length, 65536)];
                uint written = 0;
                while (written < length)
                {
                    int chunk = (int)Math.Min((uint)temp.Length, length - written);
                    Marshal.Copy(IntPtr.Add(buffer, (int)written), temp, 0, chunk);
                    desc.StagingStream.Write(temp, 0, chunk);
                    written += (uint)chunk;
                }

                desc.IsModified = true;
                if (desc.StagingStream.Length > node.Size)
                {
                    node.Size = desc.StagingStream.Length;
                }
                node.LastWriteTime = DateTimeOffset.UtcNow;

                bytesTransferred = written;
                fileInfo = CreateFileInfo(node);
                return STATUS_SUCCESS;
            }
        }

        public override int SetFileSize(
            object fileNode,
            object fileDesc,
            ulong newSize,
            bool setAllocationSize,
            out FileInfo fileInfo)
        {
            var node = (VirtualFileNode)fileNode;
            var desc = (VirtualFileDesc)fileDesc;

            lock (node.LockObj)
            {
                if (!setAllocationSize)
                {
                    if (desc.StagingStream == null)
                    {
                        string tempPath = Path.Combine(_stagingDir, Guid.NewGuid().ToString("N") + ".tmp");
                        desc.StagingFilePath = tempPath;
                        var staging = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                        if (node.Size > 0 && desc.ReadStream != null)
                        {
                            desc.ReadStream.Position = 0;
                            desc.ReadStream.CopyTo(staging);
                            desc.ReadStream.Dispose();
                            desc.ReadStream = null;
                        }
                        desc.StagingStream = staging;
                    }

                    desc.StagingStream.SetLength((long)newSize);
                    node.Size = (long)newSize;
                    desc.IsModified = true;
                }

                fileInfo = CreateFileInfo(node);
                return STATUS_SUCCESS;
            }
        }

        public override int SetBasicInfo(
            object fileNode,
            object fileDesc,
            uint fileAttributes,
            ulong creationTime,
            ulong lastAccessTime,
            ulong lastWriteTime,
            ulong changeTime,
            out FileInfo fileInfo)
        {
            var node = (VirtualFileNode)fileNode;
            lock (node.LockObj)
            {
                if (fileAttributes != 0 && fileAttributes != uint.MaxValue)
                {
                    node.Attributes = (FileAttributes)fileAttributes;
                }
                if (creationTime != 0)
                {
                    node.CreationTime = DateTimeOffset.FromFileTime((long)creationTime);
                }
                if (lastAccessTime != 0)
                {
                    node.LastAccessTime = DateTimeOffset.FromFileTime((long)lastAccessTime);
                }
                if (lastWriteTime != 0)
                {
                    node.LastWriteTime = DateTimeOffset.FromFileTime((long)lastWriteTime);
                }

                fileInfo = CreateFileInfo(node);
                return STATUS_SUCCESS;
            }
        }

        public override int GetFileInfo(object fileNode, object fileDesc, out FileInfo fileInfo)
        {
            var node = (VirtualFileNode)fileNode;
            fileInfo = CreateFileInfo(node);
            return STATUS_SUCCESS;
        }

        public override int CanDelete(object fileNode, object fileDesc, string fileName)
        {
            var node = (VirtualFileNode)fileNode;
            if (node.IsDirectory)
            {
                try
                {
                    var items = _storage.ListDirectoryAsync(node.Path).GetAwaiter().GetResult();
                    if (items != null && items.Count > 0)
                    {
                        return STATUS_DIRECTORY_NOT_EMPTY;
                    }
                }
                catch { }
            }
            return STATUS_SUCCESS;
        }

        public override int SetDelete(object fileNode, object fileDesc, string fileName, bool deleteFile)
        {
            var node = (VirtualFileNode)fileNode;
            node.DeleteOnClose = deleteFile;
            return STATUS_SUCCESS;
        }

        public override int Rename(object fileNode, object fileDesc, string fileName, string newFileName, bool replaceIfExists)
        {
            var node = (VirtualFileNode)fileNode;
            try
            {
                _storage.MoveAsync(fileName, newFileName).GetAwaiter().GetResult();
                node.Path = newFileName;
                return STATUS_SUCCESS;
            }
            catch
            {
                return STATUS_ACCESS_DENIED;
            }
        }

        public override void Cleanup(object fileNode, object fileDesc, string fileName, uint flags)
        {
            var node = (VirtualFileNode)fileNode;
            var desc = (VirtualFileDesc)fileDesc;

            if ((flags & 0x01) != 0 || node.DeleteOnClose)
            {
                try
                {
                    if (node.IsDirectory)
                    {
                        _storage.DeleteDirectoryAsync(node.Path, false).GetAwaiter().GetResult();
                    }
                    else
                    {
                        _storage.DeleteFileAsync(node.Path).GetAwaiter().GetResult();
                    }
                }
                catch { }

                desc.Dispose();
                return;
            }

            if (desc.IsModified && desc.StagingStream != null)
            {
                try
                {
                    desc.StagingStream.Flush();
                    desc.StagingStream.Position = 0;
                    _storage.CreateFileAsync(node.Path, desc.StagingStream).GetAwaiter().GetResult();
                    desc.IsModified = false;
                }
                catch { }
            }
        }

        public override void Close(object fileNode, object fileDesc)
        {
            var desc = (VirtualFileDesc)fileDesc;
            desc.Dispose();
        }

        public override int Flush(object fileNode, object fileDesc, out FileInfo fileInfo)
        {
            var node = (VirtualFileNode)fileNode;
            var desc = (VirtualFileDesc)fileDesc;

            lock (node.LockObj)
            {
                if (desc.IsModified && desc.StagingStream != null)
                {
                    desc.StagingStream.Flush();
                }
                fileInfo = CreateFileInfo(node);
                return STATUS_SUCCESS;
            }
        }

        public override int GetSecurity(object fileNode, object fileDesc, ref byte[] securityDescriptor)
        {
            securityDescriptor = DefaultSecurityDescriptor;
            return STATUS_SUCCESS;
        }

        public override int SetSecurity(object fileNode, object fileDesc, AccessControlSections sections, byte[] securityDescriptor)
        {
            return STATUS_SUCCESS;
        }

        public override bool ReadDirectoryEntry(
            object fileNode,
            object fileDesc,
            string pattern,
            string marker,
            ref object context,
            out string fileName,
            out FileInfo fileInfo)
        {
            var node = (VirtualFileNode)fileNode;

            if (context == null)
            {
                var list = new List<DirEntryItem>();
                list.Add(new DirEntryItem(".", true, 0, node.CreationTime, node.LastAccessTime, node.LastWriteTime, FileAttributes.Directory));
                list.Add(new DirEntryItem("..", true, 0, node.CreationTime, node.LastAccessTime, node.LastWriteTime, FileAttributes.Directory));

                try
                {
                    var items = _storage.ListDirectoryAsync(node.Path).GetAwaiter().GetResult();
                    if (items != null)
                    {
                        foreach (var it in items)
                        {
                            list.Add(new DirEntryItem(
                                it.Name,
                                it.IsDirectory,
                                it.Size,
                                it.CreatedTime,
                                it.LastAccessTime,
                                it.LastModifiedTime,
                                it.Attributes));
                        }
                    }
                }
                catch { }

                int startIndex = 0;
                if (!string.IsNullOrEmpty(marker))
                {
                    int idx = list.FindIndex(x => string.Equals(x.Name, marker, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) startIndex = idx + 1;
                }

                context = new DirEnumContext(list, startIndex);
            }

            var ctx = (DirEnumContext)context;
            if (ctx.CurrentIndex < ctx.Items.Count)
            {
                var cur = ctx.Items[ctx.CurrentIndex++];
                fileName = cur.Name;
                fileInfo = cur.ToFileInfo();
                return true;
            }

            fileName = null!;
            fileInfo = default;
            return false;
        }

        private static FileInfo CreateFileInfo(VirtualFileNode node)
        {
            return new FileInfo
            {
                FileAttributes = (uint)node.Attributes,
                ReparseTag = 0,
                AllocationSize = node.AllocationSize,
                FileSize = (ulong)Math.Max(0, node.Size),
                CreationTime = (ulong)node.CreationTime.ToFileTime(),
                LastAccessTime = (ulong)node.LastAccessTime.ToFileTime(),
                LastWriteTime = (ulong)node.LastWriteTime.ToFileTime(),
                ChangeTime = (ulong)node.LastWriteTime.ToFileTime(),
                IndexNumber = 0,
                HardLinks = 1
            };
        }

        private class DirEnumContext
        {
            public List<DirEntryItem> Items { get; }
            public int CurrentIndex { get; set; }

            public DirEnumContext(List<DirEntryItem> items, int startIndex)
            {
                Items = items;
                CurrentIndex = startIndex;
            }
        }

        private class DirEntryItem
        {
            public string Name { get; }
            public bool IsDirectory { get; }
            public long Size { get; }
            public DateTimeOffset CreationTime { get; }
            public DateTimeOffset LastAccessTime { get; }
            public DateTimeOffset LastWriteTime { get; }
            public FileAttributes Attributes { get; }

            public DirEntryItem(
                string name,
                bool isDirectory,
                long size,
                DateTimeOffset creationTime,
                DateTimeOffset lastAccessTime,
                DateTimeOffset lastWriteTime,
                FileAttributes attributes = FileAttributes.Normal)
            {
                Name = name;
                IsDirectory = isDirectory;
                Size = size;
                CreationTime = creationTime;
                LastAccessTime = lastAccessTime;
                LastWriteTime = lastWriteTime;
                Attributes = isDirectory ? (attributes | FileAttributes.Directory) : attributes;
            }

            public FileInfo ToFileInfo()
            {
                return new FileInfo
                {
                    FileAttributes = (uint)Attributes,
                    ReparseTag = 0,
                    AllocationSize = IsDirectory ? 0 : (ulong)((Size + 4095) / 4096 * 4096),
                    FileSize = (ulong)Math.Max(0, Size),
                    CreationTime = (ulong)CreationTime.ToFileTime(),
                    LastAccessTime = (ulong)LastAccessTime.ToFileTime(),
                    LastWriteTime = (ulong)LastWriteTime.ToFileTime(),
                    ChangeTime = (ulong)LastWriteTime.ToFileTime(),
                    IndexNumber = 0,
                    HardLinks = 1
                };
            }
        }
    }
}
