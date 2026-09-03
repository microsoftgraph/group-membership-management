// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Hosts.MembershipAggregator.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.Helpers;
using Models.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Repositories.BlobStorage;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
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
                .Setup(repository => repository.WriteMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<GroupMembership>(),
                    It.IsAny<IAsyncEnumerable<AzureADUser>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    string path,
                    GroupMembership envelope,
                    IAsyncEnumerable<AzureADUser> members,
                    Dictionary<string, string> metadata,
                    CancellationToken cancellationToken) =>
                    StoreMembershipAsync(path, envelope, members, cancellationToken));
            _blobStorageRepository
                .Setup(repository => repository.StreamMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    string path,
                    Action<GroupMembership> onMembershipDetailsKnown,
                    CancellationToken cancellationToken) =>
                    ReadMembershipAsync(
                        path,
                        onMembershipDetailsKnown,
                        cancellationToken));

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

            var function = new AggregatedMembershipUploaderFunction(
                NullLogger<AggregatedMembershipUploaderFunction>.Instance,
                _blobStorageRepository.Object);

            // Act
            var response = await function.UploadAggregatedMembershipAsync(request);

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
                .Setup(repository => repository.WriteMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<GroupMembership>(),
                    It.IsAny<IAsyncEnumerable<AzureADUser>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
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

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithExplicitStagedMode_StreamsAdditionsThenRemovals()
        {
            var sourcePath = $"/{_groupId}/source.json";
            var additionsPath = $"/{_groupId}/additions.json";
            var removalsPath = $"/{_groupId}/removals.json";
            _existingSourcePaths.Add(sourcePath);
            var addition = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Add
            };
            var removal = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Remove,
                Properties = new TeamsUserProperties
                {
                    ConversationMemberId = "MCMjMCMjNQ=="
                }
            };
            await StoreMembershipAsync(
                additionsPath,
                new GroupMembership(),
                Enumerate(addition),
                CancellationToken.None);
            await StoreMembershipAsync(
                removalsPath,
                new GroupMembership(),
                Enumerate(removal),
                CancellationToken.None);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    CompressedMembersToAddJson = "not-valid-base64",
                    CompressedMembersToRemoveJson = "not-valid-base64",
                    MembersToAddFilePath = additionsPath,
                    MembersToRemoveFilePath = removalsPath,
                    DeltaManifestFilePath = $"/{_groupId}/manifest.json",
                    UseStagedDeltaFiles = true,
                    MembersToAddCount = 1,
                    MembersToRemoveCount = 1,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsTrue(response.IsSuccessful, response.ErrorMessage);
            Assert.AreEqual(2, response.MemberCount);
            var membership = GetUploadedMembership(response.FilePath);
            Assert.AreEqual(2, membership.SourceMembers.Count);
            Assert.AreEqual(addition.ObjectId, membership.SourceMembers[0].ObjectId);
            Assert.AreEqual(MembershipAction.Add, membership.SourceMembers[0].MembershipAction);
            Assert.AreEqual(removal.ObjectId, membership.SourceMembers[1].ObjectId);
            Assert.AreEqual(MembershipAction.Remove, membership.SourceMembers[1].MembershipAction);
            var teamsRemoval = new AzureADTeamsUser
            {
                ObjectId = membership.SourceMembers[1].ObjectId,
                Properties = membership.SourceMembers[1].Properties
            };
            Assert.AreEqual("MCMjMCMjNQ==", teamsRemoval.ConversationMemberId);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithExplicitInlineMode_IgnoresStagedPaths()
        {
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);
            var addition = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Add
            };

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(new[] { addition })),
                    CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    MembersToAddFilePath = "/missing/additions",
                    MembersToRemoveFilePath = "/missing/removals",
                    UseStagedDeltaFiles = false,
                    MembersToAddCount = 1,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsTrue(response.IsSuccessful, response.ErrorMessage);
            Assert.AreEqual(1, response.MemberCount);
            _blobStorageRepository.Verify(
                repository => repository.StreamMembershipAsync(
                    "/missing/additions",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _blobStorageRepository.Verify(
                repository => repository.StreamMembershipAsync(
                    "/missing/removals",
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithExplicitInlineMode_ValidatesZeroCounts()
        {
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);
            var addition = new AzureADUser { ObjectId = Guid.NewGuid() };

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(new[] { addition })),
                    CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    UseStagedDeltaFiles = false,
                    MembersToAddCount = 0,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "Declared delta counts do not match");
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithExplicitStagedMode_RequiresBothPaths()
        {
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    MembersToAddFilePath = "/delta/additions",
                    UseStagedDeltaFiles = true,
                    MembersToAddCount = 0,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "Both staged addition and removal paths are required");
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task UploadAggregatedMembershipAsync_WithExplicitMode_RejectsNegativeCounts(bool useStagedDeltaFiles)
        {
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    MembersToAddFilePath = "/delta/additions",
                    MembersToRemoveFilePath = "/delta/removals",
                    UseStagedDeltaFiles = useStagedDeltaFiles,
                    MembersToAddCount = -1,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "Delta member counts cannot be negative");
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithHistoricalInlineMode_RejectsNegativeCounts()
        {
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);
            var addition = new AzureADUser { ObjectId = Guid.NewGuid() };

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(new[] { addition })),
                    CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    MembersToAddCount = -1,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "Delta member counts cannot be negative");
            _blobStorageRepository.Verify(
                repository => repository.WriteMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<GroupMembership>(),
                    It.IsAny<IAsyncEnumerable<AzureADUser>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithHistoricalStagedRequest_InfersStagedMode()
        {
            var sourcePath = $"/{_groupId}/source.json";
            var additionsPath = $"/{_groupId}/additions.json";
            var removalsPath = $"/{_groupId}/removals.json";
            _existingSourcePaths.Add(sourcePath);
            var addition = new AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                MembershipAction = MembershipAction.Add
            };
            await StoreMembershipAsync(additionsPath, new GroupMembership(), Enumerate(addition), CancellationToken.None);
            await StoreMembershipAsync(removalsPath, new GroupMembership(), Enumerate(), CancellationToken.None);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    MembersToAddFilePath = additionsPath,
                    MembersToRemoveFilePath = removalsPath,
                    MembersToAddCount = 1,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsTrue(response.IsSuccessful, response.ErrorMessage);
            Assert.AreEqual(1, response.MemberCount);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithOneHistoricalStagedPath_FailsWithoutInlineFallback()
        {
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    CompressedMembersToAddJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    CompressedMembersToRemoveJson = TextCompressor.Compress(JsonSerializer.Serialize(Array.Empty<AzureADUser>())),
                    MembersToAddFilePath = "/delta/additions",
                    MembersToAddCount = 0,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "Both staged addition and removal paths are required");
        }

        [TestMethod]
        public void DeltaTransportFlag_PreservesMissingAndExplicitFalse()
        {
            var historicalResponse = JsonSerializer.Deserialize<DeltaCalculatorResponse>("{}");
            var explicitInlineResponse = JsonSerializer.Deserialize<DeltaCalculatorResponse>("{\"UseStagedDeltaFiles\":false}");
            var historicalRequest = JsonSerializer.Deserialize<AggregatedMembershipUploadRequest>(
                "{\"SyncJob\":{},\"CurrentPart\":1,\"TotalParts\":1,\"SourceMembershipFilePath\":\"/source\"}");
            var explicitInlineRequest = JsonSerializer.Deserialize<AggregatedMembershipUploadRequest>(
                "{\"SyncJob\":{},\"CurrentPart\":1,\"TotalParts\":1,\"SourceMembershipFilePath\":\"/source\",\"UseStagedDeltaFiles\":false}");

            Assert.IsNotNull(historicalResponse);
            Assert.IsNull(historicalResponse.UseStagedDeltaFiles);
            Assert.IsNotNull(explicitInlineResponse);
            Assert.AreEqual(false, explicitInlineResponse.UseStagedDeltaFiles);
            Assert.IsNotNull(historicalRequest);
            Assert.IsNull(historicalRequest.UseStagedDeltaFiles);
            Assert.IsNotNull(explicitInlineRequest);
            Assert.AreEqual(false, explicitInlineRequest.UseStagedDeltaFiles);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WithCountsButNoDeltaRepresentation_FailsClosed()
        {
            var sourcePath = $"/{_groupId}/source.json";
            _existingSourcePaths.Add(sourcePath);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    MembersToAddCount = 1,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(
                response.ErrorMessage,
                "no staged or legacy delta members");
            _blobStorageRepository.Verify(
                repository => repository.WriteMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<GroupMembership>(),
                    It.IsAny<IAsyncEnumerable<AzureADUser>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WhenStagedCountDoesNotMatch_FailsBeforeUpload()
        {
            var sourcePath = $"/{_groupId}/source.json";
            var additionsPath = $"/{_groupId}/additions.json";
            var removalsPath = $"/{_groupId}/removals.json";
            _existingSourcePaths.Add(sourcePath);
            await StoreMembershipAsync(
                additionsPath,
                new GroupMembership(),
                Enumerate(new AzureADUser
                {
                    ObjectId = Guid.NewGuid(),
                    MembershipAction = MembershipAction.Add
                }),
                CancellationToken.None);
            await StoreMembershipAsync(
                removalsPath,
                new GroupMembership(),
                Enumerate(),
                CancellationToken.None);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    MembersToAddFilePath = additionsPath,
                    MembersToRemoveFilePath = removalsPath,
                    MembersToAddCount = 2,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "contains 1 members, expected 2");
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WhenStagedActionDoesNotMatch_FailsBeforeUpload()
        {
            var sourcePath = $"/{_groupId}/source.json";
            var additionsPath = $"/{_groupId}/additions.json";
            var removalsPath = $"/{_groupId}/removals.json";
            _existingSourcePaths.Add(sourcePath);
            await StoreMembershipAsync(
                additionsPath,
                new GroupMembership(),
                Enumerate(new AzureADUser
                {
                    ObjectId = Guid.NewGuid(),
                    MembershipAction = MembershipAction.Remove
                }),
                CancellationToken.None);
            await StoreMembershipAsync(
                removalsPath,
                new GroupMembership(),
                Enumerate(),
                CancellationToken.None);

            var response = await _function.UploadAggregatedMembershipAsync(
                new AggregatedMembershipUploadRequest
                {
                    SyncJob = _syncJob,
                    CurrentPart = 1,
                    TotalParts = 1,
                    GroupId = _groupId,
                    SourceMembershipFilePath = sourcePath,
                    MembersToAddFilePath = additionsPath,
                    MembersToRemoveFilePath = removalsPath,
                    MembersToAddCount = 1,
                    MembersToRemoveCount = 0,
                    CurrentUtcDateTime = DateTime.UtcNow
                });

            Assert.IsFalse(response.IsSuccessful);
            StringAssert.Contains(response.ErrorMessage, "expected Add");
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.FilePath));
        }

        [TestMethod]
        public async Task UploadAggregatedMembershipAsync_WhenCancelled_PropagatesCancellation()
        {
            var sourcePath = $"/{_groupId}/source.json";
            var additionsPath = $"/{_groupId}/additions.json";
            var removalsPath = $"/{_groupId}/removals.json";
            _existingSourcePaths.Add(sourcePath);
            await StoreMembershipAsync(
                additionsPath,
                new GroupMembership(),
                Enumerate(new AzureADUser
                {
                    ObjectId = Guid.NewGuid(),
                    MembershipAction = MembershipAction.Add
                }),
                CancellationToken.None);
            await StoreMembershipAsync(
                removalsPath,
                new GroupMembership(),
                Enumerate(),
                CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                await _function.UploadAggregatedMembershipAsync(
                    new AggregatedMembershipUploadRequest
                    {
                        SyncJob = _syncJob,
                        CurrentPart = 1,
                        TotalParts = 1,
                        GroupId = _groupId,
                        SourceMembershipFilePath = sourcePath,
                        MembersToAddFilePath = additionsPath,
                        MembersToRemoveFilePath = removalsPath,
                        MembersToAddCount = 1,
                        MembersToRemoveCount = 0,
                        CurrentUtcDateTime = DateTime.UtcNow
                    },
                    cancellation.Token);
                Assert.Fail("Expected cancellation to propagate.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task StoreMembershipAsync(
            string path,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            CancellationToken cancellationToken)
        {
            await using var stream = new MemoryStream();
            await MembershipStream.WriteAsync(
                stream,
                envelope,
                members,
                cancellationToken: cancellationToken);
            _uploadedBlobs[path] = Encoding.UTF8.GetString(stream.ToArray());
        }

        private async IAsyncEnumerable<AzureADUser> ReadMembershipAsync(
            string path,
            Action<GroupMembership> onMembershipDetailsKnown,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!_uploadedBlobs.TryGetValue(path, out var content))
            {
                throw new FileNotFoundException(path);
            }

            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await foreach (var member in MembershipStream.ReadAsync(
                stream,
                onMembershipDetailsKnown,
                cancellationToken))
            {
                yield return member;
            }
        }

        private static async IAsyncEnumerable<AzureADUser> Enumerate(
            params AzureADUser[] members)
        {
            foreach (var member in members)
            {
                yield return member;
            }

            await Task.CompletedTask;
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
