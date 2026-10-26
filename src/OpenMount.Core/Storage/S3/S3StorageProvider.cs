using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using OpenMount.Core.Manager;

namespace OpenMount.Core.Storage.S3
{
    public class S3StorageConfig
    {
        public string ServiceUrl { get; set; } = "https://s3.amazonaws.com";
        public string Region { get; set; } = "us-east-1";
        public string BucketName { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string RootPrefix { get; set; } = string.Empty;
        public bool ForcePathStyle { get; set; } = true;
    }

    public class S3StorageProvider : IStorageProvider
    {
        private readonly S3StorageConfig _config;
        private readonly IAmazonS3 _s3Client;
        private readonly bool _disposeClient;

        public string ProviderId => "s3";
        public string DisplayName { get; }

        public S3StorageProvider(S3StorageConfig config, IAmazonS3? s3Client = null, string displayName = "Amazon S3")
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            DisplayName = displayName;

            if (s3Client != null)
            {
                _s3Client = s3Client;
                _disposeClient = false;
            }
            else
            {
                var s3Config = new AmazonS3Config
                {
                    ForcePathStyle = _config.ForcePathStyle
                };

                if (!string.IsNullOrEmpty(_config.ServiceUrl) &&
                    !_config.ServiceUrl.Contains("s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
                {
                    s3Config.ServiceURL = _config.ServiceUrl;
                }
                else
                {
                    s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(_config.Region);
                }

                var credentials = new BasicAWSCredentials(_config.AccessKey, _config.SecretKey);
                _s3Client = new AmazonS3Client(credentials, s3Config);
                _disposeClient = true;
            }
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            var req = new ListObjectsV2Request
            {
                BucketName = _config.BucketName,
                MaxKeys = 1
            };
            await _s3Client.ListObjectsV2Async(req, ct).ConfigureAwait(false);
        }

        private string ToS3Key(string virtualPath)
        {
            string clean = virtualPath.TrimStart('\\', '/').Replace('\\', '/');
            if (string.IsNullOrEmpty(_config.RootPrefix)) return clean;
            string prefix = _config.RootPrefix.Trim('/');
            return string.IsNullOrEmpty(clean) ? prefix + "/" : prefix + "/" + clean;
        }

        private string ToVirtualPath(string s3Key)
        {
            string clean = s3Key;
            if (!string.IsNullOrEmpty(_config.RootPrefix))
            {
                string prefix = _config.RootPrefix.Trim('/') + "/";
                if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean.Substring(prefix.Length);
                }
            }
            return "\\" + clean.TrimEnd('/').Replace('/', '\\');
        }

        public Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default)
        {
            string label = $"{DisplayName} ({_config.BucketName})";
            if (label.Length > 31) label = label.Substring(0, 31);

            return Task.FromResult(new StorageVolumeInfo
            {
                TotalSize = 10UL * 1024 * 1024 * 1024 * 1024, // 10 TB virtual capacity
                FreeSize = 5UL * 1024 * 1024 * 1024 * 1024,
                VolumeLabel = label,
                FileSystemName = "NTFS"
            });
        }

        public async Task<StorageItemMetadata?> GetMetadataAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(path) || path == "\\" || path == "/")
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

            string key = ToS3Key(path);

            // Check if directory placeholder exists
            try
            {
                var dirReq = new ListObjectsV2Request
                {
                    BucketName = _config.BucketName,
                    Prefix = key.EndsWith('/') ? key : key + "/",
                    MaxKeys = 1
                };
                var dirRes = await _s3Client.ListObjectsV2Async(dirReq, ct).ConfigureAwait(false);
                if (dirRes.KeyCount > 0)
                {
                    return new StorageItemMetadata
                    {
                        Path = path,
                        Name = Path.GetFileName(path.TrimEnd('\\', '/')),
                        IsDirectory = true,
                        Size = 0,
                        CreatedTime = DateTimeOffset.UtcNow,
                        LastAccessTime = DateTimeOffset.UtcNow,
                        LastModifiedTime = DateTimeOffset.UtcNow,
                        Attributes = FileAttributes.Directory
                    };
                }
            }
            catch { }

