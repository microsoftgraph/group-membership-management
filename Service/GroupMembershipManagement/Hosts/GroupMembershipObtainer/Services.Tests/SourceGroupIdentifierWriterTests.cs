// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    /// <summary>
    /// I14: the source-group overload of the identifier writer, used only for a one-type part's selected saved-delta
    /// output, writes <c>SourceGroup</c> on every member; production and the in-memory double store identical bytes;
    /// the identifier-only overload (both-types saved-delta output, I16) is unchanged.
    /// </summary>
    [TestClass]
    public sealed class SourceGroupIdentifierWriterTests
    {
        private const string Path = "group/part.json";

        private static readonly Guid DestinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid SyncJobId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid SourceGroupId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid MemberA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid MemberB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

        private const string ExpectedSourceGroupJson =
            "{\"Destination\":{\"ObjectId\":\"11111111-1111-1111-1111-111111111111\","
            + "\"Type\":null,\"Name\":null,\"Email\":null,\"Visibility\":null},"
            + "\"RunId\":\"22222222-2222-2222-2222-222222222222\","
            + "\"SyncJobId\":\"33333333-3333-3333-3333-333333333333\","
            + "\"Exclusionary\":true,\"MembershipObtainerDryRunEnabled\":false,\"Query\":\"[\\u0022q\\u0022]\","
            + "\"SourceMembers\":["
            + "{\"ObjectId\":\"00000000-0000-0000-0000-0000000000bb\",\"SourceGroup\":\"44444444-4444-4444-4444-444444444444\"},"
            + "{\"ObjectId\":\"aaaaaaaa-0000-0000-0000-000000000001\",\"SourceGroup\":\"44444444-4444-4444-4444-444444444444\"}"
            + "]}";

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "two members with query", new[] { MemberA, MemberB }, "[{\"type\":\"GroupMembership\"}]", false, false };
            yield return new object[] { "null query", new[] { MemberB, MemberA }, null, true, false };
            yield return new object[] { "no members", Array.Empty<Guid>(), "[]", false, true };
            yield return new object[] { "many unsorted members", Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray(), "q", true, true };
        }

        [TestMethod]
        public async Task SourceGroupOverload_Production_WritesPinnedBytes()
        {
            var azure = new AzureBlobContainerFake();

            await azure.CreateRepository().UploadGroupMembershipFromGuidsAsync(
                Path,
                new[] { MemberA, MemberB },
                new AzureADGroup { ObjectId = DestinationId },
                RunId,
                SyncJobId,
                exclusionary: true,
                membershipObtainerDryRunEnabled: false,
                query: "[\"q\"]",
                sourceGroupId: SourceGroupId);

            Assert.AreEqual(ExpectedSourceGroupJson, Encoding.UTF8.GetString(azure.GetBytes(Path)));
        }

        [DataTestMethod]
        [DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
        public async Task SourceGroupOverload_ProductionAndDouble_StoreIdenticalBytes(
            string description, Guid[] memberIds, string query, bool exclusionary, bool dryRun)
        {
            var azure = new AzureBlobContainerFake();
            var inMemory = new InMemoryBlobStorageRepository();
            var destination = new AzureADGroup { ObjectId = DestinationId };

            await azure.CreateRepository().UploadGroupMembershipFromGuidsAsync(
                Path, memberIds, destination, RunId, SyncJobId, exclusionary, dryRun, query, SourceGroupId);
            await inMemory.UploadGroupMembershipFromGuidsAsync(
                Path, memberIds, destination, RunId, SyncJobId, exclusionary, dryRun, query, SourceGroupId);

            CollectionAssert.AreEqual(azure.GetBytes(Path), inMemory.GetBytes(Path), description);
        }

        [DataTestMethod]
        [DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
        public async Task SourceGroupWriteOnce_MatchesOverwritingBytesInProductionAndDouble(
            string description, Guid[] memberIds, string query, bool exclusionary, bool dryRun)
        {
            var destination = new AzureADGroup { ObjectId = DestinationId };
            var overwriting = new AzureBlobContainerFake();
            await overwriting.CreateRepository().UploadGroupMembershipFromGuidsAsync(
                Path, memberIds, destination, RunId, SyncJobId, exclusionary, dryRun, query, SourceGroupId);
            var writeOnce = new AzureBlobContainerFake();
            var inMemory = new InMemoryBlobStorageRepository();

            var productionResult = await writeOnce.CreateRepository().UploadGroupMembershipFromGuidsWriteOnceAsync(
                Path, memberIds, destination, RunId, SyncJobId, exclusionary, dryRun, query, SourceGroupId);
            var inMemoryResult = await inMemory.UploadGroupMembershipFromGuidsWriteOnceAsync(
                Path, memberIds, destination, RunId, SyncJobId, exclusionary, dryRun, query, SourceGroupId);

            Assert.IsFalse(productionResult.AlreadyExisted, description);
            Assert.IsFalse(inMemoryResult.AlreadyExisted, description);
            Assert.AreEqual(memberIds.Length, productionResult.Count);
            Assert.AreEqual(memberIds.Length, inMemoryResult.Count);
            CollectionAssert.AreEqual(overwriting.GetBytes(Path), writeOnce.GetBytes(Path), description);
            CollectionAssert.AreEqual(writeOnce.GetBytes(Path), inMemory.GetBytes(Path), description);
        }

        [TestMethod]
        public async Task SourceGroupWriteOnce_SecondWrite_ReportsAlreadyExistedAndKeepsFirstBytes()
        {
            var destination = new AzureADGroup { ObjectId = DestinationId };
            var repository = new InMemoryBlobStorageRepository();
            await repository.UploadGroupMembershipFromGuidsWriteOnceAsync(
                Path, new[] { MemberA }, destination, RunId, SyncJobId, false, false, "[]", SourceGroupId);
            var first = repository.GetBytes(Path);

            var second = await repository.UploadGroupMembershipFromGuidsWriteOnceAsync(
                Path, new[] { MemberA, MemberB }, destination, RunId, SyncJobId, false, false, "[]", SourceGroupId);

            Assert.IsTrue(second.AlreadyExisted);
            CollectionAssert.AreEqual(first, repository.GetBytes(Path));
        }

        [TestMethod]
        public async Task SourceGroupWriteOnce_Production_CommitsOnceWithIfNoneMatchAll()
        {
            CommitBlockListOptions commitOptions = null;
            var commits = 0;
            var blockBlob = new Moq.Mock<Azure.Storage.Blobs.Specialized.BlockBlobClient>();
            blockBlob
                .Setup(client => client.StageBlockAsync(
                    Moq.It.IsAny<string>(),
                    Moq.It.IsAny<Stream>(),
                    Moq.It.IsAny<Azure.Storage.Blobs.Models.BlockBlobStageBlockOptions>(),
                    Moq.It.IsAny<CancellationToken>()))
                .ReturnsAsync(Moq.Mock.Of<Azure.Response<Azure.Storage.Blobs.Models.BlockInfo>>());
            blockBlob
                .Setup(client => client.CommitBlockListAsync(
                    Moq.It.IsAny<IEnumerable<string>>(),
                    Moq.It.IsAny<CommitBlockListOptions>(),
                    Moq.It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<string>, CommitBlockListOptions, CancellationToken>((_, options, _) =>
                {
                    commits++;
                    commitOptions = options;
                })
                .ReturnsAsync(Moq.Mock.Of<Azure.Response<Azure.Storage.Blobs.Models.BlobContentInfo>>());
            var repository = new BlobStorageRepository(
                new Moq.Mock<Azure.Storage.Blobs.BlobContainerClient>().Object,
                _ => blockBlob.Object);

            var result = await repository.UploadGroupMembershipFromGuidsWriteOnceAsync(
                Path, new[] { MemberA }, new AzureADGroup { ObjectId = DestinationId },
                RunId, SyncJobId, false, false, "[]", SourceGroupId);

            Assert.IsFalse(result.AlreadyExisted);
            Assert.AreEqual(1, commits);
            Assert.AreEqual(Azure.ETag.All, commitOptions.Conditions?.IfNoneMatch);
        }

        [TestMethod]
        public async Task SourceGroupOverload_IsReadableByProductionReaderWithSourceGroupOnEveryMember()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadGroupMembershipFromGuidsAsync(
                Path, new[] { MemberA, MemberB }, new AzureADGroup { ObjectId = DestinationId },
                RunId, SyncJobId, false, false, "[]", SourceGroupId);

            GroupMembership envelope = null;
            var members = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(
                new MemoryStream(inMemory.GetBytes(Path)), details => envelope = details))
            {
                members.Add(member);
            }

            Assert.AreEqual(DestinationId, envelope.Destination.ObjectId);
            CollectionAssert.AreEqual(new[] { MemberB, MemberA }, members.Select(m => m.ObjectId).ToArray());
            Assert.IsTrue(members.All(m => m.SourceGroup == SourceGroupId));
            var deserialized = JsonSerializer.Deserialize<GroupMembership>(inMemory.GetBytes(Path));
            Assert.IsTrue(deserialized.SourceMembers.All(m => m.SourceGroup == SourceGroupId));
        }

        [TestMethod]
        public async Task SourceGroupOverload_EmptySourceGroup_IsRejected()
        {
            var azure = new AzureBlobContainerFake();

            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                azure.CreateRepository().UploadGroupMembershipFromGuidsAsync(
                    Path, new[] { MemberA }, new AzureADGroup { ObjectId = DestinationId },
                    RunId, SyncJobId, false, false, "[]", Guid.Empty));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                new InMemoryBlobStorageRepository().UploadGroupMembershipFromGuidsWriteOnceAsync(
                    Path, new[] { MemberA }, new AzureADGroup { ObjectId = DestinationId },
                    RunId, SyncJobId, false, false, "[]", Guid.Empty));
        }

        [TestMethod]
        public async Task SharedSerializer_WithoutSourceGroup_KeepsIdentifierOnlyBytes()
        {
            // I16: the both-types saved-delta output keeps today's identifier-only shape.
            var azure = new AzureBlobContainerFake();
            var memberIds = new[] { MemberA, MemberB };
            var destination = new AzureADGroup { ObjectId = DestinationId };
            await azure.CreateRepository().UploadGroupMembershipFromGuidsAsync(
                Path, memberIds, destination, RunId, SyncJobId, true, false, "[\"q\"]");

            var json = Encoding.UTF8.GetString(azure.GetBytes(Path));

            Assert.IsFalse(json.Contains("SourceGroup", StringComparison.Ordinal));
            Assert.AreEqual(ExpectedSourceGroupJson.Replace(",\"SourceGroup\":\"44444444-4444-4444-4444-444444444444\"", string.Empty), json);
        }
    }
}
