using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using File = Google.Apis.Drive.v3.Data.File;

namespace OpenMount.Core.Storage.GoogleDrive
{
    public class GoogleDriveStorageProvider : IStorageProvider
    {
        private readonly GoogleDriveOAuthConfig _config;
        private readonly DriveService _driveService;
        private readonly bool _disposeService;

        // Path (normalized e.g. "\folder\file.txt") -> GoogleDriveItem
        private readonly ConcurrentDictionary<string, GoogleDriveItem> _pathCache =
            new ConcurrentDictionary<string, GoogleDriveItem>(StringComparer.OrdinalIgnoreCase);

        // Parent Folder ID -> List of Children
        private readonly ConcurrentDictionary<string, (DateTime CachedAtUtc, List<GoogleDriveItem> Items)> _dirCache =
            new ConcurrentDictionary<string, (DateTime, List<GoogleDriveItem>)>(StringComparer.OrdinalIgnoreCase);

        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);

        public string ProviderId => "gdrive";
        public string DisplayName { get; }

        public const string MimeTypeFolder = "application/vnd.google-apps.folder";
        public const string MimeTypeDoc = "application/vnd.google-apps.document";
        public const string MimeTypeSheet = "application/vnd.google-apps.spreadsheet";
        public const string MimeTypeSlides = "application/vnd.google-apps.presentation";
        public const string MimeTypeShortcut = "application/vnd.google-apps.shortcut";

        public const string ExportMimeDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        public const string ExportMimeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public const string ExportMimePptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

        public GoogleDriveStorageProvider(
            GoogleDriveOAuthConfig config,
            DriveService driveService,
            bool disposeService = false,
            string displayName = "Google Drive")
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _driveService = driveService ?? throw new ArgumentNullException(nameof(driveService));
            _disposeService = disposeService;
            DisplayName = displayName;

