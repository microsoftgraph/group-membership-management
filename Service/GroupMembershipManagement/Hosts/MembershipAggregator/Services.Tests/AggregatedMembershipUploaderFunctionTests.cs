// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
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
        private Dictionary<string, string> _blobStore;
        private SyncJob _syncJob;
        private Guid _groupId;

        [TestInitialize]
        public void Setup()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _function = new AggregatedMembershipUploaderFunction(_loggingRepository.Object, _blobStorageRepository.Object);
            _blobStore = new Dictionary<string, string>();
            _groupId = Guid.NewGuid();
            _syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), TargetOfficeGroupId = _groupId };

            _blobStorageRepository
                .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((string path, string content, Dictionary<string, string> metadata) =>
                {
                    _blobStore[path] = content;
                    return Task.CompletedTask;
                });

            _blobStorageRepository
                .Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                .ReturnsAsync((string path) =>
                {
                    return _blobStore.TryGetValue(path, out var storedContent)
                        ? new BlobResult { BlobStatus = BlobStatus.Found, Content = storedContent }
                        : new BlobResult { BlobStatus = BlobStatus.NotFound };
                });
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithValidData_ReturnsSuccess()
        {
            // Arrange
            var sourceMembership = new GroupMembership
            {
                SourceMembers = new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = _groupId }
            };

            var sourcePath = $"/{_groupId}/source.json";
            _blobStore[sourcePath] = TextCompressor.Compress(JsonSerializer.Serialize(sourceMembership));

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
            Assert.IsTrue(_blobStore.ContainsKey(response.FilePath));
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
