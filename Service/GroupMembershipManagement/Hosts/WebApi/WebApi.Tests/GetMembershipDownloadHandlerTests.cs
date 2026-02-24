// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Helpers;
using Moq;
using Repositories.Contracts;
using Services;
using Services.Messages.Requests;
using System.Net;

namespace WebApi.Tests
{
    [TestClass]
    public class GetMembershipDownloadHandlerTests
    {
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _mockSyncJobRepository = null!;
        private Mock<IBlobStorageRepository> _mockBlobStorageRepository = null!;
        private GetMembershipDownloadHandler _handler = null!;

        private Guid _syncJobId;
        private Guid _runId;
        private Guid _targetGroupId;
        private SyncJob _testSyncJob = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockSyncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _mockBlobStorageRepository = new Mock<IBlobStorageRepository>();

            _handler = new GetMembershipDownloadHandler(
                _mockLoggingRepository.Object,
                _mockSyncJobRepository.Object,
                _mockBlobStorageRepository.Object);

            _syncJobId = Guid.NewGuid();
            _runId = Guid.NewGuid();
            _targetGroupId = Guid.NewGuid();

            _testSyncJob = new SyncJob
            {
                Id = _syncJobId,
                TargetOfficeGroupId = _targetGroupId,
                DestinationName = new DestinationName { Name = "Test Group" }
            };
        }

        [TestMethod]
        public async Task HappyPath_ReturnsZipFile()
        {
            // Arrange
            var blobContent = "{\"SourceMembers\":[],\"RunId\":\"" + _runId + "\"}";
            var blobPath = $"{_targetGroupId}/02232026-1200_{_runId}_Aggregated.json";
            SetupMocks(blobContent, blobPath);

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(response.FileContent);
            Assert.AreEqual($"membership_changes_{_runId}.zip", response.FileName);
            Assert.IsTrue(response.FileContent!.Length > 0);

            var extractedJson = ExtractJsonFromZip(response.FileContent);
            Assert.AreEqual(blobContent, extractedJson);
        }

        [TestMethod]
        public async Task SyncJobNotFound_ReturnsNotFound()
        {
            // Arrange
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync((SyncJob?)null);

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.IsNull(response.FileContent);
        }

        [TestMethod]
        public async Task BlobNotFound_ReturnsNotFound()
        {
            // Arrange
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(_testSyncJob);
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(
                _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.IsNull(response.FileContent);
        }

        [TestMethod]
        public async Task DownloadNotFound_ReturnsNotFound()
        {
            // Arrange - blob found but deleted before download
            var blobPath = $"{_targetGroupId}/02232026-1200_{_runId}_Aggregated.json";
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(_testSyncJob);
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(
                _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = blobPath });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.IsNull(response.FileContent);
        }

        [TestMethod]
        public async Task CompressedBlob_DecompressesAndReturnsZip()
        {
            // Arrange
            var originalJson = "{\"SourceMembers\":[],\"RunId\":\"" + _runId + "\"}";
            var compressed = TextCompressor.Compress(originalJson);
            var blobPath = $"{_targetGroupId}/02232026-1200_{_runId}_Aggregated.json";

            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(_testSyncJob);
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(
                _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = blobPath });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new BlobResult { Content = compressed });

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var extractedJson = ExtractJsonFromZip(response.FileContent!);
            Assert.AreEqual(originalJson, extractedJson);
        }

        [TestMethod]
        public async Task DatabaseException_ReturnsInternalServerError()
        {
            // Arrange
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ThrowsAsync(new InvalidOperationException("DB timeout"));

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.IsNull(response.FileContent);
        }

        [TestMethod]
        public async Task BlobException_ReturnsInternalServerError()
        {
            // Arrange
            var blobPath = $"{_targetGroupId}/02232026-1200_{_runId}_Aggregated.json";
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(_testSyncJob);
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(
                _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = blobPath });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(blobPath))
                .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.IsNull(response.FileContent);
        }

        [TestMethod]
        public async Task DownloadNullContent_ReturnsInternalServerError()
        {
            // Arrange
            var blobPath = $"{_targetGroupId}/02232026-1200_{_runId}_Aggregated.json";
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(_testSyncJob);
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(
                _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = blobPath });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new BlobResult { Content = null! });

            // Act
            var response = await _handler.ExecuteAsync(new GetMembershipDownloadRequest(_syncJobId, _runId));

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.IsNull(response.FileContent);
        }

        private void SetupMocks(string blobContent, string blobPath)
        {
            _mockSyncJobRepository.Setup(x => x.GetSyncJobAsync(_syncJobId))
                .ReturnsAsync(_testSyncJob);
            _mockBlobStorageRepository.Setup(x => x.FindAggregatedFileByRunIdAsync(
                _targetGroupId.ToString(), _runId.ToString()))
                .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.Found, Path = blobPath });
            _mockBlobStorageRepository.Setup(x => x.DownloadFileAsync(blobPath))
                .ReturnsAsync(new BlobResult { Content = blobContent });
        }

        private static string ExtractJsonFromZip(byte[] zipBytes)
        {
            using var stream = new MemoryStream(zipBytes);
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            var entry = archive.Entries[0];
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            return reader.ReadToEnd();
        }
    }
}
