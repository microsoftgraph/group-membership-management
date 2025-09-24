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
    }
}
