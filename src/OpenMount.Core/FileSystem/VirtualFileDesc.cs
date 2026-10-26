using System;
using System.IO;

namespace OpenMount.Core.FileSystem
{
    public class VirtualFileDesc : IDisposable
    {
        public VirtualFileNode Node { get; }
        public FileStream? LocalFileStream { get; set; }
        public string? LocalFilePath { get; set; }
        public string? StagingFilePath
        {
            get => LocalFilePath;
            set => LocalFilePath = value;
        }
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
            if (LocalFileStream != null) return LocalFileStream;
            throw new InvalidOperationException("No active stream on file descriptor.");
        }

        public void PreserveUnsavedChanges()
        {
            if (!IsModified || string.IsNullOrEmpty(LocalFilePath) || !File.Exists(LocalFilePath))
                return;

            try
            {
                if (!Directory.Exists(UnsavedRecoveryDir))
                {
                    Directory.CreateDirectory(UnsavedRecoveryDir);
                }
                string safeName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(LocalFilePath)}_{Path.GetFileName(Node.Path)}";
                string recoveryPath = Path.Combine(UnsavedRecoveryDir, safeName);
                File.Copy(LocalFilePath, recoveryPath, overwrite: true);
                LastPreservedRecoveryPath = recoveryPath;
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                LocalFileStream?.Dispose();
                LocalFileStream = null;
            }
            catch { }

            if (IsModified && !string.IsNullOrEmpty(LocalFilePath) && File.Exists(LocalFilePath))
            {
                PreserveUnsavedChanges();
                try
                {
                    File.Delete(LocalFilePath);
                }
                catch { }
            }
        }
    }
}