            // Check if file object exists
            try
            {
                var metaReq = new GetObjectMetadataRequest
                {
                    BucketName = _config.BucketName,
                    Key = key
                };
                var metaRes = await _s3Client.GetObjectMetadataAsync(metaReq, ct).ConfigureAwait(false);

                return new StorageItemMetadata
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    Size = metaRes.ContentLength,
                    IsDirectory = false,
                    CreatedTime = metaRes.LastModified,
                    LastAccessTime = metaRes.LastModified,
                    LastModifiedTime = metaRes.LastModified,
                    Attributes = FileAttributes.Normal,
                    ETag = metaRes.ETag
                };
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct = default)
        {
            string prefix = ToS3Key(path);
            if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith('/'))
            {
                prefix += "/";
            }

            var list = new List<StorageItemMetadata>();
            string? continuationToken = null;

            do
            {
                var req = new ListObjectsV2Request
                {
                    BucketName = _config.BucketName,
                    Prefix = prefix,
                    Delimiter = "/",
                    ContinuationToken = continuationToken
                };

                var res = await _s3Client.ListObjectsV2Async(req, ct).ConfigureAwait(false);

                // Subdirectories (CommonPrefixes)
                foreach (string commonPrefix in res.CommonPrefixes)
                {
                    string dirName = commonPrefix.TrimEnd('/').Substring(prefix.Length);
                    if (string.IsNullOrEmpty(dirName)) continue;

                    list.Add(new StorageItemMetadata
                    {
                        Path = ToVirtualPath(commonPrefix),
                        Name = dirName,
                        IsDirectory = true,
                        Size = 0,
                        CreatedTime = DateTimeOffset.UtcNow,
                        LastAccessTime = DateTimeOffset.UtcNow,
                        LastModifiedTime = DateTimeOffset.UtcNow,
                        Attributes = FileAttributes.Directory
                    });
                }

                // Files
                foreach (S3Object s3Obj in res.S3Objects)
                {
                    if (s3Obj.Key.EndsWith('/') || s3Obj.Key == prefix)
                        continue;

                    string fileName = Path.GetFileName(s3Obj.Key);
                    list.Add(new StorageItemMetadata
                    {
                        Path = ToVirtualPath(s3Obj.Key),
                        Name = fileName,
                        Size = s3Obj.Size,
                        IsDirectory = false,
                        CreatedTime = s3Obj.LastModified,
                        LastAccessTime = s3Obj.LastModified,
                        LastModifiedTime = s3Obj.LastModified,
                        Attributes = FileAttributes.Normal,
                        ETag = s3Obj.ETag
                    });
                }

                continuationToken = res.NextContinuationToken;
            } while (!string.IsNullOrEmpty(continuationToken));

            return list;
        }

        public async Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct = default)
        {
            string key = ToS3Key(path);
            var req = new GetObjectRequest
            {
                BucketName = _config.BucketName,
                Key = key
            };

            if (offset > 0 || length.HasValue)
            {
                long start = offset;
                long end = length.HasValue ? (offset + length.Value - 1) : long.MaxValue;
                req.ByteRange = new ByteRange(start, end);
            }

            var res = await _s3Client.GetObjectAsync(req, ct).ConfigureAwait(false);
            return res.ResponseStream;
        }

        public async Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct = default)
        {
            string key = ToS3Key(path);
            using var transferUtil = new TransferUtility(_s3Client);
            var uploadReq = new TransferUtilityUploadRequest
            {
                BucketName = _config.BucketName,
                Key = key,
                InputStream = contentStream
            };
            await transferUtil.UploadAsync(uploadReq, ct).ConfigureAwait(false);
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            string key = ToS3Key(path);
            if (!key.EndsWith('/')) key += "/";

            var req = new PutObjectRequest
            {
                BucketName = _config.BucketName,
                Key = key,
                InputStream = new MemoryStream(Array.Empty<byte>())
            };
            await _s3Client.PutObjectAsync(req, ct).ConfigureAwait(false);
        }

        public async Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            string key = ToS3Key(path);
            var req = new DeleteObjectRequest
            {
                BucketName = _config.BucketName,
                Key = key
            };
            await _s3Client.DeleteObjectAsync(req, ct).ConfigureAwait(false);
        }

        public async Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            string prefix = ToS3Key(path);
            if (!prefix.EndsWith('/')) prefix += "/";

            if (recursive)
            {
                var listReq = new ListObjectsV2Request
                {
                    BucketName = _config.BucketName,
                    Prefix = prefix
                };
                var listRes = await _s3Client.ListObjectsV2Async(listReq, ct).ConfigureAwait(false);
                foreach (var obj in listRes.S3Objects)
                {
                    await _s3Client.DeleteObjectAsync(_config.BucketName, obj.Key, ct).ConfigureAwait(false);
                }
            }
            else
            {
                await _s3Client.DeleteObjectAsync(_config.BucketName, prefix, ct).ConfigureAwait(false);
            }
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            string srcKey = ToS3Key(sourcePath);
            string dstKey = ToS3Key(destinationPath);

            var copyReq = new CopyObjectRequest
            {
                SourceBucket = _config.BucketName,
                SourceKey = srcKey,
                DestinationBucket = _config.BucketName,
                DestinationKey = dstKey
            };
            await _s3Client.CopyObjectAsync(copyReq, ct).ConfigureAwait(false);

            await _s3Client.DeleteObjectAsync(_config.BucketName, srcKey, ct).ConfigureAwait(false);
        }

        public async Task CopyAsync(string sourcePath, string destinationPath, ConflictResolutionMode conflictResolution, CancellationToken ct = default)
        {
            string srcKey = ToS3Key(sourcePath);
            string dstKey = ToS3Key(destinationPath);

            var existingDst = await GetMetadataAsync(destinationPath, ct).ConfigureAwait(false);
            if (existingDst != null)
            {
                if (conflictResolution == ConflictResolutionMode.Skip)
                    return;
                if (conflictResolution == ConflictResolutionMode.RenameSource)
                {
                    string dir = Path.GetDirectoryName(destinationPath) ?? "";
                    string baseName = Path.GetFileNameWithoutExtension(destinationPath);
                    string ext = Path.GetExtension(destinationPath);
                    int counter = 1;
                    while (await GetMetadataAsync(destinationPath, ct).ConfigureAwait(false) != null)
                    {
                        destinationPath = Path.Combine(dir, $"{baseName} ({counter}){ext}");
                        counter++;
                    }
                    dstKey = ToS3Key(destinationPath);
                }
            }

            var copyReq = new CopyObjectRequest
            {
                SourceBucket = _config.BucketName,
                SourceKey = srcKey,
                DestinationBucket = _config.BucketName,
                DestinationKey = dstKey
            };
            await _s3Client.CopyObjectAsync(copyReq, ct).ConfigureAwait(false);
        }

        public Task SetLastModifiedTimeAsync(string path, DateTimeOffset lastModifiedTime, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposeClient)
            {
                _s3Client.Dispose();
            }
            return ValueTask.CompletedTask;
        }
    }
}
