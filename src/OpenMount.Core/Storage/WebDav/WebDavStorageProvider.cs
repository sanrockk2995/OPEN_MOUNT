using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OpenMount.Core.Manager;

namespace OpenMount.Core.Storage.WebDav
{
    public class WebDavStorageProvider : IStorageProvider
    {
        private readonly WebDavStorageConfig _config;
        private readonly HttpClient _httpClient;
        private readonly Uri _baseUri;
        private readonly string _authHeaderValue;

        public string ProviderId => "webdav";
        public string DisplayName { get; }

        private static readonly XNamespace DavNs = "DAV:";

        public WebDavStorageProvider(WebDavStorageConfig config, string? displayName = null, HttpClient? httpClient = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            DisplayName = displayName ?? "WebDAV";

            string rawUrl = _config.ServerUrl.TrimEnd('/') + "/";
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException($"Invalid WebDAV Server URL: '{_config.ServerUrl}'", nameof(config));
            }
            _baseUri = uri;

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.Username}:{_config.Password}"));
            _authHeaderValue = $"Basic {credentials}";

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

        private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? depth = null)
        {
            var targetUri = BuildUri(path);
            var req = new HttpRequestMessage(method, targetUri);
            req.Headers.TryAddWithoutValidation("Authorization", _authHeaderValue);
            req.Headers.UserAgent.ParseAdd("OpenMount/1.0");

            if (!string.IsNullOrEmpty(depth))
            {
                req.Headers.TryAddWithoutValidation("Depth", depth);
            }

            return req;
        }

        private Uri BuildUri(string path)
        {
            string clean = path.Replace('\\', '/').TrimStart('/');
            string combined;
            if (!string.IsNullOrEmpty(_config.RootPath) && _config.RootPath != "/")
            {
                string root = _config.RootPath.Trim('/');
                combined = string.IsNullOrEmpty(clean) ? root : $"{root}/{clean}";
            }
            else
            {
                combined = clean;
            }

            if (string.IsNullOrEmpty(combined))
            {
                return _baseUri;
            }

            var segments = combined.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var seg in segments)
            {
                sb.Append('/').Append(Uri.EscapeDataString(seg));
            }

            return new Uri(_baseUri, sb.ToString().TrimStart('/'));
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            using var req = CreateRequest(new HttpMethod("PROPFIND"), "/", "0");
            using var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.MultiStatus)
            {
                throw new IOException($"WebDAV server returned HTTP {(int)res.StatusCode} ({res.ReasonPhrase}) on connection verification.");
            }
        }

        public async Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct = default)
        {
            ulong totalSize = 10UL * 1024 * 1024 * 1024 * 1024; // 10 TB fallback
            ulong freeSize = 5UL * 1024 * 1024 * 1024 * 1024;

            try
            {
                using var req = CreateRequest(new HttpMethod("PROPFIND"), "/", "0");
                req.Content = new StringContent(
                    "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n" +
                    "<D:propfind xmlns:D=\"DAV:\">\n" +
                    "  <D:prop>\n" +
                    "    <D:quota-available-bytes/>\n" +
                    "    <D:quota-used-bytes/>\n" +
                    "  </D:prop>\n" +
                    "</D:propfind>",
                    Encoding.UTF8,
                    "application/xml");

                using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (res.StatusCode == HttpStatusCode.MultiStatus || res.IsSuccessStatusCode)
                {
                    string xml = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = XDocument.Parse(xml);

                    var quotaAvail = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "quota-available-bytes");
                    var quotaUsed = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "quota-used-bytes");

                    if (quotaAvail != null && ulong.TryParse(quotaAvail.Value, out var avail))
                    {
                        freeSize = avail;
                        if (quotaUsed != null && ulong.TryParse(quotaUsed.Value, out var used))
                        {
                            totalSize = avail + used;
                        }
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

            try
            {
                using var req = CreateRequest(new HttpMethod("PROPFIND"), clean, "0");
                using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);

                if (res.StatusCode == HttpStatusCode.NotFound) return null;
                if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.MultiStatus) return null;

                string xml = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var doc = XDocument.Parse(xml);
                var response = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "response");
                if (response == null) return null;

                return ParseResponseElement(response, clean);
            }
            catch
            {
                return null;
            }
        }

        public async Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            var results = new List<StorageItemMetadata>();

            using var req = CreateRequest(new HttpMethod("PROPFIND"), clean, "1");
            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.MultiStatus)
            {
                return results;
            }

            string xml = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = XDocument.Parse(xml);
            var responses = doc.Descendants().Where(e => e.Name.LocalName == "response").ToList();

            string currentUri = BuildUri(clean).AbsolutePath.TrimEnd('/');

            foreach (var r in responses)
            {
                var href = r.Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value;
                if (string.IsNullOrEmpty(href)) continue;

                string decodedHref = Uri.UnescapeDataString(href).TrimEnd('/');
                string itemUri = decodedHref;
                if (Uri.TryCreate(decodedHref, UriKind.Absolute, out var abs))
                {
                    itemUri = abs.AbsolutePath.TrimEnd('/');
                }

                // Skip the parent directory itself
                if (string.Equals(itemUri, currentUri, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string itemName = Path.GetFileName(decodedHref);
                if (string.IsNullOrEmpty(itemName)) continue;

                string itemPath = clean == "\\" ? $"\\{itemName}" : $"{clean}\\{itemName}";
                var meta = ParseResponseElement(r, itemPath);
                if (meta != null)
                {
                    results.Add(meta);
                }
            }

            return results;
        }

        private StorageItemMetadata? ParseResponseElement(XElement response, string expectedPath)
        {
            var propstat = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "propstat" &&
                e.Descendants().Any(s => s.Name.LocalName == "status" && s.Value.Contains("200")));

            var propContainer = propstat?.Descendants().FirstOrDefault(e => e.Name.LocalName == "prop") ??
                                response.Descendants().FirstOrDefault(e => e.Name.LocalName == "prop");

            if (propContainer == null) return null;

            bool isDir = propContainer.Descendants().Any(e => e.Name.LocalName == "collection");

            long size = 0;
            var lenElem = propContainer.Descendants().FirstOrDefault(e => e.Name.LocalName == "getcontentlength");
            if (lenElem != null && long.TryParse(lenElem.Value, out var parsedSize))
            {
                size = parsedSize;
            }

            var modified = DateTimeOffset.UtcNow;
            var modElem = propContainer.Descendants().FirstOrDefault(e => e.Name.LocalName == "getlastmodified");
            if (modElem != null && DateTimeOffset.TryParse(modElem.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedMod))
            {
                modified = parsedMod;
            }

            string name = Path.GetFileName(expectedPath);
            if (string.IsNullOrEmpty(name)) name = expectedPath.Trim('\\');

            return new StorageItemMetadata
            {
                Path = expectedPath,
                Name = name,
                Size = isDir ? 0 : size,
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
            var req = CreateRequest(HttpMethod.Get, clean);

            if (length.HasValue)
            {
                req.Headers.Range = new RangeHeaderValue(offset, offset + length.Value - 1);
            }
            else if (offset > 0)
            {
                req.Headers.Range = new RangeHeaderValue(offset, null);
            }

            var res = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                res.Dispose();
                req.Dispose();
                throw new IOException($"Failed to open WebDAV file '{path}': HTTP {(int)res.StatusCode} ({res.ReasonPhrase})");
            }

            return new WebDavHttpStream(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), res, req);
        }

        public async Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            using var req = CreateRequest(HttpMethod.Put, clean);
            req.Content = new StreamContent(contentStream);

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                throw new IOException($"Failed to upload WebDAV file '{path}': HTTP {(int)res.StatusCode} ({res.ReasonPhrase})");
            }
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            using var req = CreateRequest(new HttpMethod("MKCOL"), clean);

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.MethodNotAllowed)
            {
                throw new IOException($"Failed to create WebDAV directory '{path}': HTTP {(int)res.StatusCode} ({res.ReasonPhrase})");
            }
        }

        public async Task DeleteFileAsync(string path, CancellationToken ct = default)
        {
            string clean = NormalizePath(path);
            using var req = CreateRequest(HttpMethod.Delete, clean);

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.NotFound)
            {
                throw new IOException($"Failed to delete WebDAV file '{path}': HTTP {(int)res.StatusCode} ({res.ReasonPhrase})");
            }
        }

        public async Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct = default)
        {
            await DeleteFileAsync(path, ct).ConfigureAwait(false);
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            string cleanSrc = NormalizePath(sourcePath);
            string cleanDst = NormalizePath(destinationPath);

            using var req = CreateRequest(new HttpMethod("MOVE"), cleanSrc);
            var destUri = BuildUri(cleanDst);
            req.Headers.TryAddWithoutValidation("Destination", destUri.AbsoluteUri);
            req.Headers.TryAddWithoutValidation("Overwrite", "T");

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                throw new IOException($"Failed to move WebDAV item '{sourcePath}' to '{destinationPath}': HTTP {(int)res.StatusCode} ({res.ReasonPhrase})");
            }
        }

        public async Task CopyAsync(string sourcePath, string destinationPath, ConflictResolutionMode conflictResolution, CancellationToken ct = default)
        {
            string cleanSrc = NormalizePath(sourcePath);
            string cleanDst = NormalizePath(destinationPath);

            using var req = CreateRequest(new HttpMethod("COPY"), cleanSrc);
            var destUri = BuildUri(cleanDst);
            req.Headers.TryAddWithoutValidation("Destination", destUri.AbsoluteUri);
            req.Headers.TryAddWithoutValidation("Overwrite", conflictResolution == ConflictResolutionMode.OverwriteTarget ? "T" : "F");

            using var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                if (conflictResolution == ConflictResolutionMode.Skip && res.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                    return;
                throw new IOException($"Failed to copy WebDAV item '{sourcePath}' to '{destinationPath}': HTTP {(int)res.StatusCode} ({res.ReasonPhrase})");
            }
        }

        public Task SetLastModifiedTimeAsync(string path, DateTimeOffset lastModifiedTime, CancellationToken ct = default)
        {
            return Task.CompletedTask;
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

        private class WebDavHttpStream : Stream
        {
            private readonly Stream _innerStream;
            private readonly HttpResponseMessage _response;
            private readonly HttpRequestMessage _request;

            public WebDavHttpStream(Stream innerStream, HttpResponseMessage response, HttpRequestMessage request)
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
