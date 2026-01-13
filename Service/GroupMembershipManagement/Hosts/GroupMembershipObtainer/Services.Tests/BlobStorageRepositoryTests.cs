// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;
using Tests.FunctionApps.Mocks;

namespace Tests.Services
{
    [TestClass]
    public class BlobStorageRepositoryTests
    {
        private MockBlobStorageRepository _mockBlobStorageRepository;

        [TestInitialize]
        public void Initialize()
        {
            _mockBlobStorageRepository = new MockBlobStorageRepository();
        }

        [TestMethod]
        public async Task DeleteFilesByPrefixExcludeLatestAsync()
        {
            var prefix = "even";
            var latestBlob = _mockBlobStorageRepository.Blobs
                                .Where(x => x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                                .MaxBy(x => x.Properties.LastModified);

            var otherBlobs = _mockBlobStorageRepository.Blobs
                                .Count(x => !x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase));

            await _mockBlobStorageRepository.DeleteFilesByPrefixAsync(prefix, excludeLatest: true);

            Assert.IsNotNull(_mockBlobStorageRepository.Blobs.FirstOrDefault(x => x.Name == latestBlob.Name));
            Assert.AreEqual(1, _mockBlobStorageRepository.Blobs.Count(x => x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)));
            Assert.AreEqual(otherBlobs, _mockBlobStorageRepository.Blobs.Count(x => !x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)));
        }

        [TestMethod]
        public async Task DeleteFilesByPrefixIncludeLatestAsync()
        {
            var prefix = "even";
            var latestBlob = _mockBlobStorageRepository.Blobs
                                .Where(x => x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                                .MaxBy(x => x.Properties.LastModified);

            var otherBlobs = _mockBlobStorageRepository.Blobs
                                .Count(x => !x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase));

            await _mockBlobStorageRepository.DeleteFilesByPrefixAsync(prefix, excludeLatest: false);

            Assert.IsNull(_mockBlobStorageRepository.Blobs.FirstOrDefault(x => x.Name == latestBlob.Name));
            Assert.AreEqual(0, _mockBlobStorageRepository.Blobs.Count(x => x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)));
            Assert.AreEqual(otherBlobs, _mockBlobStorageRepository.Blobs.Count(x => !x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)));
        }

        [TestMethod]
        public void ExtractGroupMembershipSourceMembers()
        {
            var groupMembership = new Models.ServiceBus.GroupMembership();

            groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = Guid.Empty });
            groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = Guid.Empty });

            for (int i = 0; i < 300000; i++)
            {
                groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = Guid.NewGuid() });
            }

            var json = System.Text.Json.JsonSerializer.Serialize(groupMembership);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var guids = Repositories.BlobStorage.GroupMembershipSourceMembersStreamingExtractor.Extract(memoryStream);
            groupMembership.SourceMembers.ForEach(x => Assert.IsTrue(guids.Contains(x.ObjectId)));
        }

        [TestMethod]
        public void EnumerateGuids_YieldsAllGuidsFromStream()
        {
            // Arrange - create a GroupMembership JSON with known GUIDs
            var expectedGuids = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToList();
            var groupMembership = new Models.ServiceBus.GroupMembership();
            expectedGuids.ForEach(g => groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = g }));

            var json = System.Text.Json.JsonSerializer.Serialize(groupMembership);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act - enumerate and collect
            var enumeratedGuids = Repositories.BlobStorage.GroupMembershipSourceMembersStreamingExtractor
                .EnumerateGuids(memoryStream)
                .ToList();

            // Assert
            Assert.AreEqual(expectedGuids.Count, enumeratedGuids.Count);
            foreach (var expected in expectedGuids)
            {
                Assert.IsTrue(enumeratedGuids.Contains(expected), $"Missing GUID: {expected}");
            }
        }

        [TestMethod]
        public void EnumerateGuids_YieldsDuplicatesWhenPresent()
        {
            // Arrange - create JSON with duplicate GUIDs
            var duplicateGuid = Guid.NewGuid();
            var groupMembership = new Models.ServiceBus.GroupMembership();
            groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = duplicateGuid });
            groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = duplicateGuid });
            groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = Guid.NewGuid() });

            var json = System.Text.Json.JsonSerializer.Serialize(groupMembership);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var enumeratedGuids = Repositories.BlobStorage.GroupMembershipSourceMembersStreamingExtractor
                .EnumerateGuids(memoryStream)
                .ToList();

            // Assert - EnumerateGuids does NOT deduplicate, so we expect 3 items
            Assert.AreEqual(3, enumeratedGuids.Count);
            Assert.AreEqual(2, enumeratedGuids.Count(g => g == duplicateGuid));
        }

        [TestMethod]
        public void EnumerateGuids_HandlesEmptySourceMembers()
        {
            // Arrange
            var groupMembership = new Models.ServiceBus.GroupMembership();
            var json = System.Text.Json.JsonSerializer.Serialize(groupMembership);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var enumeratedGuids = Repositories.BlobStorage.GroupMembershipSourceMembersStreamingExtractor
                .EnumerateGuids(memoryStream)
                .ToList();

            // Assert
            Assert.AreEqual(0, enumeratedGuids.Count);
        }

        [TestMethod]
        public void EnumerateGuids_HandlesLargeDataSet()
        {
            // Arrange - 100K members to ensure streaming works across multiple chunks
            const int memberCount = 100000;
            var expectedGuids = Enumerable.Range(0, memberCount).Select(_ => Guid.NewGuid()).ToList();
            var groupMembership = new Models.ServiceBus.GroupMembership();
            expectedGuids.ForEach(g => groupMembership.SourceMembers.Add(new Models.AzureADUser { ObjectId = g }));

            var json = System.Text.Json.JsonSerializer.Serialize(groupMembership);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var enumeratedGuids = Repositories.BlobStorage.GroupMembershipSourceMembersStreamingExtractor
                .EnumerateGuids(memoryStream)
                .ToList();

            // Assert
            Assert.AreEqual(memberCount, enumeratedGuids.Count);
        }

        [TestMethod]
        public void EnumerateUsers_YieldsAllUsersFromArray()
        {
            // Arrange - create a JSON array of AzureADUser (the format MembersReaderFunction produces)
            var users = new System.Collections.Generic.List<Models.AzureADUser>
            {
                new Models.AzureADUser { ObjectId = Guid.NewGuid(), Mail = "user1@test.com", DisplayName = "User One" },
                new Models.AzureADUser { ObjectId = Guid.NewGuid(), Mail = "user2@test.com", DisplayName = "User Two" },
                new Models.AzureADUser { ObjectId = Guid.NewGuid(), Mail = "user3@test.com", DisplayName = "User Three" }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(users);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var enumeratedUsers = Repositories.BlobStorage.UserArrayStreamingExtractor
                .EnumerateUsers(memoryStream)
                .ToList();

            // Assert
            Assert.AreEqual(users.Count, enumeratedUsers.Count);
            for (int i = 0; i < users.Count; i++)
            {
                Assert.AreEqual(users[i].ObjectId, enumeratedUsers[i].ObjectId);
                Assert.AreEqual(users[i].Mail, enumeratedUsers[i].Mail);
                Assert.AreEqual(users[i].DisplayName, enumeratedUsers[i].DisplayName);
            }
        }

        [TestMethod]
        public void EnumerateUsers_HandlesEmptyArray()
        {
            // Arrange
            var users = new System.Collections.Generic.List<Models.AzureADUser>();
            var json = System.Text.Json.JsonSerializer.Serialize(users);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var enumeratedUsers = Repositories.BlobStorage.UserArrayStreamingExtractor
                .EnumerateUsers(memoryStream)
                .ToList();

            // Assert
            Assert.AreEqual(0, enumeratedUsers.Count);
        }

        [TestMethod]
        public void EnumerateUsers_HandlesLargeDataSet()
        {
            // Arrange - 10K users to ensure streaming works across multiple chunks
            const int userCount = 10000;
            var users = Enumerable.Range(0, userCount)
                .Select(i => new Models.AzureADUser 
                { 
                    ObjectId = Guid.NewGuid(), 
                    Mail = $"user{i}@test.com",
                    DisplayName = $"User {i}"
                })
                .ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(users);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var enumeratedUsers = Repositories.BlobStorage.UserArrayStreamingExtractor
                .EnumerateUsers(memoryStream)
                .ToList();

            // Assert
            Assert.AreEqual(userCount, enumeratedUsers.Count);
            for (int i = 0; i < userCount; i++)
            {
                Assert.AreEqual(users[i].ObjectId, enumeratedUsers[i].ObjectId);
            }
        }

        [TestMethod]
        public void EnumerateUsers_PreservesAllProperties()
        {
            // Arrange - test that all AzureADUser properties are preserved
            var user = new Models.AzureADUser
            {
                ObjectId = Guid.NewGuid(),
                Mail = "test@example.com",
                UserPrincipalName = "test@example.com",
                DisplayName = "Test User",
                OnPremisesImmutableId = "immutableId123",
                SourceGroup = Guid.NewGuid(),
                MembershipAction = Models.MembershipAction.Add
            };

            var users = new System.Collections.Generic.List<Models.AzureADUser> { user };
            var json = System.Text.Json.JsonSerializer.Serialize(users);
            var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var enumeratedUsers = Repositories.BlobStorage.UserArrayStreamingExtractor
                .EnumerateUsers(memoryStream)
                .ToList();

            // Assert
            Assert.AreEqual(1, enumeratedUsers.Count);
            var result = enumeratedUsers[0];
            Assert.AreEqual(user.ObjectId, result.ObjectId);
            Assert.AreEqual(user.Mail, result.Mail);
            Assert.AreEqual(user.UserPrincipalName, result.UserPrincipalName);
            Assert.AreEqual(user.DisplayName, result.DisplayName);
            Assert.AreEqual(user.OnPremisesImmutableId, result.OnPremisesImmutableId);
            Assert.AreEqual(user.SourceGroup, result.SourceGroup);
            Assert.AreEqual(user.MembershipAction, result.MembershipAction);
        }
    }
}
