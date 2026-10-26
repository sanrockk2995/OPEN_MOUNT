using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenMount.Core.Storage.PCloud;
using Xunit;

namespace OpenMount.Tests
{
    public class PCloudStorageProviderTests
    {
        private class MockPCloudHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string url = request.RequestUri!.ToString();

                if (url.Contains("/userinfo"))
                {
                    string json = @"{
                        ""result"": 0,
                        ""auth"": ""mock_auth_token_xyz"",
                        ""email"": ""user@pcloud.com"",
                        ""quota"": 10737418240,
                        ""usedquota"": 2147483648
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }
                else if (url.Contains("/stat"))
                {
                    string json = @"{
                        ""result"": 0,
                        ""metadata"": {
                            ""name"": ""sample.txt"",
                            ""isfolder"": false,
                            ""size"": 2048,
                            ""modified"": ""Wed, 23 Oct 2026 12:00:00 +0000""
                        }
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }
                else if (url.Contains("/listfolder"))
                {
                    string json = @"{
                        ""result"": 0,
                        ""metadata"": {
                            ""isfolder"": true,
                            ""contents"": [
                                {
                                    ""name"": ""sample.txt"",
                                    ""isfolder"": false,
                                    ""size"": 2048,
                                    ""modified"": ""Wed, 23 Oct 2026 12:00:00 +0000""
                                },
                                {
                                    ""name"": ""Photos"",
                                    ""isfolder"": true,
                                    ""size"": 0,
                                    ""modified"": ""Wed, 23 Oct 2026 12:00:00 +0000""
                                }
                            ]
                        }
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }
                else if (url.Contains("/getfilelink"))
                {
                    string json = @"{
                        ""result"": 0,
                        ""hosts"": [""mock.pcloud.cdn""],
                        ""path"": ""/download/sample.txt""
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }
                else if (url.Contains("mock.pcloud.cdn/download/sample.txt"))
                {
                    byte[] data = Encoding.UTF8.GetBytes("Hello from pCloud direct link!");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(data)
                    });
                }
                else if (url.Contains("/uploadfile") || url.Contains("/createfolder") || url.Contains("/deletefile") || url.Contains("/deletefolderrecursive") || url.Contains("/renamefile") || url.Contains("/renamefolder"))
                {
                    string json = @"{ ""result"": 0 }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        [Fact]
        public async Task Test_PCloud_Auth_And_VolumeInfo()
        {
            var config = new PCloudStorageConfig
            {
                Username = "user@pcloud.com",
                Password = "secretpassword",
                Location = "US"
            };

            var mockClient = new HttpClient(new MockPCloudHandler());
            await using var provider = new PCloudStorageProvider(config, "pCloud Test", mockClient);

            await provider.InitializeAsync();
            var vol = await provider.GetVolumeInfoAsync();

            Assert.Equal("NTFS", vol.FileSystemName);
            Assert.Equal(10737418240UL, vol.TotalSize);
            Assert.Equal(10737418240UL - 2147483648UL, vol.FreeSize);
        }

        [Fact]
        public async Task Test_PCloud_Listing_And_Streaming()
        {
            var config = new PCloudStorageConfig
            {
                AuthToken = "existing_token",
                Location = "EU"
            };

            var mockClient = new HttpClient(new MockPCloudHandler());
            await using var provider = new PCloudStorageProvider(config, "pCloud Test", mockClient);

            var items = await provider.ListDirectoryAsync("\\");
            Assert.Equal(2, items.Count);

            var file = items.FirstOrDefault(i => !i.IsDirectory);
            Assert.NotNull(file);
            Assert.Equal("sample.txt", file.Name);
            Assert.Equal(2048, file.Size);

            using var stream = await provider.OpenReadAsync("\\sample.txt", 0, null);
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();
            Assert.Equal("Hello from pCloud direct link!", content);
        }

        [Fact]
        public async Task Test_PCloud_Create_Delete_Move()
        {
            var config = new PCloudStorageConfig
            {
                AuthToken = "existing_token"
            };

            var mockClient = new HttpClient(new MockPCloudHandler());
            await using var provider = new PCloudStorageProvider(config, "pCloud Test", mockClient);

            using var mem = new MemoryStream(Encoding.UTF8.GetBytes("new pcloud file"));
            await provider.CreateFileAsync("\\folder\\newfile.bin", mem);

            await provider.CreateDirectoryAsync("\\folder\\sub");
            await provider.MoveAsync("\\folder\\newfile.bin", "\\folder\\moved.bin");
            await provider.DeleteFileAsync("\\folder\\moved.bin");
            await provider.DeleteDirectoryAsync("\\folder\\sub", recursive: true);
        }
    }
}
