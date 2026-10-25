using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using OpenMount.Core.Storage.S3;
using Xunit;

namespace OpenMount.Tests
{
    public class S3StorageProviderTests
    {
        private readonly Mock<IAmazonS3> _mockS3;
        private readonly S3StorageConfig _config;
        private readonly S3StorageProvider _provider;

        public S3StorageProviderTests()
        {
            _mockS3 = new Mock<IAmazonS3>();
            _config = new S3StorageConfig
            {
                BucketName = "test-cloud-bucket",
                Region = "us-east-1",
                RootPrefix = "mydata"
            };
            _provider = new S3StorageProvider(_config, _mockS3.Object, "Test S3 Cloud");
        }

        [Fact]
        public async Task Test_GetMetadataAsync_ExistingObject()
        {
            string vPath = @"\images\logo.png";
            string expectedKey = "mydata/images/logo.png";

            var metaResponse = new GetObjectMetadataResponse
            {
                ContentLength = 1048576,
                LastModified = DateTime.UtcNow,
                ETag = "\"abc123etag\""
            };

            _mockS3.Setup(s => s.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.BucketName == "test-cloud-bucket" && r.Key == expectedKey),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(metaResponse);

            var meta = await _provider.GetMetadataAsync(vPath);

            Assert.NotNull(meta);
            Assert.False(meta.IsDirectory);
            Assert.Equal(1048576, meta.Size);
            Assert.Equal("\"abc123etag\"", meta.ETag);
        }

        [Fact]
        public async Task Test_GetMetadataAsync_NotFound_ReturnsNull()
        {
            string vPath = @"\non_existent.bin";
            string expectedKey = "mydata/non_existent.bin";

            _mockS3.Setup(s => s.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.Key == expectedKey),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });

            // Dir check also returns empty
            _mockS3.Setup(s => s.ListObjectsV2Async(
                It.IsAny<ListObjectsV2Request>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ListObjectsV2Response { KeyCount = 0 });

            var meta = await _provider.GetMetadataAsync(vPath);
            Assert.Null(meta);
        }

        [Fact]
        public async Task Test_ListDirectoryAsync_ReturnsFilesAndPrefixes()
        {
            string dirPath = @"\documents";
            string expectedPrefix = "mydata/documents/";

            var listResponse = new ListObjectsV2Response
            {
                CommonPrefixes = new List<string> { "mydata/documents/finance/" },
                S3Objects = new List<S3Object>
                {
                    new S3Object
                    {
                        Key = "mydata/documents/report.pdf",
                        Size = 2048,
                        LastModified = DateTime.UtcNow
                    }
                }
            };

            _mockS3.Setup(s => s.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.BucketName == "test-cloud-bucket" && r.Prefix == expectedPrefix && r.Delimiter == "/"),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(listResponse);

            var items = await _provider.ListDirectoryAsync(dirPath);

            Assert.Equal(2, items.Count);

            var dir = items.FirstOrDefault(i => i.IsDirectory);
            Assert.NotNull(dir);
            Assert.Equal("finance", dir.Name);

            var file = items.FirstOrDefault(i => !i.IsDirectory);
            Assert.NotNull(file);
            Assert.Equal("report.pdf", file.Name);
            Assert.Equal(2048, file.Size);
        }

        [Fact]
        public async Task Test_OpenReadAsync_ByteRangeStreaming()
        {
            string vPath = @"\stream.dat";
            byte[] expectedData = Encoding.UTF8.GetBytes("Chunk data from cloud range request!");
            var responseStream = new MemoryStream(expectedData);

            var getObjResponse = new GetObjectResponse
            {
                ResponseStream = responseStream
            };

            _mockS3.Setup(s => s.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.BucketName == "test-cloud-bucket" && r.Key == "mydata/stream.dat" && r.ByteRange.Start == 100 && r.ByteRange.End == 150),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(getObjResponse);

            using var stream = await _provider.OpenReadAsync(vPath, 100, 51);
            using var reader = new StreamReader(stream);
            string read = await reader.ReadToEndAsync();

            Assert.Equal("Chunk data from cloud range request!", read);
        }
    }
}
