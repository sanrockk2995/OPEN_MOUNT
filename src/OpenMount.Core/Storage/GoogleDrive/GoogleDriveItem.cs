using System;
using System.IO;

namespace OpenMount.Core.Storage.GoogleDrive
{
    public class GoogleDriveItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public string VirtualPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public bool IsWorkspaceDoc { get; set; }
        public string? WorkspaceExportMime { get; set; }
        public long Size { get; set; }
        public DateTimeOffset CreatedTime { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ModifiedTime { get; set; } = DateTimeOffset.UtcNow;
        public string MimeType { get; set; } = "application/octet-stream";
        public string? ETag { get; set; }
    }
}
