// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.ServiceBus;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    /// <summary>
    /// Differential tests: the same inputs go to the production <see cref="BlobStorageRepository"/>
    /// (over mocked Azure SDK clients) and to <see cref="InMemoryBlobStorageRepository"/>, and the stored
    /// bytes or parsed results must agree (failure-model invariant I13).
    /// </summary>
    [TestClass]
    public sealed class InMemoryBlobStorageRepositoryFidelityTests
    {
        private const string Path = "group/part.json";

        private static readonly Guid DestinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid SyncJobId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid MemberA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid MemberB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

        public static IEnumerable<object[]> IdentifierOnlyCases()
        {
            yield return new object[] { "two members with query", new[] { MemberA, MemberB }, "[{\"type\":\"GroupMembership\"}]", false, false };
            yield return new object[] { "null query", new[] { MemberB, MemberA }, null, true, false };
            yield return new object[] { "dry run, no members", Array.Empty<Guid>(), "[]", false, true };
            yield return new object[] { "many unsorted members", Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray(), "q", true, true };
        }

        [TestMethod]
        [DynamicData(nameof(IdentifierOnlyCases), DynamicDataSourceType.Method)]
        public async Task UploadGroupMembershipFromGuids_ProductionAndDouble_StoreIdenticalBytes(
            string description,
            Guid[] memberIds,
            string query,
            bool exclusionary,
            bool dryRun)
        {
            var azure = new AzureBlobContainerFake();
            var production = azure.CreateRepository();
            var inMemory = new InMemoryBlobStorageRepository();
            var destination = new AzureADGroup { ObjectId = DestinationId };

            await production.UploadGroupMembershipFromGuidsAsync(
                Path, memberIds, destination, RunId, SyncJobId, exclusionary, dryRun, query);
            await inMemory.UploadGroupMembershipFromGuidsAsync(
                Path, memberIds, destination, RunId, SyncJobId, exclusionary, dryRun, query);

            CollectionAssert.AreEqual(azure.GetBytes(Path), inMemory.GetBytes(Path), description);
        }

        [TestMethod]
        public async Task UploadGroupMembershipFromGuids_Production_WritesPinnedIdentifierOnlyBytes()
        {
            // Pins production output so extracting the shared serializer cannot change a byte.
            var azure = new AzureBlobContainerFake();

            await azure.CreateRepository().UploadGroupMembershipFromGuidsAsync(
                Path,
                new[] { MemberA, MemberB },
                new AzureADGroup { ObjectId = DestinationId },
                RunId,
                SyncJobId,
                exclusionary: true,
                membershipObtainerDryRunEnabled: false,
                query: "[\"q\"]");

            Assert.AreEqual(ExpectedIdentifierOnlyJson, Encoding.UTF8.GetString(azure.GetBytes(Path)));
        }

        [TestMethod]
        public async Task UploadGroupMembershipFromGuids_SharedSerializer_MatchesRepositoryBytes()
        {
            var azure = new AzureBlobContainerFake();
            var memberIds = new[] { MemberA, MemberB };
            var destination = new AzureADGroup { ObjectId = DestinationId };
            await azure.CreateRepository().UploadGroupMembershipFromGuidsAsync(
                Path, memberIds, destination, RunId, SyncJobId, true, false, "[\"q\"]");

            using var direct = new MemoryStream();
            await BlobStorageRepository.WriteGroupMembershipFromGuidsAsync(
                direct, memberIds, destination, RunId, SyncJobId, true, false, "[\"q\"]", CancellationToken.None);

            CollectionAssert.AreEqual(azure.GetBytes(Path), direct.ToArray());
            Assert.AreEqual(ExpectedIdentifierOnlyJson, Encoding.UTF8.GetString(direct.ToArray()));
        }

        [TestMethod]
        public async Task UploadGroupMembershipFromGuids_Double_IsReadableByProductionReader()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadGroupMembershipFromGuidsAsync(
                Path, new[] { MemberB, MemberA }, new AzureADGroup { ObjectId = DestinationId },
                RunId, SyncJobId, false, false, "[]");

            GroupMembership envelope = null;
            var members = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(
                new MemoryStream(inMemory.GetBytes(Path)), details => envelope = details))
            {
                members.Add(member);
            }

            Assert.AreEqual(DestinationId, envelope.Destination.ObjectId);
            // Canonical order sorts MemberB (0000...) before MemberA (aaaa...).
            CollectionAssert.AreEqual(new[] { MemberB, MemberA }, members.Select(member => member.ObjectId).ToArray());
            Assert.IsTrue(members.All(member => member.SourceGroup == Guid.Empty && member.Mail == null));
        }

        [TestMethod]
        public async Task WriteMembership_ProductionAndDouble_StoreIdenticalCompressedBytesAndMetadata()
        {
            var azure = new AzureBlobContainerFake();
            var inMemory = new InMemoryBlobStorageRepository();
            var metadata = new Dictionary<string, string> { ["status"] = "complete" };
            var members = new[]
            {
                new AzureADUser { ObjectId = MemberA, SourceGroup = DestinationId, Mail = "a@contoso.com" },
                new AzureADUser { ObjectId = MemberB }
            };

            await azure.CreateRepository().WriteMembershipAsync(Path, Envelope(), Stream(members), metadata);
            await inMemory.WriteMembershipAsync(Path, Envelope(), Stream(members), metadata);

            var stored = inMemory.GetBytes(Path);
            CollectionAssert.AreEqual(azure.GetBytes(Path), stored);
            Assert.AreNotEqual((byte)'{', stored[0], "WriteMembershipAsync must not store plain JSON.");
            Assert.IsTrue(Convert.TryFromBase64String(Encoding.ASCII.GetString(stored), new byte[stored.Length], out _),
                "WriteMembershipAsync stores Base64-encoded Brotli, as production does.");
            var storedMetadata = await inMemory.GetBlobMetadataAsync(Path);
            CollectionAssert.AreEquivalent(
                azure.GetMetadata(Path).ToList(),
                storedMetadata.Metadata.ToList());
        }

        [TestMethod]
        public async Task WriteMembership_EnumerationFails_DoesNotPublishLikeProduction()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadFileAsync(Path, "previous");

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                inMemory.WriteMembershipAsync(Path, Envelope(), FailingMembers()));

            Assert.AreEqual("previous", inMemory.GetContent(Path));
        }

        public static IEnumerable<object[]> MembershipEncodings()
        {
            yield return new object[] { "compressed" };
            yield return new object[] { "plain" };
            yield return new object[] { "plain with BOM" };
            yield return new object[] { "members before envelope" };
        }

        [TestMethod]
        [DynamicData(nameof(MembershipEncodings), DynamicDataSourceType.Method)]
        public async Task StreamMembership_ProductionAndDouble_ParseTheSameBytesIdentically(string encoding)
        {
            var bytes = await EncodeMembershipAsync(encoding);
            var azure = new AzureBlobContainerFake();
            azure.Put(Path, bytes);
            var inMemory = new InMemoryBlobStorageRepository();
            inMemory.PutBytes(Path, bytes);

            var production = await ReadAllAsync(azure.CreateRepository(), Path);
            var actual = await ReadAllAsync(inMemory, Path);

            Assert.AreEqual(production.Envelope, actual.Envelope, encoding);
            CollectionAssert.AreEqual(production.Members, actual.Members, encoding);
            Assert.AreEqual(production.EnvelopeBeforeFirstMember, actual.EnvelopeBeforeFirstMember, encoding);
        }

        [TestMethod]
        public async Task StreamMembership_MissingSourceMembers_FailsLikeProduction()
        {
            var bytes = Encoding.UTF8.GetBytes("{\"RunId\":\"" + RunId + "\"}");
            var azure = new AzureBlobContainerFake();
            azure.Put(Path, bytes);
            var inMemory = new InMemoryBlobStorageRepository();
            inMemory.PutBytes(Path, bytes);

            await Assert.ThrowsExceptionAsync<JsonException>(() => ReadAllAsync(azure.CreateRepository(), Path));
            await Assert.ThrowsExceptionAsync<JsonException>(() => ReadAllAsync(inMemory, Path));
        }

        [TestMethod]
        public async Task StreamMembership_MissingBlob_ThrowsFileNotFoundLikeProduction()
        {
            var azure = new AzureBlobContainerFake();

            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => ReadAllAsync(azure.CreateRepository(), Path));
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => ReadAllAsync(new InMemoryBlobStorageRepository(), Path));
        }

        [TestMethod]
        public async Task StreamMembership_EnvelopeCallback_ReceivesProductionEnvelope()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.WriteMembershipAsync(Path, Envelope(), Stream(new[] { new AzureADUser { ObjectId = MemberA } }));

            GroupMembership envelope = null;
            await foreach (var _ in inMemory.StreamMembershipAsync(Path, details => envelope = details))
            {
            }

            Assert.IsNotNull(envelope.SourceMembers, "Production reports an empty, non-null SourceMembers list.");
            Assert.AreEqual(0, envelope.SourceMembers.Count);
            Assert.AreEqual("[\"q\"]", envelope.Query);
        }

        [TestMethod]
        public async Task ExtractSourceMembers_ProductionAndDouble_AgreeOnRawStringMembers()
        {
            var bytes = Encoding.UTF8.GetBytes(
                "{\"SourceMembers\":[\"" + MemberA + "\",{\"ObjectId\":\"" + MemberB + "\"},\"" + MemberA + "\"]}");
            var azure = new AzureBlobContainerFake();
            azure.Put(Path, bytes);
            var inMemory = new InMemoryBlobStorageRepository();
            inMemory.PutBytes(Path, bytes);

            var production = await azure.CreateRepository().ExtractGroupMembershipSourceMembersAsync(Path);
            var actual = await inMemory.ExtractGroupMembershipSourceMembersAsync(Path);

            CollectionAssert.AreEquivalent(production.ToList(), actual.ToList());
        }

        [TestMethod]
        public async Task StreamMembershipToCache_ProductionAndDouble_StoreIdenticalBytesAndMetadata()
        {
            var azure = new AzureBlobContainerFake();
            var production = azure.CreateRepository();
            var inMemory = new InMemoryBlobStorageRepository();
            var ids = new[] { MemberB, MemberA };
            var destination = new AzureADGroup { ObjectId = DestinationId };
            await production.UploadGroupMembershipFromGuidsAsync(Path, ids, destination, RunId, SyncJobId, false, false, "[]");
            await inMemory.UploadGroupMembershipFromGuidsAsync(Path, ids, destination, RunId, SyncJobId, false, false, "[]");
            var metadata = new Dictionary<string, string> { ["RunId"] = RunId.ToString() };

            var productionCount = await production.StreamMembershipToCacheAsync(Path, "cache/group", metadata);
            var actualCount = await inMemory.StreamMembershipToCacheAsync(Path, "cache/group", metadata);

            Assert.AreEqual(productionCount, actualCount);
            CollectionAssert.AreEqual(azure.GetBytes("cache/group"), inMemory.GetBytes("cache/group"));
            CollectionAssert.AreEquivalent(
                azure.GetMetadata("cache/group").ToList(),
                (await inMemory.GetBlobMetadataAsync("cache/group")).Metadata.ToList());
        }

        public static IEnumerable<object[]> CacheInputs()
        {
            yield return new object[] { "empty", Array.Empty<Guid>() };
            yield return new object[] { "unsorted with duplicate", new[] { MemberB, MemberA, MemberB } };
            yield return new object[] { "spans blocks", Enumerable.Range(0, 10_001).Select(_ => Guid.NewGuid()).ToArray() };
        }

        [TestMethod]
        [DynamicData(nameof(CacheInputs), DynamicDataSourceType.Method)]
        public async Task UploadCacheFromGuids_ProductionAndDouble_StoreIdenticalBytes(string description, Guid[] ids)
        {
            var azure = new AzureBlobContainerFake();
            var inMemory = new InMemoryBlobStorageRepository();
            var metadata = new Dictionary<string, string> { ["NumberOfUsers"] = ids.Length.ToString() };

            await azure.CreateRepository().UploadCacheFromGuidsAsync("cache/group", ids, metadata);
            await inMemory.UploadCacheFromGuidsAsync("cache/group", ids, metadata);

            CollectionAssert.AreEqual(azure.GetBytes("cache/group"), inMemory.GetBytes("cache/group"), description);
            CollectionAssert.AreEquivalent(
                azure.GetMetadata("cache/group").ToList(),
                (await inMemory.GetBlobMetadataAsync("cache/group")).Metadata.ToList(),
                description);
        }

        [TestMethod]
        public async Task CacheRoundTrip_ProductionAndDouble_ReadTheSameValues()
        {
            var azure = new AzureBlobContainerFake();
            var production = azure.CreateRepository();
            var inMemory = new InMemoryBlobStorageRepository();
            var ids = new[] { MemberB, MemberA };
            await production.UploadCacheFromGuidsAsync("cache/group", ids);
            await inMemory.UploadCacheFromGuidsAsync("cache/group", ids);

            var productionValues = await production.ReadValuesFromBlobAsync("cache/group", Guid.Parse);
            var actualValues = await inMemory.ReadValuesFromBlobAsync("cache/group", Guid.Parse);

            CollectionAssert.AreEquivalent(productionValues.ToList(), actualValues.ToList());
        }

        [TestMethod]
        public async Task UploadFileAndStream_ProductionAndDouble_StoreIdenticalBytes()
        {
            var azure = new AzureBlobContainerFake();
            var production = azure.CreateRepository();
            var inMemory = new InMemoryBlobStorageRepository();
            var users = new List<AzureADUser> { new() { ObjectId = MemberA, Mail = "ä@contoso.com" } };

            await production.UploadFileAsync("plain.txt", "héllo");
            await inMemory.UploadFileAsync("plain.txt", "héllo");
            await production.UploadFileStreamAsync("users.json", users);
            await inMemory.UploadFileStreamAsync("users.json", users);

            CollectionAssert.AreEqual(azure.GetBytes("plain.txt"), inMemory.GetBytes("plain.txt"));
            CollectionAssert.AreEqual(azure.GetBytes("users.json"), inMemory.GetBytes("users.json"));
        }

        [TestMethod]
        public async Task StreamUsersFromBlobs_ProductionAndDouble_DeduplicateAcrossBlobsInNameOrder()
        {
            var azure = new AzureBlobContainerFake();
            var inMemory = new InMemoryBlobStorageRepository();
            var later = JsonSerializer.Serialize(new[] { new AzureADUser { ObjectId = MemberA, Mail = "later" } });
            var earlier = JsonSerializer.Serialize(new[]
            {
                new AzureADUser { ObjectId = MemberA, Mail = "earlier" },
                new AzureADUser { ObjectId = MemberB }
            });
            // Written out of name order so name ordering and write ordering disagree.
            azure.Put("users/2", Encoding.UTF8.GetBytes(later));
            azure.Put("users/1", Encoding.UTF8.GetBytes(earlier));
            await inMemory.UploadFileAsync("users/2", later);
            await inMemory.UploadFileAsync("users/1", earlier);

            var production = new List<AzureADUser>();
            await foreach (var user in azure.CreateRepository().StreamUsersFromBlobsAsync("users/"))
            {
                production.Add(user);
            }

            var actual = new List<AzureADUser>();
            await foreach (var user in inMemory.StreamUsersFromBlobsAsync("users/"))
            {
                actual.Add(user);
            }

            CollectionAssert.AreEqual(
                production.Select(user => (user.ObjectId, user.Mail)).ToList(),
                actual.Select(user => (user.ObjectId, user.Mail)).ToList());
            var productionList = await azure.CreateRepository().ReadBlobsAsync("users/");
            var actualList = await inMemory.ReadBlobsAsync("users/");
            CollectionAssert.AreEqual(
                productionList.Select(user => (user.ObjectId, user.Mail)).ToList(),
                actualList.Select(user => (user.ObjectId, user.Mail)).ToList());
        }

        [TestMethod]
        public async Task UploadFileIfAbsent_ProductionAndDouble_AgreeOnAbsentAndPresentBlobs()
        {
            var azure = new AzureBlobContainerFake();
            var production = azure.CreateRepository();
            var inMemory = new InMemoryBlobStorageRepository();

            Assert.IsTrue(await production.UploadFileIfAbsentAsync("once.json", "first"));
            Assert.IsTrue(await inMemory.UploadFileIfAbsentAsync("once.json", "first"));
            Assert.IsFalse(await production.UploadFileIfAbsentAsync("once.json", "second"));
            Assert.IsFalse(await inMemory.UploadFileIfAbsentAsync("once.json", "second"));

            CollectionAssert.AreEqual(azure.GetBytes("once.json"), inMemory.GetBytes("once.json"));
            Assert.AreEqual("first", inMemory.GetContent("once.json"));
        }

        [TestMethod]
        [DataRow(409)]
        [DataRow(412)]
        public async Task UploadFileIfAbsent_InjectedPreconditionFailure_ReturnsFalseWithoutWriting(int status)
        {
            var inMemory = new InMemoryBlobStorageRepository();
            inMemory.InjectPreconditionFailure("once.json", status);

            Assert.IsFalse(await inMemory.UploadFileIfAbsentAsync("once.json", "first"));
            Assert.IsNull(inMemory.GetContent("once.json"));
            Assert.IsTrue(await inMemory.UploadFileIfAbsentAsync("once.json", "retry"), "The injection is consumed once.");
            Assert.AreEqual("retry", inMemory.GetContent("once.json"));
        }

        [TestMethod]
        public async Task ConditionalWrite_InjectedPreconditionFailure_ThrowsRequestFailedException()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadFileAsync("state.json", "v1");
            var read = await inMemory.DownloadWithETagAsync("state.json");
            inMemory.InjectPreconditionFailure("state.json");

            var failure = await Assert.ThrowsExceptionAsync<RequestFailedException>(() =>
                inMemory.UploadFileConditionalAsync(
                    "state.json", "v2", new BlobRequestConditions { IfMatch = read.ETag }));

            Assert.AreEqual(412, failure.Status);
            Assert.AreEqual("ConditionNotMet", failure.ErrorCode);
            Assert.AreEqual("v1", inMemory.GetContent("state.json"));
        }

        [TestMethod]
        public async Task ConditionalWrite_IfNoneMatchAllOnExistingBlob_ThrowsBlobAlreadyExists()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadFileAsync("once.json", "first");

            var failure = await Assert.ThrowsExceptionAsync<RequestFailedException>(() =>
                inMemory.UploadFileConditionalAsync(
                    "once.json", "second", new BlobRequestConditions { IfNoneMatch = ETag.All }));

            Assert.AreEqual(409, failure.Status);
            Assert.AreEqual("BlobAlreadyExists", failure.ErrorCode);
            Assert.AreEqual("first", inMemory.GetContent("once.json"));
        }

        [TestMethod]
        public async Task ReadThenConditionalWrite_MatchingETag_SucceedsAndRotatesETag()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadFileAsync("state.json", "v1");

            var read = await inMemory.DownloadWithETagAsync("state.json");
            var newETag = await inMemory.UploadFileConditionalAsync(
                "state.json", "v2", new BlobRequestConditions { IfMatch = read.ETag });

            Assert.AreEqual(BlobStatus.Found, read.Result.BlobStatus);
            Assert.AreEqual("v1", read.Result.Content);
            Assert.IsNotNull(read.ETag);
            Assert.AreNotEqual(read.ETag, newETag);
            Assert.AreEqual(newETag, inMemory.GetETag("state.json"));
            Assert.AreEqual("v2", inMemory.GetContent("state.json"));
        }

        [TestMethod]
        public async Task ReadThenConditionalWrite_StaleETag_ThrowsConditionNotMet()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadFileAsync("state.json", "v1");
            var read = await inMemory.DownloadWithETagAsync("state.json");
            await inMemory.UploadFileAsync("state.json", "concurrent");

            var failure = await Assert.ThrowsExceptionAsync<RequestFailedException>(() =>
                inMemory.UploadFileConditionalAsync(
                    "state.json", "v2", new BlobRequestConditions { IfMatch = read.ETag }));

            Assert.AreEqual(412, failure.Status);
            Assert.AreEqual("ConditionNotMet", failure.ErrorCode);
            Assert.AreEqual("concurrent", inMemory.GetContent("state.json"));
        }

        [TestMethod]
        public async Task ConditionalWrite_IfMatchOnMissingBlob_ThrowsConditionNotMet()
        {
            var inMemory = new InMemoryBlobStorageRepository();

            var failure = await Assert.ThrowsExceptionAsync<RequestFailedException>(() =>
                inMemory.UploadFileConditionalAsync(
                    "state.json", "v1", new BlobRequestConditions { IfMatch = new ETag("\"0x1\"") }));

            Assert.AreEqual(412, failure.Status);
            Assert.IsNull(inMemory.GetContent("state.json"));
        }

        [TestMethod]
        public async Task DownloadWithETag_MissingBlob_ReturnsNotFoundWithoutETag()
        {
            var read = await new InMemoryBlobStorageRepository().DownloadWithETagAsync("missing.json");

            Assert.AreEqual(BlobStatus.NotFound, read.Result.BlobStatus);
            Assert.IsNull(read.ETag);
        }

        [TestMethod]
        public async Task BeforeConditionalWrite_RacingWriter_CausesPreconditionFailure()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            await inMemory.UploadFileAsync("state.json", "v1");
            var read = await inMemory.DownloadWithETagAsync("state.json");
            inMemory.BeforeConditionalWriteAsync = async path =>
            {
                inMemory.BeforeConditionalWriteAsync = null;
                await inMemory.UploadFileAsync(path, "racer");
            };

            var failure = await Assert.ThrowsExceptionAsync<RequestFailedException>(() =>
                inMemory.UploadFileConditionalAsync(
                    "state.json", "v2", new BlobRequestConditions { IfMatch = read.ETag }));

            Assert.AreEqual(412, failure.Status);
            Assert.AreEqual("racer", inMemory.GetContent("state.json"));
        }

        [TestMethod]
        public async Task InjectLatency_DelaysOnlyTheNamedPathAndHonoursCancellation()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            inMemory.InjectLatency("slow.json", TimeSpan.FromMilliseconds(200));

            var fast = Stopwatch.StartNew();
            await inMemory.UploadFileAsync("fast.json", "x");
            fast.Stop();
            var slow = Stopwatch.StartNew();
            await inMemory.UploadFileAsync("slow.json", "x");
            await inMemory.DownloadFileAsync("slow.json");
            slow.Stop();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

            Assert.IsTrue(slow.ElapsedMilliseconds >= 380, $"slow path took {slow.ElapsedMilliseconds} ms");
            Assert.IsTrue(fast.ElapsedMilliseconds < 150, $"fast path took {fast.ElapsedMilliseconds} ms");
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
                inMemory.UploadFileIfAbsentAsync("slow.json", "y", cancellation.Token));
        }

        [TestMethod]
        public async Task ExistingHooks_StillApplyToConditionalWrites()
        {
            var inMemory = new InMemoryBlobStorageRepository();
            var beforeWrites = new List<string>();
            inMemory.BeforeWriteAsync = path =>
            {
                beforeWrites.Add(path);
                return Task.CompletedTask;
            };
            inMemory.WriteFailurePaths.Add("blocked.json");

            await inMemory.UploadFileConditionalAsync("/state.json", "abc", new BlobRequestConditions { IfNoneMatch = ETag.All });
            await Assert.ThrowsExceptionAsync<IOException>(() =>
                inMemory.UploadFileConditionalAsync("blocked.json", "x", new BlobRequestConditions()));

            CollectionAssert.AreEqual(new[] { "state.json", "blocked.json" }, beforeWrites);
            Assert.AreEqual(3, inMemory.WriteHistory.Single(write => write.Path == "state.json").ByteCount);
            CollectionAssert.Contains(inMemory.Operations, "write:state.json");
        }

        private const string ExpectedIdentifierOnlyJson =
            "{\"Destination\":{\"ObjectId\":\"11111111-1111-1111-1111-111111111111\"," +
            "\"Type\":null,\"Name\":null,\"Email\":null,\"Visibility\":null}," +
            "\"RunId\":\"22222222-2222-2222-2222-222222222222\"," +
            "\"SyncJobId\":\"33333333-3333-3333-3333-333333333333\"," +
            "\"Exclusionary\":true," +
            "\"MembershipObtainerDryRunEnabled\":false," +
            "\"Query\":\"[\\u0022q\\u0022]\"," +
            "\"SourceMembers\":[{\"ObjectId\":\"00000000-0000-0000-0000-0000000000bb\"}," +
            "{\"ObjectId\":\"aaaaaaaa-0000-0000-0000-000000000001\"}]}";

        private static GroupMembership Envelope() => new()
        {
            Destination = new AzureADGroup { ObjectId = DestinationId },
            RunId = RunId,
            SyncJobId = SyncJobId,
            Exclusionary = true,
            Query = "[\"q\"]",
            SourceMembers = new List<AzureADUser>()
        };

        private static async Task<byte[]> EncodeMembershipAsync(string encoding)
        {
            var members = new[]
            {
                new AzureADUser { ObjectId = MemberA, SourceGroup = DestinationId },
                new AzureADUser { ObjectId = MemberB }
            };
            if (encoding == "compressed")
            {
                using var compressed = new MemoryStream();
                await MembershipStream.WriteAsync(compressed, Envelope(), Stream(members));
                return compressed.ToArray();
            }

            var envelope = Envelope();
            envelope.SourceMembers = members.ToList();
            var json = JsonSerializer.Serialize(envelope);
            if (encoding == "members before envelope")
            {
                json = "{\"SourceMembers\":" + JsonSerializer.Serialize(members) +
                       ",\"RunId\":\"" + RunId + "\",\"Query\":\"[\\\"q\\\"]\"}";
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            return encoding == "plain with BOM"
                ? Encoding.UTF8.GetPreamble().Concat(bytes).ToArray()
                : bytes;
        }

        private static async Task<(string Envelope, List<string> Members, bool EnvelopeBeforeFirstMember)> ReadAllAsync(
            Repositories.Contracts.IBlobStorageRepository repository,
            string path)
        {
            GroupMembership envelope = null;
            bool? envelopeBeforeFirstMember = null;
            var members = new List<string>();
            await foreach (var member in repository.StreamMembershipAsync(path, details => envelope = details))
            {
                envelopeBeforeFirstMember ??= envelope != null;
                members.Add(JsonSerializer.Serialize(member));
            }

            return (JsonSerializer.Serialize(envelope), members, envelopeBeforeFirstMember ?? false);
        }

        private static async IAsyncEnumerable<AzureADUser> Stream(
            IEnumerable<AzureADUser> members,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return member;
            }
        }

        private static async IAsyncEnumerable<AzureADUser> FailingMembers()
        {
            yield return new AzureADUser { ObjectId = MemberA };
            await Task.Yield();
            throw new InvalidOperationException("enumeration failed");
        }
    }
}
