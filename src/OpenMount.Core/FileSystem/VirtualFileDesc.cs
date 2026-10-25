using System;
using System.IO;

namespace OpenMount.Core.FileSystem
{
    public class VirtualFileDesc : IDisposable
    {
        public VirtualFileNode Node { get; }
        public Stream? ReadStream { get; set; }
        public FileStream? StagingStream { get; set; }
        public string? StagingFilePath { get; set; }
        public bool IsModified { get; set; }
        public uint GrantedAccess { get; }

        public VirtualFileDesc(VirtualFileNode node, uint grantedAccess = 0)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            GrantedAccess = grantedAccess;
        }

        public Stream GetActiveStream()
        {
            if (StagingStream != null) return StagingStream;
            if (ReadStream != null) return ReadStream;
            throw new InvalidOperationException("No active stream on file descriptor.");
        }

        public void Dispose()
        {
            try
            {
                StagingStream?.Dispose();
                StagingStream = null;
            }
            catch { }

            try
            {
                ReadStream?.Dispose();
                ReadStream = null;
            }
            catch { }

            if (!string.IsNullOrEmpty(StagingFilePath) && File.Exists(StagingFilePath))
            {
                try
                {
                    File.Delete(StagingFilePath);
                }
                catch { }
                StagingFilePath = null;
            }
        }
    }
}