            // Seed root item
            _pathCache["\\"] = new GoogleDriveItem
            {
                Id = _config.RootFolderId,
                Name = "\\",
                OriginalName = "root",
                ParentId = string.Empty,
                VirtualPath = "\\",
                IsDirectory = true,
                Size = 0,
                MimeType = MimeTypeFolder
            };
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            var aboutReq = _driveService.About.Get();
            aboutReq.Fields = "user, storageQuota";
            await aboutReq.ExecuteAsync(ct).ConfigureAwait(false);
        }

        public async Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default)
        {
            try
            {
                var aboutReq = _driveService.About.Get();
                aboutReq.Fields = "storageQuota, user";
                var about = await aboutReq.ExecuteAsync(ct).ConfigureAwait(false);

                ulong limit = (ulong)(about.StorageQuota?.Limit ?? (100L * 1024 * 1024 * 1024));
                ulong usage = (ulong)(about.StorageQuota?.Usage ?? 0);
                ulong free = limit > usage ? (limit - usage) : 0;

                string userDisplay = about.User?.DisplayName ?? "User";

                return new StorageVolumeInfo
                {
                    TotalSize = limit,
                    FreeSize = free,
                    VolumeLabel = $"{DisplayName} ({userDisplay})",
                    FileSystemName = "OpenMountGDrive"
                };
            }
            catch
            {
                return new StorageVolumeInfo
                {
                    TotalSize = 100UL * 1024 * 1024 * 1024,
                    FreeSize = 50UL * 1024 * 1024 * 1024,
                    VolumeLabel = DisplayName,
                    FileSystemName = "OpenMountGDrive"
                };
            }
        }

        public async Task<StorageItemMetadata?> GetMetadataAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            if (clean == "\\")
            {
                return new StorageItemMetadata
                {
                    Path = "\\",
                    Name = "\\",
                    IsDirectory = true,
                    Size = 0,
                    CreatedTime = DateTimeOffset.UtcNow,
                    LastAccessTime = DateTimeOffset.UtcNow,
                    LastModifiedTime = DateTimeOffset.UtcNow,
                    Attributes = FileAttributes.Directory
                };
            }

            var item = await ResolveItemByPathAsync(clean, ct).ConfigureAwait(false);
            if (item == null) return null;

            return new StorageItemMetadata
            {
                Path = item.VirtualPath,
                Name = item.Name,
                Size = item.Size,
                IsDirectory = item.IsDirectory,
                CreatedTime = item.CreatedTime,
                LastAccessTime = item.ModifiedTime,
                LastModifiedTime = item.ModifiedTime,
                Attributes = item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                ETag = item.ETag
            };
        }

        public async Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            var parentItem = await ResolveItemByPathAsync(clean, ct).ConfigureAwait(false);
            if (parentItem == null || !parentItem.IsDirectory)
            {
                return Array.Empty<StorageItemMetadata>();
            }

            var items = await GetFolderChildrenAsync(parentItem.Id, clean, ct).ConfigureAwait(false);

            return items.Select(it => new StorageItemMetadata
            {
                Path = it.VirtualPath,
                Name = it.Name,
                Size = it.Size,
                IsDirectory = it.IsDirectory,
                CreatedTime = it.CreatedTime,
                LastAccessTime = it.ModifiedTime,
                LastModifiedTime = it.ModifiedTime,
                Attributes = it.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                ETag = it.ETag
            }).ToList();
        }

        private async Task<List<GoogleDriveItem>> GetFolderChildrenAsync(string parentFolderId, string parentVirtualPath, CancellationToken ct)
        {
            if (_dirCache.TryGetValue(parentFolderId, out var cached) &&
                (DateTime.UtcNow - cached.CachedAtUtc) < _cacheTtl)
            {
                return cached.Items;
            }

            var driveFiles = new List<File>();
            string? pageToken = null;

            do
            {
                var listReq = _driveService.Files.List();
                listReq.Q = $"'{parentFolderId}' in parents and trashed = false";
                listReq.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime, createdTime, capabilities, shortcutDetails)";
                listReq.SupportsAllDrives = true;
                listReq.IncludeItemsFromAllDrives = true;
                listReq.PageSize = 1000;
                listReq.PageToken = pageToken;

                var res = await listReq.ExecuteAsync(ct).ConfigureAwait(false);
                if (res.Files != null)
                {
                    driveFiles.AddRange(res.Files);
                }
                pageToken = res.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            // Disambiguate duplicate file names and map Workspace docs
            var result = DisambiguateAndMapItems(driveFiles, parentFolderId, parentVirtualPath);

            _dirCache[parentFolderId] = (DateTime.UtcNow, result);
            foreach (var item in result)
            {
                _pathCache[item.VirtualPath] = item;
            }

            return result;
        }

        private List<GoogleDriveItem> DisambiguateAndMapItems(List<File> files, string parentFolderId, string parentVirtualPath)
        {
            var result = new List<GoogleDriveItem>();
            var nameUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                string targetId = file.Id;
                string mime = file.MimeType;

                // Resolve shortcut target if applicable
                if (mime == MimeTypeShortcut && file.ShortcutDetails?.TargetId != null)
                {
                    targetId = file.ShortcutDetails.TargetId;
                    mime = file.ShortcutDetails.TargetMimeType ?? mime;
                }

                bool isFolder = mime == MimeTypeFolder;
                bool isWorkspace = false;
                string? exportMime = null;
                string baseName = file.Name ?? "untitled";
                long size = file.Size ?? 0;

                // Handle Google Workspace documents export policy
                if (_config.ExportWorkspaceDocuments && !isFolder)
                {
                    if (mime == MimeTypeDoc)
                    {
                        isWorkspace = true;
                        exportMime = ExportMimeDocx;
                        if (!baseName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) baseName += ".docx";
                        if (size == 0) size = 64 * 1024; // Virtual default size for Explorer
                    }
                    else if (mime == MimeTypeSheet)
                    {
                        isWorkspace = true;
                        exportMime = ExportMimeXlsx;
                        if (!baseName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) baseName += ".xlsx";
                        if (size == 0) size = 64 * 1024;
                    }
                    else if (mime == MimeTypeSlides)
                    {
                        isWorkspace = true;
                        exportMime = ExportMimePptx;
                        if (!baseName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)) baseName += ".pptx";
                        if (size == 0) size = 128 * 1024;
                    }
                }

                // Deterministic duplicate name disambiguation for Windows Explorer
                string finalName = baseName;
                if (nameUsage.TryGetValue(baseName, out int count))
                {
                    count++;
                    nameUsage[baseName] = count;
                    string ext = Path.GetExtension(baseName);
                    string nameNoExt = Path.GetFileNameWithoutExtension(baseName);
                    finalName = $"{nameNoExt} ({count}){ext}";
                }
                else
                {
                    nameUsage[baseName] = 1;
                }

                string virtualPath = parentVirtualPath == "\\"
                    ? "\\" + finalName
                    : parentVirtualPath + "\\" + finalName;

                DateTimeOffset created = file.CreatedTimeDateTimeOffset ?? DateTimeOffset.UtcNow;
                DateTimeOffset modified = file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow;

                result.Add(new GoogleDriveItem
                {
                    Id = targetId,
                    Name = finalName,
                    OriginalName = file.Name ?? "untitled",
                    ParentId = parentFolderId,
                    VirtualPath = virtualPath,
                    IsDirectory = isFolder,
                    IsWorkspaceDoc = isWorkspace,
                    WorkspaceExportMime = exportMime,
                    Size = size,
                    CreatedTime = created,
                    ModifiedTime = modified,
                    MimeType = mime
                });
            }

            return result;
        }

        private async Task<GoogleDriveItem?> ResolveItemByPathAsync(string virtualPath, CancellationToken ct)
        {
            string clean = NormalizePath(virtualPath);
            if (clean == "\\") return _pathCache["\\"];

            if (_pathCache.TryGetValue(clean, out var cached))
            {
                return cached;
            }

            // Split into path segments
            string[] parts = clean.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string currentParentId = _config.RootFolderId;
            string currentVirtualPath = "";

            GoogleDriveItem? currentItem = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string segment = parts[i];
                currentVirtualPath = currentVirtualPath + "\\" + segment;

                if (_pathCache.TryGetValue(currentVirtualPath, out var segmentItem))
                {
                    currentItem = segmentItem;
                    currentParentId = segmentItem.Id;
                    continue;
                }

                var children = await GetFolderChildrenAsync(currentParentId, currentVirtualPath.Substring(0, currentVirtualPath.LastIndexOf('\\') == 0 ? 1 : currentVirtualPath.LastIndexOf('\\')), ct).ConfigureAwait(false);
                var match = children.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    return null;
                }

                currentItem = match;
                currentParentId = match.Id;
            }

            return currentItem;
        }

        public async Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            var item = await ResolveItemByPathAsync(clean, ct).ConfigureAwait(false);
            if (item == null || item.IsDirectory)
            {
                throw new FileNotFoundException("Google Drive file not found", path);
            }

            // If Google Workspace document (Doc, Sheet, Slide): Export to DOCX/XLSX/PPTX
            if (item.IsWorkspaceDoc && !string.IsNullOrEmpty(item.WorkspaceExportMime))
            {
                var exportReq = _driveService.Files.Export(item.Id, item.WorkspaceExportMime);
                var ms = new MemoryStream();
                await exportReq.DownloadAsync(ms, ct).ConfigureAwait(false);
                ms.Position = 0;

                if (offset > 0 || length.HasValue)
                {
                    return new SlicedStream(ms, offset, length);
                }
                return ms;
            }

            // Binary files: Stream via HTTP Range request without loading entire file
            string uri = $"https://www.googleapis.com/drive/v3/files/{item.Id}?alt=media&supportsAllDrives=true";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (offset > 0 || length.HasValue)
            {
                long start = offset;
                long? end = length.HasValue ? (offset + length.Value - 1) : null;
                request.Headers.Range = new RangeHeaderValue(start, end);
            }

            var response = await _driveService.HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }

        public async Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            string parentPath = GetParentPath(clean);
            string fileName = Path.GetFileName(clean);

            var parent = await ResolveItemByPathAsync(parentPath, ct).ConfigureAwait(false);
            if (parent == null || !parent.IsDirectory)
            {
                throw new DirectoryNotFoundException($"Parent directory not found for {path}");
            }

            // Check if file already exists in parent
            var existing = await ResolveItemByPathAsync(clean, ct).ConfigureAwait(false);

            if (existing != null && !existing.IsDirectory)
            {
                // Update existing file content via Resumable Upload
                var updateReq = _driveService.Files.Update(
                    new File { Name = fileName },
                    existing.Id,
                    contentStream,
                    "application/octet-stream");
                updateReq.SupportsAllDrives = true;

                await ExecuteResumableUploadAsync(updateReq, clean, ct).ConfigureAwait(false);
            }
            else
            {
                // Create new file via Resumable Upload
                var fileMetadata = new File
                {
                    Name = fileName,
                    Parents = new List<string> { parent.Id }
                };

                var createReq = _driveService.Files.Create(
                    fileMetadata,
                    contentStream,
                    "application/octet-stream");
                createReq.SupportsAllDrives = true;

                await ExecuteResumableUploadAsync(createReq, clean, ct).ConfigureAwait(false);
            }

            InvalidateCache(parent.Id, clean);
        }

        private async Task ExecuteResumableUploadAsync<TRequest>(
            Google.Apis.Upload.ResumableUpload<TRequest, File> uploader,
            string virtualPath,
            CancellationToken ct)
        {
            uploader.ChunkSize = Google.Apis.Upload.ResumableUpload.MinimumChunkSize * 4; // 1 MB chunking

            int maxRetries = 3;
            int retryCount = 0;

            var progress = await uploader.UploadAsync(ct).ConfigureAwait(false);

            while (progress.Status == Google.Apis.Upload.UploadStatus.Failed && retryCount < maxRetries && !ct.IsCancellationRequested)
            {
                retryCount++;
                if (IsTransientUploadError(progress.Exception))
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), ct).ConfigureAwait(false);
                    progress = await uploader.ResumeAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    break;
                }
            }

            if (progress.Status == Google.Apis.Upload.UploadStatus.Failed)
            {
                throw new IOException(
                    $"Google Drive resumable upload failed for '{virtualPath}' after {retryCount} retries: {progress.Exception?.Message}",
                    progress.Exception);
            }
        }

        private static bool IsTransientUploadError(Exception? ex)
        {
            if (ex == null) return false;
            if (ex is HttpRequestException || ex is IOException || ex is TimeoutException) return true;
            if (ex is Google.GoogleApiException gex)
            {
                int code = (int)gex.HttpStatusCode;
                if (code >= 500 || code == 429) return true;
            }
            return false;
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            string parentPath = GetParentPath(clean);
            string dirName = Path.GetFileName(clean);

            var parent = await ResolveItemByPathAsync(parentPath, ct).ConfigureAwait(false);
            if (parent == null || !parent.IsDirectory)
            {
                throw new DirectoryNotFoundException($"Parent directory not found for {path}");
            }

            var folderMeta = new File
            {
                Name = dirName,
                MimeType = MimeTypeFolder,
                Parents = new List<string> { parent.Id }
            };

            var req = _driveService.Files.Create(folderMeta);
            req.SupportsAllDrives = true;
            await req.ExecuteAsync(ct).ConfigureAwait(false);

            InvalidateCache(parent.Id, clean);
        }

        public async Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            await DeleteInternalAsync(path, ct).ConfigureAwait(false);
        }

        public async Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            await DeleteInternalAsync(path, ct).ConfigureAwait(false);
        }

        private async Task DeleteInternalAsync(string path, CancellationToken ct)
        {
            string clean = NormalizePath(path);
            var item = await ResolveItemByPathAsync(clean, ct).ConfigureAwait(false);
            if (item == null) return;

            // Move to Trash (safest policy for virtual drives)
            var trashUpdate = new File { Trashed = true };
            var updateReq = _driveService.Files.Update(trashUpdate, item.Id);
            updateReq.SupportsAllDrives = true;
            await updateReq.ExecuteAsync(ct).ConfigureAwait(false);

            InvalidateCache(item.ParentId, clean);
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            string cleanSrc = NormalizePath(sourcePath);
            string cleanDst = NormalizePath(destinationPath);

            var item = await ResolveItemByPathAsync(cleanSrc, ct).ConfigureAwait(false);
            if (item == null)
            {
                throw new FileNotFoundException("Source file not found", sourcePath);
            }

            string oldParentId = item.ParentId;
            string newParentPath = GetParentPath(cleanDst);
            string newName = Path.GetFileName(cleanDst);

            var newParent = await ResolveItemByPathAsync(newParentPath, ct).ConfigureAwait(false);
            if (newParent == null || !newParent.IsDirectory)
            {
                throw new DirectoryNotFoundException($"Destination parent folder not found: {newParentPath}");
            }

            var updateMeta = new File { Name = newName };
            var updateReq = _driveService.Files.Update(updateMeta, item.Id);
            updateReq.SupportsAllDrives = true;

            if (oldParentId != newParent.Id)
            {
                updateReq.AddParents = newParent.Id;
                if (!string.IsNullOrEmpty(oldParentId))
                {
                    updateReq.RemoveParents = oldParentId;
                }
            }

            await updateReq.ExecuteAsync(ct).ConfigureAwait(false);

            InvalidateCache(oldParentId, cleanSrc);
            InvalidateCache(newParent.Id, cleanDst);
        }

        private void InvalidateCache(string parentFolderId, string virtualPath)
        {
            _dirCache.TryRemove(parentFolderId, out _);
            _pathCache.TryRemove(virtualPath, out _);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";
            string clean = path.Replace('/', '\\');
            return clean.StartsWith("\\") ? clean : "\\" + clean;
        }

        private static string GetParentPath(string path)
        {
            string clean = NormalizePath(path).TrimEnd('\\');
            int idx = clean.LastIndexOf('\\');
            if (idx <= 0) return "\\";
            return clean.Substring(0, idx);
        }

        public ValueTask DisposeAsync()
        {
            if (_disposeService)
            {
                _driveService.Dispose();
            }
            return ValueTask.CompletedTask;
        }

        private class SlicedStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _start;
            private readonly long _length;
            private long _position;

            public SlicedStream(Stream inner, long start, long? length)
            {
                _inner = inner;
                _start = start;
                _length = length ?? (inner.Length - start);
                _inner.Seek(start, SeekOrigin.Begin);
                _position = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _length) return 0;
                int toRead = (int)Math.Min((long)count, _length - _position);
                int r = _inner.Read(buffer, offset, toRead);
                _position += r;
                return r;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
