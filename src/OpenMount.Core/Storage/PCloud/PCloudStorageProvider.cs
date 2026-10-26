using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenMount.Core.Storage.PCloud
{
    public class PCloudStorageProvider : IStorageProvider
    {
        private readonly PCloudStorageConfig _config;
        private readonly HttpClient _httpClient;
        private readonly string _apiBase;
        private string? _authToken;

        public string ProviderId => "pcloud";
        public string DisplayName { get; }

        public PCloudStorageProvider(PCloudStorageConfig config, string? displayName = null, HttpClient? httpClient = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            DisplayName = displayName ?? "pCloud";

            bool isEu = string.Equals(_config.Location, "EU", StringComparison.OrdinalIgnoreCase);
            _apiBase = isEu ? "https://eapi.pcloud.com" : "https://api.pcloud.com";
            _authToken = _config.AuthToken;

            if (httpClient != null)
            {
                _httpClient = httpClient;
            }
            else
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    AllowAutoRedirect = true
                };
                _httpClient = new HttpClient(handler, disposeHandler: true);
            }
        }

        private string ToPCloudPath(string path)
        {
            string clean = path.Replace('\\', '/').Trim('/');
            string root = (_config.RootPath ?? "/").Trim('/');
            if (string.IsNullOrEmpty(clean))
            {
                return string.IsNullOrEmpty(root) ? "/" : $"/{root}";
            }
            return string.IsNullOrEmpty(root) ? $"/{clean}" : $"/{root}/{clean}";
        }

        private async Task EnsureAuthenticatedAsync(CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(_authToken)) return;

            if (string.IsNullOrEmpty(_config.Username) || string.IsNullOrEmpty(_config.Password))
            {
                throw new InvalidOperationException("pCloud requires either an AuthToken or Username/Password.");
            }

            string url = $"{_apiBase}/userinfo?getauth=1&username={Uri.EscapeDataString(_config.Username)}&password={Uri.EscapeDataString(_config.Password)}";
            using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            int result = doc.RootElement.GetProperty("result").GetInt32();
            if (result != 0)
            {
                string error = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() ?? "Unknown error" : $"Error code {result}";
                throw new IOException($"pCloud authentication failed: {error}");
            }

            _authToken = doc.RootElement.GetProperty("auth").GetString();
            _config.AuthToken = _authToken;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
        }

        public async Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default)
        {
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

            ulong totalSize = 10UL * 1024 * 1024 * 1024 * 1024;
            ulong freeSize = 5UL * 1024 * 1024 * 1024 * 1024;

            try
            {
                string url = $"{_apiBase}/userinfo?auth={Uri.EscapeDataString(_authToken!)}";
                using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                    if (doc.RootElement.GetProperty("result").GetInt32() == 0)
                    {
                        ulong quota = doc.RootElement.GetProperty("quota").GetUInt64();
                        ulong used = doc.RootElement.GetProperty("usedquota").GetUInt64();
                        totalSize = quota;
                        freeSize = quota > used ? (quota - used) : 0;
                    }
                }
            }
            catch { }

            string label = DisplayName;
            if (label.Length > 31) label = label.Substring(0, 31);

            return new StorageVolumeInfo
            {
                TotalSize = totalSize,
                FreeSize = freeSize,
                VolumeLabel = label,
                FileSystemName = "NTFS"
            };
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
                    CreatedTime = DateTimeOffset.UtcNow,
                    LastAccessTime = DateTimeOffset.UtcNow,
                    LastModifiedTime = DateTimeOffset.UtcNow,
                    Attributes = FileAttributes.Directory
                };
            }

            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            string pcloudPath = ToPCloudPath(clean);

            try
            {
                string url = $"{_apiBase}/stat?path={Uri.EscapeDataString(pcloudPath)}&auth={Uri.EscapeDataString(_authToken!)}";
                using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;

                using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                if (doc.RootElement.GetProperty("result").GetInt32() != 0)
                {
                    return null;
                }

                var meta = doc.RootElement.GetProperty("metadata");
                return ParseMetadataElement(meta, clean);
            }
            catch
            {
                return null;
            }
        }

        public async Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            string pcloudPath = ToPCloudPath(clean);

            var results = new List<StorageItemMetadata>();

            try
            {
                string url = $"{_apiBase}/listfolder?path={Uri.EscapeDataString(pcloudPath)}&auth={Uri.EscapeDataString(_authToken!)}";
                using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return results;

                using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                if (doc.RootElement.GetProperty("result").GetInt32() != 0)
                {
                    return results;
                }

                var meta = doc.RootElement.GetProperty("metadata");
                if (meta.TryGetProperty("contents", out var contents))
                {
                    foreach (var item in contents.EnumerateArray())
                    {
                        string name = item.GetProperty("name").GetString() ?? "";
                        string itemPath = clean == "\\" ? $"\\{name}" : $"{clean}\\{name}";
                        var parsed = ParseMetadataElement(item, itemPath);
                        if (parsed != null)
                        {
                            results.Add(parsed);
                        }
                    }
                }
            }
            catch { }

            return results;
        }

        private static StorageItemMetadata? ParseMetadataElement(JsonElement elem, string expectedPath)
        {
            bool isDir = elem.GetProperty("isfolder").GetBoolean();
            long size = isDir ? 0 : elem.GetProperty("size").GetInt64();

            DateTimeOffset modified = DateTimeOffset.UtcNow;
            if (elem.TryGetProperty("modified", out var modProp))
            {
                if (modProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(modProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                    {
                        modified = dt;
                    }
                }
                else if (modProp.ValueKind == JsonValueKind.Number)
                {
                    modified = DateTimeOffset.FromUnixTimeSeconds(modProp.GetInt64());
                }
            }

            string name = Path.GetFileName(expectedPath);
            if (string.IsNullOrEmpty(name)) name = expectedPath.Trim('\\');

            return new StorageItemMetadata
            {
                Path = expectedPath,
                Name = name,
                Size = size,
                IsDirectory = isDir,
                CreatedTime = modified,
                LastAccessTime = modified,
                LastModifiedTime = modified,
                Attributes = isDir ? FileAttributes.Directory : FileAttributes.Normal
            };
        }

        public async Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            string pcloudPath = ToPCloudPath(clean);

            string linkUrl = $"{_apiBase}/getfilelink?path={Uri.EscapeDataString(pcloudPath)}&auth={Uri.EscapeDataString(_authToken!)}";
            using var res = await _httpClient.GetAsync(linkUrl, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.GetProperty("result").GetInt32() != 0)
            {
                string error = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() ?? "" : "";
                throw new IOException($"Failed to get pCloud download link for '{path}': {error}");
            }

            string host = doc.RootElement.GetProperty("hosts")[0].GetString()!;
            string downloadPath = doc.RootElement.GetProperty("path").GetString()!;
            string fileUrl = $"https://{host}{downloadPath}";

            var req = new HttpRequestMessage(HttpMethod.Get, fileUrl);
            if (length.HasValue)
            {
                req.Headers.Range = new RangeHeaderValue(offset, offset + length.Value - 1);
            }
            else if (offset > 0)
            {
                req.Headers.Range = new RangeHeaderValue(offset, null);
            }

            var downloadRes = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!downloadRes.IsSuccessStatusCode)
            {
                downloadRes.Dispose();
                req.Dispose();
                throw new IOException($"Failed to stream pCloud file '{path}': HTTP {(int)downloadRes.StatusCode}");
            }

            return new PCloudHttpStream(await downloadRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), downloadRes, req);
        }

        public async Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

            string parentDir = Path.GetDirectoryName(clean)?.Replace('\\', '/') ?? "/";
            string fileName = Path.GetFileName(clean);
            string parentPcloud = ToPCloudPath(parentDir);

            string uploadUrl = $"{_apiBase}/uploadfile?path={Uri.EscapeDataString(parentPcloud)}&filename={Uri.EscapeDataString(fileName)}&auth={Uri.EscapeDataString(_authToken!)}";

            using var req = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            req.Content = new StreamContent(contentStream);

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.GetProperty("result").GetInt32() != 0)
            {
                string error = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() ?? "" : "";
                throw new IOException($"Failed to upload pCloud file '{path}': {error}");
            }
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            string pcloudPath = ToPCloudPath(clean);

            string url = $"{_apiBase}/createfolder?path={Uri.EscapeDataString(pcloudPath)}&auth={Uri.EscapeDataString(_authToken!)}";
            using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
        }

        public async Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            string pcloudPath = ToPCloudPath(clean);

            string url = $"{_apiBase}/deletefile?path={Uri.EscapeDataString(pcloudPath)}&auth={Uri.EscapeDataString(_authToken!)}";
            using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
        }

        public async Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            string pcloudPath = ToPCloudPath(clean);

            string endpoint = recursive ? "deletefolderrecursive" : "deletefolder";
            string url = $"{_apiBase}/{endpoint}?path={Uri.EscapeDataString(pcloudPath)}&auth={Uri.EscapeDataString(_authToken!)}";
            using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            string cleanSrc = NormalizePath(sourcePath);
            string cleanDst = NormalizePath(destinationPath);
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

            string srcPcloud = ToPCloudPath(cleanSrc);
            string dstPcloud = ToPCloudPath(cleanDst);

            var meta = await GetMetadataAsync(cleanSrc, ct).ConfigureAwait(false);
            bool isDir = meta?.IsDirectory == true;

            string endpoint = isDir ? "renamefolder" : "renamefile";
            string url = $"{_apiBase}/{endpoint}?path={Uri.EscapeDataString(srcPcloud)}&topath={Uri.EscapeDataString(dstPcloud)}&auth={Uri.EscapeDataString(_authToken!)}";

            using var res = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";
            string clean = path.Replace('/', '\\');
            return clean.StartsWith("\\") ? clean : "\\" + clean;
        }

        public ValueTask DisposeAsync()
        {
            _httpClient.Dispose();
            return ValueTask.CompletedTask;
        }

        private class PCloudHttpStream : Stream
        {
            private readonly Stream _innerStream;
            private readonly HttpResponseMessage _response;
            private readonly HttpRequestMessage _request;

            public PCloudHttpStream(Stream innerStream, HttpResponseMessage response, HttpRequestMessage request)
            {
                _innerStream = innerStream;
                _response = response;
                _request = request;
            }

            public override bool CanRead => _innerStream.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _innerStream.Length;
            public override long Position { get => _innerStream.Position; set => throw new NotSupportedException(); }
            public override void Flush() => _innerStream.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _innerStream.ReadAsync(buffer, offset, count, ct);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _innerStream.ReadAsync(buffer, ct);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _innerStream.Dispose();
                    _response.Dispose();
                    _request.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
