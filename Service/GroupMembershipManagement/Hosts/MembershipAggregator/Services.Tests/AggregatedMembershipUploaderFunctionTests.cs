// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Hosts.MembershipAggregator.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
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
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private AggregatedMembershipUploaderFunction _function;
        private Dictionary<string, string> _uploadedBlobs;
        private HashSet<string> _existingSourcePaths;
        private SyncJob _syncJob;
        private Guid _groupId;

        [TestInitialize]
        public void Setup()
        {
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _function = new AggregatedMembershipUploaderFunction(NullLogger<AggregatedMembershipUploaderFunction>.Instance, _blobStorageRepository.Object);
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
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(membersToAdd)),
                CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(membersToRemove)),
                CurrentUtcDateTime = DateTime.UtcNow
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
        public async Task UploadAggregatedMembershipAsync_WithOnlyRemovals_ReturnsSuccess()
        {
            // Arrange
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var removeList = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = Guid.NewGuid() },
                new AzureADUser { ObjectId = Guid.NewGuid() }
            };
            var expectedRemovalCount = removeList.Count;

            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = string.Empty,
                CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(removeList)),
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            Assert.AreEqual(expectedRemovalCount, response.MemberCount);
            Assert.IsTrue(_uploadedBlobs.ContainsKey(response.FilePath));
            Assert.AreEqual(expectedRemovalCount, removeList.Count);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithMissingSourcePath_ReturnsFailure()
        {
            // Arrange
            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = "  ",
                CompressedMembersToAddJson = string.Empty,
                CompressedMembersToRemoveJson = string.Empty,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
            StringAssert.Contains(response.ErrorMessage, "Source membership file path is missing");
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithCustomTimestamp_UploadsExpectedContent()
        {
            // Arrange
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var addMembers = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = Guid.NewGuid() },
                new AzureADUser { ObjectId = Guid.NewGuid() }
            };
            var removeMembers = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = Guid.NewGuid() }
            };

            var expectedAdds = addMembers.Count;
            var expectedRemoves = removeMembers.Count;
            var currentTime = new DateTime(2025, 1, 5, 12, 30, 0, DateTimeKind.Utc);

            _syncJob.RunId = Guid.NewGuid();
            _syncJob.Query = "SELECT * FROM Users";
            _syncJob.DestinationName = new DestinationName { Id = _syncJob.Id, Name = "Destination Team" };
            _syncJob.DestinationEmail = new DestinationEmail { Id = _syncJob.Id, Email = "team@example.com" };
            _syncJob.IsDryRunEnabled = true;

            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(addMembers)),
                CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(removeMembers)),
                CurrentUtcDateTime = currentTime
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            Assert.AreEqual(expectedAdds + expectedRemoves, response.MemberCount);

            var expectedFilePath = MembershipFilePathHelper.BuildFilePath(_syncJob, _groupId, "Aggregated", currentTime);
            Assert.AreEqual(expectedFilePath, response.FilePath);
            Assert.IsTrue(_uploadedBlobs.ContainsKey(response.FilePath));

            var aggregatedMembership = GetUploadedMembership(response.FilePath);

            Assert.IsNotNull(aggregatedMembership);
            Assert.AreEqual(_syncJob.RunId, aggregatedMembership.RunId);
            Assert.AreEqual(_syncJob.Id, aggregatedMembership.SyncJobId);
            Assert.AreEqual(_syncJob.Query, aggregatedMembership.Query);
            Assert.AreEqual(_syncJob.IsDryRunEnabled, aggregatedMembership.MembershipObtainerDryRunEnabled);
            Assert.AreEqual(expectedAdds + expectedRemoves, aggregatedMembership.SourceMembers.Count);
            Assert.AreEqual(expectedAdds, aggregatedMembership.TotalMembersToAdd);
            Assert.AreEqual(expectedRemoves, aggregatedMembership.TotalMembersToRemove);
            Assert.AreEqual(expectedAdds + expectedRemoves, aggregatedMembership.ProjectedMemberCount);
            Assert.AreEqual("Destination Team", aggregatedMembership.Destination.Name);
            Assert.AreEqual("team@example.com", aggregatedMembership.Destination.Email);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithNullSyncJob_ReturnsFailure()
        {
            // Arrange
            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = null!,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = $"/{_groupId}/source.json",
                CompressedMembersToAddJson = string.Empty,
                CompressedMembersToRemoveJson = string.Empty,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.ErrorMessage));
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithEmptyGroupId_ReturnsFailure()
        {
            // Arrange
            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = Guid.Empty,
                SourceMembershipFilePath = "/ignored/path.json",
                CompressedMembersToAddJson = string.Empty,
                CompressedMembersToRemoveJson = string.Empty,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
            StringAssert.Contains(response.ErrorMessage, "GroupId");
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WhenBlobMetadataIsNull_ReturnsFailure()
        {
            // Arrange
            var sourcePath = $"/{_groupId}/source.json";
            _blobStorageRepository
                .Setup(x => x.GetBlobMetadataAsync(sourcePath))
                .ReturnsAsync((BlobMetadataResult)null!);

            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = string.Empty,
                CompressedMembersToRemoveJson = string.Empty,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
            StringAssert.Contains(response.ErrorMessage, "blob was not found");
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WhenUploadFails_ReturnsFailure()
        {
            // Arrange
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            _blobStorageRepository
                .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .ThrowsAsync(new InvalidOperationException("upload failure"));

            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = string.Empty,
                CompressedMembersToRemoveJson = string.Empty,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
            StringAssert.Contains(response.ErrorMessage, "upload failure");
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WhenSourceBlobMissing_ReturnsFailure()
        {
            // Arrange
            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = "/missing/path.json",
                CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNull(response.FilePath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.ErrorMessage));
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithInvalidCompressedAddContent_ReturnsFailure()
        {
            // Arrange
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = "not-valid-base64",
                CompressedMembersToRemoveJson = string.Empty,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.ErrorMessage));
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithInvalidJsonAfterDecompression_ReturnsFailure()
        {
            // Arrange
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var invalidJson = TextCompressor.Compress("{ this is not valid json }");

            var request = new AggregatedMembershipUploadRequest
            {
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                SourceMembershipFilePath = sourcePath,
                CompressedMembersToAddJson = invalidJson,
                CompressedMembersToRemoveJson = string.Empty,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Act
            var response = await _function.UploadAggregatedMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.ErrorMessage));
        }

        private GroupMembership GetUploadedMembership(string filePath)
        {
            Assert.IsTrue(_uploadedBlobs.ContainsKey(filePath), "Expected uploaded blob content to be present.");
            var compressedContent = _uploadedBlobs[filePath];
            var json = TextCompressor.Decompress(compressedContent);
            return JsonSerializer.Deserialize<GroupMembership>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }
}
