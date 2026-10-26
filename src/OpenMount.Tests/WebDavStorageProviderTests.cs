using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenMount.Core.Storage.WebDav;
using Xunit;

namespace OpenMount.Tests
{
    public class WebDavStorageProviderTests
    {
        private class MockWebDavHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string path = request.RequestUri!.AbsolutePath;

                if (request.Method.Method == "PROPFIND")
                {
                    string depth = request.Headers.TryGetValues("Depth", out var vals) ? vals.FirstOrDefault() ?? "0" : "0";

                    if (path.EndsWith("/"))
                    {
                        if (depth == "0")
                        {
                            // Root quota / metadata
                            string xml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<D:multistatus xmlns:D=""DAV:"">
  <D:response>
    <D:href>/</D:href>
    <D:propstat>
      <D:status>HTTP/1.1 200 OK</D:status>
      <D:prop>
        <D:resourcetype><D:collection/></D:resourcetype>
        <D:quota-available-bytes>536870912000</D:quota-available-bytes>
        <D:quota-used-bytes>107374182400</D:quota-used-bytes>
      </D:prop>
    </D:propstat>
  </D:response>
</D:multistatus>";
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MultiStatus)
                            {
                                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
                            });
                        }
                        else
                        {
                            // Directory listing
                            string xml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<D:multistatus xmlns:D=""DAV:"">
  <D:response>
    <D:href>/</D:href>
    <D:propstat>
      <D:status>HTTP/1.1 200 OK</D:status>
      <D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop>
    </D:propstat>
  </D:response>
  <D:response>
    <D:href>/testfile.txt</D:href>
    <D:propstat>
      <D:status>HTTP/1.1 200 OK</D:status>
      <D:prop>
        <D:resourcetype/>
        <D:getcontentlength>1024</D:getcontentlength>
        <D:getlastmodified>Wed, 21 Oct 2026 07:28:00 GMT</D:getlastmodified>
      </D:prop>
    </D:propstat>
  </D:response>
  <D:response>
    <D:href>/Documents/</D:href>
    <D:propstat>
      <D:status>HTTP/1.1 200 OK</D:status>
      <D:prop>
        <D:resourcetype><D:collection/></D:resourcetype>
        <D:getlastmodified>Wed, 21 Oct 2026 07:28:00 GMT</D:getlastmodified>
      </D:prop>
    </D:propstat>
  </D:response>
</D:multistatus>";
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MultiStatus)
                            {
                                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
                            });
                        }
                    }
                    else
                    {
                        // Single file stat
                        string xml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<D:multistatus xmlns:D=""DAV:"">
  <D:response>
    <D:href>/testfile.txt</D:href>
    <D:propstat>
      <D:status>HTTP/1.1 200 OK</D:status>
      <D:prop>
        <D:resourcetype/>
        <D:getcontentlength>1024</D:getcontentlength>
        <D:getlastmodified>Wed, 21 Oct 2026 07:28:00 GMT</D:getlastmodified>
      </D:prop>
    </D:propstat>
  </D:response>
</D:multistatus>";
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MultiStatus)
                        {
                            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
                        });
                    }
                }
                else if (request.Method == HttpMethod.Get)
                {
                    byte[] data = Encoding.UTF8.GetBytes("Hello WebDAV content stream!");
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(data)
                    };
                    return Task.FromResult(response);
                }
                else if (request.Method == HttpMethod.Put || request.Method.Method == "MKCOL" || request.Method == HttpMethod.Delete || request.Method.Method == "MOVE")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        [Fact]
        public async Task Test_WebDav_VolumeInfo_ParsesQuotaRfc4331()
        {
            var config = new WebDavStorageConfig
            {
                ServerUrl = "https://webdav.example.com",
                Username = "user",
                Password = "password"
            };

            var mockClient = new HttpClient(new MockWebDavHandler());
            await using var provider = new WebDavStorageProvider(config, "WebDAV Test", mockClient);

            await provider.InitializeAsync();
            var vol = await provider.GetVolumeInfoAsync();

            Assert.Equal("NTFS", vol.FileSystemName);
            Assert.Equal(536870912000UL, vol.FreeSize);
            Assert.Equal(536870912000UL + 107374182400UL, vol.TotalSize);
        }

        [Fact]
        public async Task Test_WebDav_ListDirectory_And_GetMetadata()
        {
            var config = new WebDavStorageConfig
            {
                ServerUrl = "https://webdav.example.com",
                Username = "user",
                Password = "password"
            };

            var mockClient = new HttpClient(new MockWebDavHandler());
            await using var provider = new WebDavStorageProvider(config, "WebDAV Test", mockClient);

            var items = await provider.ListDirectoryAsync("\\");
            Assert.Equal(2, items.Count);

            var file = items.FirstOrDefault(i => !i.IsDirectory);
            Assert.NotNull(file);
            Assert.Equal("testfile.txt", file.Name);
            Assert.Equal(1024, file.Size);

            var dir = items.FirstOrDefault(i => i.IsDirectory);
            Assert.NotNull(dir);
            Assert.Equal("Documents", dir.Name);

            var meta = await provider.GetMetadataAsync("\\testfile.txt");
            Assert.NotNull(meta);
            Assert.Equal(1024, meta.Size);
            Assert.False(meta.IsDirectory);
        }

        [Fact]
        public async Task Test_WebDav_Read_Create_Move_Delete()
        {
            var config = new WebDavStorageConfig
            {
                ServerUrl = "https://webdav.example.com",
                Username = "user",
                Password = "password"
            };

            var mockClient = new HttpClient(new MockWebDavHandler());
            await using var provider = new WebDavStorageProvider(config, "WebDAV Test", mockClient);

            // Read
            using (var stream = await provider.OpenReadAsync("\\testfile.txt", 0, null))
            using (var reader = new StreamReader(stream))
            {
                string text = await reader.ReadToEndAsync();
                Assert.Equal("Hello WebDAV content stream!", text);
            }

            // Create file
            using (var mem = new MemoryStream(Encoding.UTF8.GetBytes("new data")))
            {
                await provider.CreateFileAsync("\\upload.dat", mem);
            }

            // Create dir
            await provider.CreateDirectoryAsync("\\NewFolder");

            // Move
            await provider.MoveAsync("\\upload.dat", "\\renamed.dat");

            // Delete
            await provider.DeleteFileAsync("\\renamed.dat");
        }
    }
}
