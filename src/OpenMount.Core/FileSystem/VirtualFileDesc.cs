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

        public static string UnsavedRecoveryDir { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenMount", "unsaved");

        public string? LastPreservedRecoveryPath { get; private set; }

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
                if (Node.OpenCount <= 1)
                {
                    if (IsModified)
                    {
                        // Preservation: if upload failed or was aborted before successful commit,
                        // quarantine the modified file in the unsaved recovery directory.
                        try
                        {
                            if (!Directory.Exists(UnsavedRecoveryDir))
                            {
                                Directory.CreateDirectory(UnsavedRecoveryDir);
                            }
                            string safeName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(StagingFilePath)}_{Path.GetFileName(Node.Path)}";
                            string recoveryPath = Path.Combine(UnsavedRecoveryDir, safeName);
                            File.Copy(StagingFilePath, recoveryPath, overwrite: true);
                            LastPreservedRecoveryPath = recoveryPath;
                        }
                        catch { }
                    }

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
}
