// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Moq;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.FunctionApps
{
    /// <summary>Tests canonical ordering for staged membership blobs.</summary>
    [TestClass]
    public class BlobStorageRepositoryOrderingTests
    {
        private static readonly string[] _conformanceCorpus =
        {
            "f0e1d2c3-b4a5-9687-7869-5a4b3c2d1e0f",
            "00000100-0000-0000-0000-000000000000",
            "00000001-0000-0000-0000-000000000000",
            "80000000-0000-0000-0000-000000000000",
            "7fffffff-ffff-ffff-ffff-ffffffffffff",
            "00000000-8000-0000-0000-000000000000",
            "00000000-7fff-0000-0000-000000000000",
            "00000000-0000-8000-0000-000000000000",
            "00000000-0000-7fff-0000-000000000000",
            "00000000-0000-0000-8000-000000000000",
            "00000000-0000-0000-7f00-000000000000",
            "00000000-0000-0000-0000-000080000000",
            "00000000-0000-0000-0000-00007f000000",
            "00000000-0000-0000-0000-000000000080",
            "00000000-0000-0000-0000-00000000007f",
            "ffffffff-ffff-ffff-ffff-ffffffffffff",
            "00000000-0000-0000-0000-000000000000",
        };

        private static List<Guid> ExpectedCanonicalOrder(IEnumerable<string> ids) =>
            ids.Select(Guid.Parse)
               .Distinct()
               .OrderBy(g => g.ToString("D"), StringComparer.Ordinal)
               .ToList();

        [TestMethod]
        public async Task UploadGroupMembershipFromGuidsAsync_WritesMembersInCanonicalOrder()
        {
            var written = new MemoryStream();
            var blobClient = new Mock<BlobClient>();
            blobClient.Setup(x => x.OpenWriteAsync(true, It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(written);

            var container = new Mock<BlobContainerClient>();
            container.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(blobClient.Object);

            var repository = new BlobStorageRepository(container.Object);

            // The input order is intentionally undefined.
            var sourceMemberIds = new HashSet<Guid>(_conformanceCorpus.Select(Guid.Parse));

            await repository.UploadGroupMembershipFromGuidsAsync(
                "/group/part.json",
                sourceMemberIds,
                new AzureADGroup { ObjectId = Guid.NewGuid() },
                Guid.NewGuid(),
                Guid.NewGuid(),
                exclusionary: false,
                membershipObtainerDryRunEnabled: false,
                query: "[]");

            var json = Encoding.UTF8.GetString(written.ToArray());
            var actual = JsonSerializer.Deserialize<GroupMembership>(json).SourceMembers.Select(x => x.ObjectId).ToList();

            CollectionAssert.AreEqual(ExpectedCanonicalOrder(_conformanceCorpus), actual,
                "UploadGroupMembershipFromGuidsAsync wrote members in an order other than the canonical one.");
        }

        [TestMethod]
        public async Task MergeAndStreamUserBlobsAsync_WritesMergedMembersInCanonicalOrder()
        {
            var duplicate = "00000000-0000-0000-0000-000000000001";
            var first = new[]
            {
                CompleteUser(_conformanceCorpus[0], "first-0"),
                CompleteUser(duplicate, "first-duplicate"),
                CompleteUser(_conformanceCorpus[5], "first-5"),
                CompleteUser(_conformanceCorpus[7], "first-7")
            };
            var second = new[]
            {
                CompleteUser(_conformanceCorpus[2], "second-2"),
                CompleteUser(_conformanceCorpus[9], "second-9"),
                CompleteUser(duplicate, "second-duplicate"),
                CompleteUser(_conformanceCorpus[6], "second-6")
            };

            var container = new Mock<BlobContainerClient>();

            var page = Azure.Page<BlobItem>.FromValues(
                new[]
                {
                    BlobsModelFactory.BlobItem(name: "prefix/part1.json"),
                    BlobsModelFactory.BlobItem(name: "prefix/part2.json")
                },
                continuationToken: null,
                response: Mock.Of<Response>());
            container.Setup(x => x.GetBlobsAsync(It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Returns(AsyncPageable<BlobItem>.FromPages(new[] { page }));
            container.Setup(x => x.GetBlobClient("prefix/part1.json")).Returns(SourceBlob(first));
            container.Setup(x => x.GetBlobClient("prefix/part2.json")).Returns(SourceBlob(second));

            BinaryData uploaded = null;
            var destinationClient = new Mock<BlobClient>();
            destinationClient.Setup(x => x.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()))
                             .Callback<BinaryData, bool, CancellationToken>((data, overwrite, token) => uploaded = data)
                             .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
            container.Setup(x => x.GetBlobClient("/group/aggregated.json")).Returns(destinationClient.Object);

            var repository = new BlobStorageRepository(container.Object);

            var count = await repository.MergeAndStreamUserBlobsAsync(
                "prefix/",
                "/group/aggregated.json",
                new AzureADGroup { ObjectId = Guid.NewGuid() },
                Guid.NewGuid(),
                Guid.NewGuid(),
                exclusionary: false,
                membershipObtainerDryRunEnabled: false,
                query: "[]");

            Assert.IsNotNull(uploaded, "The merged membership was never uploaded.");

            var actual = JsonSerializer.Deserialize<GroupMembership>(uploaded.ToString()).SourceMembers;

            var expected = first.Concat(second)
                                .GroupBy(user => user.ObjectId)
                                .Select(group => group.First())
                                .OrderBy(user => user.ObjectId.ToString("D"), StringComparer.Ordinal)
                                .ToList();

            CollectionAssert.AreEqual(
                expected.Select(user => JsonSerializer.Serialize(user)).ToList(),
                actual.Select(user => JsonSerializer.Serialize(user)).ToList(),
                "MergeAndStreamUserBlobsAsync changed member order or content.");

            Assert.AreEqual(expected.Count, actual.Count);
            Assert.AreEqual(expected.Count, count);
            Assert.AreEqual(1, actual.Count(user => user.ObjectId == Guid.Parse(duplicate)));
        }

        private static AzureADUser CompleteUser(string id, string marker) => new AzureADUser
        {
            ObjectId = Guid.Parse(id),
            DisplayName = marker,
            Properties = new Dictionary<string, object> { ["marker"] = marker },
            MembershipAction = MembershipAction.Add,
            SourceGroup = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SourceGroups = new List<Guid> { Guid.Parse("22222222-2222-2222-2222-222222222222") }
        };

        private static BlobClient SourceBlob(IEnumerable<AzureADUser> users)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(users);

            var client = new Mock<BlobClient>();
            client.Setup(x => x.OpenReadAsync(It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(() => new MemoryStream(bytes));
            return client.Object;
        }
    }
}
