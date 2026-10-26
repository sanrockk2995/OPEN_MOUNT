using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenMount.Core.Manager;

namespace OpenMount.Core.Storage
{
    public interface IStorageProvider : IAsyncDisposable
    {
        string ProviderId { get; }
        string DisplayName { get; }

        Task InitializeAsync(CancellationToken ct = default);
        Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default);
        Task<StorageItemMetadata?> GetMetadataAsync(string path, CancellationToken ct = default);
        Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct = default);
        
        Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct = default);
        Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct = default);
        Task CreateDirectoryAsync(string path, CancellationToken ct = default);
        Task DeleteFileAsync(string path, CancellationToken ct = default);
        Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default);
        Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default);
        Task CopyAsync(string sourcePath, string destinationPath, ConflictResolutionMode conflictResolution, CancellationToken ct = default);
        Task SetLastModifiedTimeAsync(string path, DateTimeOffset lastModifiedTime, CancellationToken ct = default);
    }
}
