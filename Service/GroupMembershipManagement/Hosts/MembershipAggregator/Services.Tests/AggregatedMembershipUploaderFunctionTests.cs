// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class AggregatedMembershipUploaderFunctionTests
    {
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private AggregatedMembershipUploaderFunction _function;
        private Dictionary<string, string> _uploadedBlobs;
        private HashSet<string> _existingSourcePaths;
        private SyncJob _syncJob;
        private Guid _groupId;

        [TestInitialize]
        public void Setup()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _function = new AggregatedMembershipUploaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            _uploadedBlobs = new Dictionary<string, string>();
            _existingSourcePaths = new HashSet<string>();
            _groupId = Guid.NewGuid();
            _syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), TargetOfficeGroupId = _groupId };

            _blobStorageRepository
                .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((string path, string content, Dictionary<string, string> metadata) =>
                {
                    _uploadedBlobs[path] = content;
                    return Task.CompletedTask;
                });

            _blobStorageRepository
                .Setup(x => x.GetBlobMetadataAsync(It.IsAny<string>()))
                .ReturnsAsync((string path) =>
                {
                    var status = _existingSourcePaths.Contains(path) ? BlobStatus.Found : BlobStatus.NotFound;
                    return new BlobMetadataResult { BlobStatus = status, Metadata = null };
                });
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithValidData_ReturnsSuccess()
        {
            // Arrange
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var membersToAdd = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = Guid.NewGuid() }
            };

            var membersToRemove = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = Guid.NewGuid() }
            };

            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(membersToAdd)),
                CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(membersToRemove)),
                CurrentUtcDateTime = DateTime.UtcNow,
                RunId = Guid.NewGuid()
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            Assert.IsNotNull(response.FilePath);
            Assert.AreEqual(2, response.MemberCount);
            Assert.IsTrue(_uploadedBlobs.ContainsKey(response.FilePath));
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WhenSourceBlobMissing_ReturnsFailure()
        {
            // Arrange
            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                GroupId = _groupId,
                SourceMembershipFilePath = "/missing/path.json",
                CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                CurrentUtcDateTime = DateTime.UtcNow,
                RunId = Guid.NewGuid()
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNull(response.FilePath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.ErrorMessage));
        }
    }
}
