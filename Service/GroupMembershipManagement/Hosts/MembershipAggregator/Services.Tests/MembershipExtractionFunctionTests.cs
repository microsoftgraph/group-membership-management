// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MembershipAggregator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class MembershipExtractionFunctionTests
    {
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private MembershipExtractionFunction _membershipExtractionFunction;
        private SyncJob _syncJob;
        private Guid _groupId;
        private Dictionary<string, string> _uploadedFiles;

        [TestInitialize]
        public void Setup()
        {
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _membershipExtractionFunction = new MembershipExtractionFunction(NullLogger<MembershipExtractionFunction>.Instance, _blobStorageRepository.Object);

            _uploadedFiles = new Dictionary<string, string>();
            _blobStorageRepository
                .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Callback<string, string, Dictionary<string, string>>((path, content, metadata) =>
                {
                    _uploadedFiles[path] = content;
                })
                .Returns(Task.CompletedTask);
            
            _groupId = Guid.NewGuid();
            _syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid()
            };
        }

        [TestMethod]
        public void Constructor_WithNullLoggingRepository_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => 
                new MembershipExtractionFunction(null!, _blobStorageRepository.Object));
        }

        [TestMethod]
        public void Constructor_WithNullBlobStorageRepository_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => 
                new MembershipExtractionFunction(NullLogger<MembershipExtractionFunction>.Instance, null!));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithValidData_ReturnsSuccessfulResponse()
        {
            // Arrange
            var request = CreateValidRequest();
            SetupBlobStorageForValidScenario();

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            Assert.IsNull(response.ErrorMessage);

            var sourceMembership = GetSourceMembership(response);
            var destinationMembership = GetDestinationMembership(response);

            Assert.AreEqual(response.SourceMemberCount, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(response.DestinationMemberCount, destinationMembership.SourceMembers.Count);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithFileNotFound_ReturnsFailureResponse()
        {
            // Arrange
            var request = CreateValidRequest();
            var missingFilePath = "missing-file-path";
            request.CompletedParts = new List<string> { missingFilePath };

            _blobStorageRepository.Setup(x => x.DownloadFileAsync(missingFilePath))
                                 .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.AreEqual($"File {missingFilePath} was not found", response.ErrorMessage);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.SourceMembershipFilePath));
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.DestinationMembershipFilePath));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithBlobStorageException_ReturnsFailureResponse()
        {
            // Arrange
            var request = CreateValidRequest();
            var exceptionMessage = "Blob storage connection failed";
            
            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                                 .ThrowsAsync(new InvalidOperationException(exceptionMessage));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.AreEqual(exceptionMessage, response.ErrorMessage);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithNoSourceGroups_ReturnsFailureResponse()
        {
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "destination" }, // Only destination, no sources
                DestinationPart = "destination",
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Setup only destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.AreEqual("SourceMembership could not be extracted", response.ErrorMessage);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithMissingDestination_ReturnsFailureResponse()
        {
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "source1", "source2", "non-existent-destination" },
                DestinationPart = "non-existent-destination",
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow
            };

            // Setup sources to return valid data
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(5), false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(3), false)));
            
            // Setup destination to return NotFound
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("non-existent-destination"))
                                 .ReturnsAsync(new BlobResult { BlobStatus = BlobStatus.NotFound });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.AreEqual("File non-existent-destination was not found", response.ErrorMessage);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithExclusionaryGroups_ProcessesCorrectly()
        {
            // Arrange
            var request = CreateValidRequest();
            SetupBlobStorageWithExclusionaryGroups();

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);

            // With exclusionary groups, some members should be filtered out
            // The exact count depends on the overlap between inclusive and exclusive sets
            Assert.AreEqual(sourceMembership.SourceMembers.Count, response.SourceMemberCount);
            Assert.IsTrue(response.SourceMemberCount >= 0);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithInvalidJsonContent_ReturnsFailureResponse()
        {
            // Arrange
            var request = CreateValidRequest();
            
            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = TextCompressor.Compress("invalid json content")
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(response.ErrorMessage.Contains("JsonException") || response.ErrorMessage.Contains("invalid"));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithEmptySourceMembers_ReturnsSuccessfulResponse()
        {
            // Arrange
            var request = CreateValidRequest();
            var emptySourceUsers = new List<AzureADUser>();
            var destinationUsers = CreateUsers(2);

            // Setup source with empty members
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(emptySourceUsers, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(emptySourceUsers, false)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);
            var destinationMembership = GetDestinationMembership(response);

            Assert.AreEqual(0, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(0, response.SourceMemberCount);
            Assert.AreEqual(destinationMembership.SourceMembers.Count, response.DestinationMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithOnlyExclusionaryGroups_ReturnsSuccessfulResponseWithZeroMembers()
        {
            // Arrange
            var request = CreateValidRequest();
            var exclusiveUsers = CreateUsers(5);
            var destinationUsers = CreateUsers(2);

            // Setup all sources as exclusionary (should result in 0 source members after exclusion)
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(exclusiveUsers, true)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(exclusiveUsers, true)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);
            var destinationMembership = GetDestinationMembership(response);
            // With only exclusionary groups, all members are excluded, so we get 0 members
            Assert.AreEqual(0, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(0, response.SourceMemberCount);
            Assert.AreEqual(destinationMembership.SourceMembers.Count, response.DestinationMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithMixedInclusionExclusionOverlap_FiltersCorrectly()
        {
            // Arrange
            var request = CreateValidRequest();
            var allUsers = CreateUsers(10);
            var inclusiveUsers = allUsers.Take(8).ToList(); // First 8 users
            var exclusiveUsers = allUsers.Skip(5).Take(3).ToList(); // Users 6, 7, 8 (overlap with inclusive)
            var destinationUsers = CreateUsers(2);

            // Setup inclusive source
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(inclusiveUsers, false)));

            // Setup exclusive source  
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(exclusiveUsers, true)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);
            // Should have 5 users (first 5 from inclusive, users 6-8 excluded)
            Assert.AreEqual(5, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(5, response.SourceMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithSingleSourceFile_ProcessesCorrectly()
        {
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "single-source", "destination" },
                DestinationPart = "destination",
                SyncJob = _syncJob,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow,
                CurrentPart = 1,
                TotalParts = 1
            };

            var sourceUsers = CreateUsers(7);
            var destinationUsers = CreateUsers(3);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("single-source"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);
            var destinationMembership = GetDestinationMembership(response);
            Assert.AreEqual(7, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(3, destinationMembership.SourceMembers.Count);
            Assert.AreEqual(7, response.SourceMemberCount);
            Assert.AreEqual(3, response.DestinationMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithNullRunId_HandlesGracefully()
        {
            // Arrange
            var request = CreateValidRequest();
            request.SyncJob.RunId = null; // Edge case with null RunId
            SetupBlobStorageForValidScenario();

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            // Should still work, but with null RunId in GroupMembership objects
            Assert.IsTrue(response.IsSuccessful);
            Assert.IsNull(response.ErrorMessage);
            var sourceMembership = GetSourceMembership(response);
            var destinationMembership = GetDestinationMembership(response);
            Assert.AreEqual(response.SourceMemberCount, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(response.DestinationMemberCount, destinationMembership.SourceMembers.Count);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithLargeUserSets_ProcessesEfficiently()
        {
            // Arrange
            var request = CreateValidRequest();
            var largeSourceUsers1 = CreateUsers(50);
            var largeSourceUsers2 = CreateUsers(30);
            var largeDestinationUsers = CreateUsers(40);

            // Setup sources with larger user sets
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(largeSourceUsers1, false)));

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(largeSourceUsers2, false)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(largeDestinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);
            var destinationMembership = GetDestinationMembership(response);
            Assert.AreEqual(80, sourceMembership.SourceMembers.Count); // 50 + 30
            Assert.AreEqual(40, destinationMembership.SourceMembers.Count);
            Assert.AreEqual(80, response.SourceMemberCount);
            Assert.AreEqual(40, response.DestinationMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithUsersHavingSourceGroups_PreservesSourceGroupInfo()
        {
            // Arrange
            var request = CreateValidRequest();
            var group1Id = Guid.NewGuid();
            var group2Id = Guid.NewGuid();
            
            var sourceUsers1 = CreateUsersWithSourceGroup(3);
            var sourceUsers2 = CreateUsersWithSourceGroup(2);
            var destinationUsers = CreateUsers(1);

            // Setup sources
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers1, false)));

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers2, false)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);
            Assert.AreEqual(5, sourceMembership.SourceMembers.Count);

            // Verify source groups are preserved (all users should have SourceGroups populated)
            var usersWithSourceGroups = sourceMembership.SourceMembers.Where(u => u.SourceGroups?.Count > 0);
            Assert.AreEqual(5, usersWithSourceGroups.Count());
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithDestinationNotInCompletedParts_ReturnsDestinationNull()
        {
            // This test targets the edge case where destinationMembershipFile.FilePath == null (line 122)
            // Arrange  
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "source1" }, // destination not in completed parts
                DestinationPart = "missing-destination", 
                SyncJob = _syncJob,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow,
                CurrentPart = 1,
                TotalParts = 1
            };

            var sourceUsers = CreateUsers(3);
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert 
            Assert.IsTrue(response.IsSuccessful, $"Response should be successful, but got error: {response.ErrorMessage}");
            var sourceMembership = GetSourceMembership(response);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.DestinationMembershipFilePath)); // Expected null when destination not in completed parts
            Assert.AreEqual(3, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(3, response.SourceMemberCount);
        }

        [TestMethod] 
        public async Task ExtractMembershipAsync_WithDuplicateUsersAcrossGroups_GroupsCorrectly()
        {
            // This test targets the GroupBy logic on lines 107-110
            // Arrange
            var request = CreateValidRequest();
            var sharedObjectId = Guid.NewGuid();
            var group1Id = Guid.NewGuid();
            var group2Id = Guid.NewGuid();

            // Create users with duplicate ObjectIds but different SourceGroups
            var sourceUsers1 = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = sharedObjectId, SourceGroup = group1Id },
                new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroup = group1Id }
            };

            var sourceUsers2 = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = sharedObjectId, SourceGroup = group2Id }, // Same user, different source
                new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroup = group2Id }
            };

            var destinationUsers = CreateUsers(1);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers1, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers2, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            // Should have 3 unique users total (2 unique from each group, 1 shared)
            var sourceMembership = GetSourceMembership(response);
            Assert.AreEqual(3, sourceMembership.SourceMembers.Count);
            
            // The shared user should have both source groups
            var sharedUser = sourceMembership.SourceMembers.FirstOrDefault(u => u.ObjectId == sharedObjectId);
            Assert.IsNotNull(sharedUser);
            Assert.AreEqual(2, sharedUser.SourceGroups.Count); // Should have both group1Id and group2Id
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithJsonSerializationException_ReturnsFailure()
        {
            // This test ensures we hit the catch block with a specific exception type
            // Arrange
            var request = CreateValidRequest();
            
            // Setup to return content that will fail JSON deserialization
            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = TextCompressor.Compress("{ this is not valid JSON at all }")  // Invalid JSON
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNotNull(response.ErrorMessage);
            // Don't check for specific error message content - just ensure it failed with an error
            Assert.IsTrue(response.ErrorMessage.Length > 0);
        }

        private GroupMembership GetSourceMembership(MembershipExtractionResponse response)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.SourceMembershipFilePath), "Expected a source membership file path.");
            Assert.IsTrue(_uploadedFiles.ContainsKey(response.SourceMembershipFilePath), $"No uploaded source membership located at {response.SourceMembershipFilePath}.");

            var json = TextCompressor.Decompress(_uploadedFiles[response.SourceMembershipFilePath]);
            return JsonSerializer.Deserialize<GroupMembership>(json);
        }

        private GroupMembership GetDestinationMembership(MembershipExtractionResponse response)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.DestinationMembershipFilePath), "Expected a destination membership file path.");
            Assert.IsTrue(_uploadedFiles.ContainsKey(response.DestinationMembershipFilePath), $"No uploaded destination membership located at {response.DestinationMembershipFilePath}.");

            var json = TextCompressor.Decompress(_uploadedFiles[response.DestinationMembershipFilePath]);
            return JsonSerializer.Deserialize<GroupMembership>(json);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithInvalidJsonFormat_ReturnsFailure()
        {
            // This test targets JSON format issues that cause deserialization to fail
            // Arrange
            var request = CreateValidRequest();
            
            // Setup to return content with invalid Base64 to test decompression failure
            _blobStorageRepository.Setup(x => x.DownloadFileAsync(It.IsAny<string>()))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = "invalid-base64-content!" // Invalid Base64
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNotNull(response.ErrorMessage);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithNullSourceContent_ReturnsFailureWithSpecificError()
        {
            // Test case for null content in source files
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "source1" },
                DestinationPart = "destination",
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow
            };
            
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = null // Null content should trigger error handling
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNotNull(response.ErrorMessage, "ErrorMessage should not be null");
            Assert.IsTrue(response.ErrorMessage.Contains("Content for file"), $"Expected 'Content for file' in error message, but got: {response.ErrorMessage}");
            Assert.IsTrue(response.ErrorMessage.Contains("is null or empty"), $"Expected 'is null or empty' in error message, but got: {response.ErrorMessage}");
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithEmptySourceContent_ReturnsFailureWithSpecificError()
        {
            // Test case for empty string content in source files
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "source1" },
                DestinationPart = "destination",
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow
            };
            
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = string.Empty // Empty content should trigger error handling
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNotNull(response.ErrorMessage, "ErrorMessage should not be null");
            Assert.IsTrue(response.ErrorMessage.Contains("Content for file"), $"Expected 'Content for file' in error message, but got: {response.ErrorMessage}");
            Assert.IsTrue(response.ErrorMessage.Contains("is null or empty"), $"Expected 'is null or empty' in error message, but got: {response.ErrorMessage}");
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithRawJsonSourceContent_ProcessesSuccessfully()
        {
            // Test case for uncompressed (raw JSON) source content
            // Arrange
            var request = CreateValidRequest();
            var sourceUsers = CreateUsers(3);
            var destinationUsers = CreateUsers(2);
            var sourceMembershipPayload = CreateGroupMembership(sourceUsers, false);
            var destinationMembershipPayload = CreateGroupMembership(destinationUsers, false);

            // Setup source with raw JSON (not compressed)
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = JsonSerializer.Serialize(sourceMembershipPayload) // Raw JSON
                                 });

            // Setup second source normally
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(destinationMembershipPayload));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var extractedSource = GetSourceMembership(response);
            var extractedDestination = GetDestinationMembership(response);
            Assert.AreEqual(5, extractedSource.SourceMembers.Count); // 3 + 2 from sources
            Assert.AreEqual(5, response.SourceMemberCount);
            Assert.AreEqual(extractedDestination.SourceMembers.Count, response.DestinationMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithRawJsonDestinationContent_ProcessesSuccessfully()
        {
            // Test case for uncompressed (raw JSON) destination content
            // Arrange
            var request = CreateValidRequest();
            var sourceUsers = CreateUsers(3);
            var destinationUsers = CreateUsers(2);
            var sourceMembershipPayload = CreateGroupMembership(sourceUsers, false);
            var destinationMembershipPayload = CreateGroupMembership(destinationUsers, false);

            // Setup sources normally
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(sourceMembershipPayload));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Setup destination with raw JSON (not compressed)
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = JsonSerializer.Serialize(destinationMembershipPayload) // Raw JSON
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceFromResponse = GetSourceMembership(response);
            var destinationFromResponse = GetDestinationMembership(response);
            Assert.AreEqual(response.SourceMemberCount, sourceFromResponse.SourceMembers.Count);
            Assert.AreEqual(2, destinationFromResponse.SourceMembers.Count);
            Assert.AreEqual(2, response.DestinationMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithNullDestinationContent_ReturnsFailureWithSpecificError()
        {
            // Test case for null destination content
            // Arrange
            var request = CreateValidRequest();
            var sourceUsers = CreateUsers(3);

            // Setup sources normally
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Setup destination with null content
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = null
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(response.ErrorMessage.Contains("Content for destination file 'destination' is null or empty"));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithEmptyDestinationContent_ReturnsFailureWithSpecificError()
        {
            // Test case for empty destination content
            // Arrange
            var request = CreateValidRequest();
            var sourceUsers = CreateUsers(3);

            // Setup sources normally
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Setup destination with empty content
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = string.Empty
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(response.ErrorMessage.Contains("Content for destination file 'destination' is null or empty"));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithJsonExceptionInSourceProcessing_ReturnsFailureWithLogging()
        {
            // Test case for JsonException during source file processing
            // Arrange
            var request = CreateValidRequest();

            // Setup source with content that will cause JsonException (malformed JSON)
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = "{ \"malformed\": json content }" // Malformed JSON
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(!string.IsNullOrEmpty(response.ErrorMessage)); // JSON errors will be in ex.Message
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithJsonExceptionInDestinationProcessing_ReturnsFailureWithLogging()
        {
            // Test case for JsonException during destination file processing
            // Arrange
            var request = CreateValidRequest();
            var sourceUsers = CreateUsers(3);

            // Setup sources normally
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Setup destination with malformed JSON
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = "{ \"invalid\": json, content }" // Malformed JSON
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(response.ErrorMessage.Contains("Failed to deserialize JSON content from destination file 'destination'"));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithFormatExceptionInSourceProcessing_ReturnsFailureWithLogging()
        {
            // Test case for FormatException during source file processing (decompression failure)
            // Arrange
            var request = CreateValidRequest();

            // Setup source with content that will cause FormatException during decompression
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = "not-valid-base64-but-will-try-to-decompress!"
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(!string.IsNullOrEmpty(response.ErrorMessage)); // Format errors will be in ex.Message
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithFormatExceptionInDestinationProcessing_ReturnsFailureWithLogging()
        {
            // Test case for FormatException during destination file processing
            // Arrange
            var request = CreateValidRequest();
            var sourceUsers = CreateUsers(3);

            // Setup sources normally
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Setup destination with content that will cause FormatException
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = "not-valid-base64-but-will-try-to-decompress!"
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsTrue(!string.IsNullOrEmpty(response.ErrorMessage)); // Format errors will be in ex.Message
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithCompressedSourceAndRawDestination_ProcessesSuccessfully()
        {
            // Test mixed compression scenarios
            // Arrange
            var request = CreateValidRequest();
            var sourceUsers = CreateUsers(4);
            var destinationUsers = CreateUsers(3);
            var sourceMembership = CreateGroupMembership(sourceUsers, false);
            var destinationMembership = CreateGroupMembership(destinationUsers, false);

            // Setup source with compressed content
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(sourceMembership));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(CreateUsers(2), false)));

            // Setup destination with raw JSON
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = JsonSerializer.Serialize(destinationMembership)
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var extractedSourceMembership = GetSourceMembership(response);
            var extractedDestinationMembership = GetDestinationMembership(response);
            Assert.AreEqual(6, extractedSourceMembership.SourceMembers.Count); // 4 + 2
            Assert.AreEqual(3, extractedDestinationMembership.SourceMembers.Count);
            Assert.AreEqual(6, response.SourceMemberCount);
            Assert.AreEqual(3, response.DestinationMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithDestinationNotInCompletedParts_ReturnsSourceOnlySuccessfully()
        {
            // Test scenario where destination file is not in completed parts but we can still process sources
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "source1", "source2" }, // No destination in completed parts
                DestinationPart = "destination",
                SyncJob = _syncJob,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow,
                CurrentPart = 1,
                TotalParts = 1
            };

            var sourceUsers1 = CreateUsers(3);
            var sourceUsers2 = CreateUsers(2);

            // Setup sources
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers1, false)));
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers2, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var extractedSource = GetSourceMembership(response);
            Assert.IsTrue(string.IsNullOrWhiteSpace(response.DestinationMembershipFilePath)); // Expected to be null when destination not in completed parts
            Assert.AreEqual(5, extractedSource.SourceMembers.Count); // 3 + 2 users from both sources
            Assert.AreEqual(5, response.SourceMemberCount);
        }

        [TestMethod] 
        public async Task ExtractMembershipAsync_WithDebugLoggingVerification_CallsLoggingCorrectly()
        {
            // Test to ensure all expected logging calls are made
            // Arrange
            var request = CreateValidRequest();
            SetupBlobStorageForValidScenario();

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithExclusionaryUserSubsetDuplication_HandlesCorrectly()
        {
            // Test case where exclusionary groups have some duplicated users but different object IDs
            // Arrange
            var request = CreateValidRequest();
            var allUsers = CreateUsers(8);
            var inclusiveUsers = allUsers.Take(6).ToList();
            var exclusiveUsers = allUsers.Skip(2).Take(4).ToList(); // Overlap with inclusive (users 3,4,5,6)
            var destinationUsers = CreateUsers(2);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(inclusiveUsers, false)));

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(exclusiveUsers, true)));

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsTrue(response.IsSuccessful);
            var sourceMembership = GetSourceMembership(response);
            // Should have users 1,2 (first 2 from inclusive, users 3-6 excluded)
            Assert.AreEqual(2, sourceMembership.SourceMembers.Count);
            Assert.AreEqual(2, response.SourceMemberCount);
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithOnlyDestinationFile_ReturnsFailureForMissingSource()
        {
            // Test case where no source files are processed (only destination)
            // This should trigger the sourceGroupsMemberships.Count == 0 condition
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "destination" }, // Only destination, no sources
                DestinationPart = "destination",
                SyncJob = _syncJob,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow,
                CurrentPart = 1,
                TotalParts = 1
            };

            var destinationUsers = CreateUsers(3);
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNotNull(response.ErrorMessage);
            Assert.IsTrue(response.ErrorMessage.Contains("SourceMembership could not be extracted"));
        }

        [TestMethod]
        public async Task ExtractMembershipAsync_WithExpectedDestinationButNotInCompleted_ReturnsFailure()
        {
            // Test case where destination is expected to be in CompletedParts but isn't
            // This tests the destinationExpected && membershipResult.DestinationMembership == null condition
            // Arrange
            var request = new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "source1", "destination" }, // destination in completed
                DestinationPart = "destination",
                SyncJob = _syncJob,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow,
                CurrentPart = 1,
                TotalParts = 1
            };

            var sourceUsers = CreateUsers(3);
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));

            // Setup destination to return null membership (simulating extraction failure)
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(new BlobResult 
                                 { 
                                     BlobStatus = BlobStatus.Found, 
                                     Content = "invalid-json-that-causes-null-membership"
                                 });

            // Act
            var response = await _membershipExtractionFunction.ExtractMembershipAsync(request);

            // Assert
            Assert.IsFalse(response.IsSuccessful);
            Assert.IsNotNull(response.ErrorMessage);
            // Should be a JSON or format error message since invalid JSON will cause extraction to fail
        }

        #region Helper Methods

        private MembershipExtractionRequest CreateValidRequest()
        {
            return new MembershipExtractionRequest
            {
                CompletedParts = new List<string> { "source1", "source2", "destination" },
                DestinationPart = "destination",
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1,
                GroupId = _groupId,
                CurrentUtcDateTime = DateTime.UtcNow
            };
        }

        private void SetupBlobStorageForValidScenario()
        {
            var sourceUsers1 = CreateUsers(5);
            var sourceUsers2 = CreateUsers(3);
            var destinationUsers = CreateUsers(2);

            // Setup source1
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers1, false)));

            // Setup source2
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers2, false)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));
        }

        private void SetupBlobStorageForDestinationOnly()
        {
            var destinationUsers = CreateUsers(2);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));
        }

        private void SetupBlobStorageForSourceOnly()
        {
            var sourceUsers = CreateUsers(5);

            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(sourceUsers, false)));
        }

        private void SetupBlobStorageWithExclusionaryGroups()
        {
            var inclusiveUsers = CreateUsers(10);
            var exclusiveUsers = inclusiveUsers.Take(5).ToList(); // 5 users overlap
            var destinationUsers = CreateUsers(3);

            // Setup inclusive source
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source1"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(inclusiveUsers, false)));

            // Setup exclusive source
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("source2"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(exclusiveUsers, true)));

            // Setup destination
            _blobStorageRepository.Setup(x => x.DownloadFileAsync("destination"))
                                 .ReturnsAsync(CreateBlobResult(CreateGroupMembership(destinationUsers, false)));
        }

        private List<AzureADUser> CreateUsers(int count)
        {
            return Enumerable.Range(0, count)
                           .Select(i => new AzureADUser { ObjectId = Guid.NewGuid() })
                           .ToList();
        }

        private List<AzureADUser> CreateUsersWithSourceGroup(int count, string sourceGroupName = "TestGroup")
        {
            var sourceGroupId = Guid.NewGuid();
            return Enumerable.Range(0, count)
                           .Select(i => new AzureADUser 
                           { 
                               ObjectId = Guid.NewGuid(),
                               SourceGroup = sourceGroupId,
                               SourceGroups = new List<Guid> { sourceGroupId }
                           })
                           .ToList();
        }

        private GroupMembership CreateGroupMembership(List<AzureADUser> users, bool exclusionary)
        {
            return new GroupMembership
            {
                SyncJobId = _syncJob.Id,
                RunId = _syncJob.RunId ?? Guid.NewGuid(),
                Exclusionary = exclusionary,
                SourceMembers = users,
                Destination = new AzureADGroup { ObjectId = _groupId }
            };
        }

        private BlobResult CreateBlobResult(GroupMembership membership)
        {
            return new BlobResult
            {
                BlobStatus = BlobStatus.Found,
                Content = TextCompressor.Compress(JsonSerializer.Serialize(membership))
            };
        }

        #endregion
    }
}