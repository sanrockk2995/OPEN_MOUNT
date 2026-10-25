using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using OpenMount.Core.FileSystem;
using OpenMount.Core.Storage.GoogleDrive;
using OpenMount.Core.Storage.Local;
using Xunit;

namespace OpenMount.Tests
{
    public class ResumableUploadAndRecoveryTests : IDisposable
    {
        private readonly string _recoveryDir;
        private readonly string _stagingDir;

        public ResumableUploadAndRecoveryTests()
        {
            _recoveryDir = Path.Combine(Path.GetTempPath(), "OpenMount_Recovery_Test_" + Guid.NewGuid().ToString("N"));
            _stagingDir = Path.Combine(Path.GetTempPath(), "OpenMount_Staging_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_recoveryDir);
            Directory.CreateDirectory(_stagingDir);

            VirtualFileDesc.UnsavedRecoveryDir = _recoveryDir;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_recoveryDir)) Directory.Delete(_recoveryDir, true);
                if (Directory.Exists(_stagingDir)) Directory.Delete(_stagingDir, true);
            }
            catch { }
        }

        [Fact]
        public void Test_FailedUpload_PreservesStagedDataInRecoveryDirectory()
        {
            var node = new VirtualFileNode("\\critical_document.docx", false);
            var desc = new VirtualFileDesc(node, 0x40000000); // Write access

            string tempStaging = Path.Combine(_stagingDir, "test_stage.tmp");
            string testContent = "Confidential user draft changes that must never be lost!";
            File.WriteAllText(tempStaging, testContent);

            desc.StagingFilePath = tempStaging;
            desc.IsModified = true; // Simulating failed upload during Cleanup

            // When handle closes without successful commit (IsModified is still true)
            desc.Dispose();

            // Assert staging file was preserved in recovery directory
            Assert.NotNull(desc.LastPreservedRecoveryPath);
            Assert.True(File.Exists(desc.LastPreservedRecoveryPath));

            string recoveredText = File.ReadAllText(desc.LastPreservedRecoveryPath);
            Assert.Equal(testContent, recoveredText);

            // Assert original temp file in staging was cleaned up
            Assert.False(File.Exists(tempStaging));
        }

        [Fact]
        public async Task Test_ResumableUpload_InitiationAndProtocolHeaders()
        {
            bool sessionInitiated = false;
            bool chunkPutReceived = false;

            var mockHandler = new MockHttpMessageHandler();
            mockHandler.Handler = req =>
            {
                // Root search or parent resolution (GET)
                if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/files?"))
                {
                    string listJson = @"{ ""files"": [] }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(listJson, Encoding.UTF8, "application/json")
                    });
                }

                // 1. Initiate Resumable Upload session (POST with uploadType=resumable)
                if (req.Method == HttpMethod.Post && req.RequestUri!.PathAndQuery.Contains("uploadType=resumable"))
                {
                    sessionInitiated = true;
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    // Google API returns session location in Location header
                    response.Headers.Location = new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=mock_session_998877");
                    response.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    return Task.FromResult(response);
                }

                // 2. Upload chunk (PUT to session URI)
                if (req.Method == HttpMethod.Put)
                {
                    chunkPutReceived = true;
                    string createdFileJson = @"{
                        ""id"": ""uploaded_file_id_123"",
                        ""name"": ""test_upload.dat"",
                        ""mimeType"": ""application/octet-stream"",
                        ""size"": ""1024""
                    }";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(createdFileJson, Encoding.UTF8, "application/json")
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

            byte[] payload = new byte[1024];
            new Random(123).NextBytes(payload);

            using (var ms = new MemoryStream(payload))
            {
                await provider.CreateFileAsync("\\test_upload.dat", ms);
            }

            Assert.True(sessionInitiated, "Resumable upload session was not initiated via uploadType=resumable");
            Assert.True(chunkPutReceived, "Chunk upload PUT was not sent to resumable session URI");
        }
    }
}
