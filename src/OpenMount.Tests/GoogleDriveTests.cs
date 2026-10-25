using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using OpenMount.Core.Storage.GoogleDrive;
using Xunit;

namespace OpenMount.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Handler(request);
        }
    }

    public class MockHttpClientFactory : HttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public MockHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => _handler;
    }

    public class GoogleDriveTests : IDisposable
    {
        private readonly string _tempTokenDir;
        private readonly DpapiDataStore _tokenStore;

        public GoogleDriveTests()
        {
            _tempTokenDir = Path.Combine(Path.GetTempPath(), "OpenMount_Tokens_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTokenDir);
            _tokenStore = new DpapiDataStore(_tempTokenDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempTokenDir))
                {
                    Directory.Delete(_tempTokenDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task Test_DpapiDataStore_StoreAndRetrieveToken()
        {
            var token = new TokenResponse
            {
                AccessToken = "ya29.mock_access_token_12345",
                RefreshToken = "1//mock_refresh_token_67890",
                ExpiresInSeconds = 3600,
                TokenType = "Bearer"
            };

            await _tokenStore.StoreAsync("user_primary", token);

            // Verify encrypted on disk
            var files = Directory.GetFiles(_tempTokenDir, "*.tok.enc");
            Assert.Single(files);

            byte[] rawBytes = await File.ReadAllBytesAsync(files[0]);
            string rawStr = Encoding.UTF8.GetString(rawBytes);
            Assert.DoesNotContain("ya29.mock_access_token_12345", rawStr);
            Assert.DoesNotContain("1//mock_refresh_token_67890", rawStr);

            // Retrieve and decrypt
            var retrieved = await _tokenStore.GetAsync<TokenResponse>("user_primary");
            Assert.NotNull(retrieved);
            Assert.Equal(token.AccessToken, retrieved.AccessToken);
            Assert.Equal(token.RefreshToken, retrieved.RefreshToken);

            // Delete
            await _tokenStore.DeleteAsync<TokenResponse>("user_primary");
            var deleted = await _tokenStore.GetAsync<TokenResponse>("user_primary");
            Assert.Null(deleted);
        }

        [Fact]
        public async Task Test_ListDirectory_DuplicateNameDisambiguationAndWorkspaceDocs()
        {
            var mockHandler = new MockHttpMessageHandler();
            mockHandler.Handler = req =>
            {
                if (req.RequestUri!.PathAndQuery.Contains("/about"))
                {
                    string aboutJson = @"{
                        ""storageQuota"": { ""limit"": ""107374182400"", ""usage"": ""21474836480"" },
                        ""user"": { ""displayName"": ""Test Engineer"" }
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(aboutJson, Encoding.UTF8, "application/json")
                    });
                }

                if (req.RequestUri.PathAndQuery.Contains("/files"))
                {
                    // Return list with duplicate names and Google Docs
                    string listJson = @"{
                        ""files"": [
                            {
                                ""id"": ""folder_1"",
                                ""name"": ""Documents"",
                                ""mimeType"": ""application/vnd.google-apps.folder""
                            },
                            {
                                ""id"": ""doc_1"",
                                ""name"": ""Annual Report"",
                                ""mimeType"": ""application/vnd.google-apps.document""
                            },
                            {
                                ""id"": ""file_dup_1"",
                                ""name"": ""photo.jpg"",
                                ""mimeType"": ""image/jpeg"",
                                ""size"": ""102400""
                            },
                            {
                                ""id"": ""file_dup_2"",
                                ""name"": ""photo.jpg"",
                                ""mimeType"": ""image/jpeg"",
                                ""size"": ""204800""
                            }
                        ]
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(listJson, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            };

            var driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientFactory = new MockHttpClientFactory(mockHandler),
                ApplicationName = "OpenMountTests"
            });

            var config = new GoogleDriveOAuthConfig
            {
                ClientId = "mock_client",
                ClientSecret = "mock_secret",
                ExportWorkspaceDocuments = true
            };

            var provider = new GoogleDriveStorageProvider(config, driveService);

            var items = await provider.ListDirectoryAsync("\\");

            Assert.Equal(4, items.Count);

            // Folder
            var folder = items[0];
            Assert.True(folder.IsDirectory);
            Assert.Equal("Documents", folder.Name);

            // Google Doc exported as .docx
            var doc = items[1];
            Assert.False(doc.IsDirectory);
            Assert.Equal("Annual Report.docx", doc.Name);

            // Duplicate photo disambiguation
            var photo1 = items[2];
            Assert.Equal("photo.jpg", photo1.Name);

            var photo2 = items[3];
            Assert.Equal("photo (2).jpg", photo2.Name);
        }

        [Fact]
        public async Task Test_OpenReadAsync_ByteRangeStreaming()
        {
            long startRange = -1;
            long endRange = -1;

            var mockHandler = new MockHttpMessageHandler();
            mockHandler.Handler = req =>
            {
                if (req.RequestUri!.PathAndQuery.Contains("/files?"))
                {
                    string listJson = @"{
                        ""files"": [
                            {
                                ""id"": ""target_file_id"",
                                ""name"": ""large_archive.zip"",
                                ""mimeType"": ""application/zip"",
                                ""size"": ""10485760""
                            }
                        ]
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(listJson, Encoding.UTF8, "application/json")
                    });
                }

                if (req.RequestUri.PathAndQuery.Contains("/files/target_file_id?alt=media"))
                {
                    if (req.Headers.Range != null)
                    {
                        var range = req.Headers.Range.Ranges.GetEnumerator();
                        if (range.MoveNext())
                        {
                            startRange = range.Current.From ?? -1;
                            endRange = range.Current.To ?? -1;
                        }
                    }

                    byte[] chunk = Encoding.UTF8.GetBytes("Range streamed chunk payload");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(chunk)
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            };

            var driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientFactory = new MockHttpClientFactory(mockHandler),
                ApplicationName = "OpenMountTests"
            });

            var config = new GoogleDriveOAuthConfig { ClientId = "id", ClientSecret = "sec" };
            var provider = new GoogleDriveStorageProvider(config, driveService);

            // Request range offset 5000, length 28
            using var stream = await provider.OpenReadAsync("\\large_archive.zip", 5000, 28);
            using var reader = new StreamReader(stream);
            string content = await reader.ReadToEndAsync();

            Assert.Equal("Range streamed chunk payload", content);
            Assert.Equal(5000, startRange);
            Assert.Equal(5027, endRange); // 5000 + 28 - 1 = 5027
        }
    }
}
