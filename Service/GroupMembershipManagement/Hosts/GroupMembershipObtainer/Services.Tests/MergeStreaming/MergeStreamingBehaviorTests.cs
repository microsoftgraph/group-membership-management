// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Moq;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services.MergeStreaming
{
    /// <summary>
    /// Behaviour of the streaming page-upload merge beyond the golden bytes: duplicate tie-breaking, the fallback for
    /// unsorted uploads (including one detected only after blocks were staged), write-once rejection, and the
    /// element reader's handling of chunk boundaries and malformed uploads.
    /// </summary>
    [TestClass]
    public class MergeStreamingBehaviorTests
    {
        private static readonly JsonSerializerOptions _readerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        [TestMethod]
        public void GoldenVariants_ExerciseBothPaths()
        {
            foreach (var name in MergeGoldenCases.Names)
            {
                Assert.IsTrue(MergeStreamingGoldenTests.AllUploadsSorted(MergeGoldenCases.Build(name, sortedPages: true)), $"{name}: sorted variant");
            }

            foreach (var name in new[] { "SingleUpload", "DuplicatesAcrossUploads", "Large25k", "Unicode", "AllNullOptional" })
            {
                Assert.IsFalse(MergeStreamingGoldenTests.AllUploadsSorted(MergeGoldenCases.Build(name, sortedPages: false)), $"{name}: unsorted variant");
            }
        }

        public static IEnumerable<object[]> Targets() =>
            from target in new[] { "production", "double" }
            from writeOnce in new[] { false, true }
            select new object[] { target, writeOnce };

        /// <summary>
        /// The same identifier in several uploads, and twice within one: the record kept is the first occurrence in
        /// listing order, then position within the upload, as the materializing merge kept, even though the
        /// first-listed upload holds it as its last element and a later upload holds it as its first.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Targets), DynamicDataSourceType.Method)]
        public async Task DuplicateIdentifiers_KeepFirstOccurrenceInListingOrder(string target, bool writeOnce)
        {
            var x = Guid.Parse("80000000-0000-0000-0000-000000000000");
            var low = Enumerable.Range(1, 5).Select(i => Guid.Parse($"0000000{i}-0000-0000-0000-000000000000")).ToList();
            var high = Guid.Parse("f0000000-0000-0000-0000-000000000000");

            var uploads = new List<MergeUpload>
            {
                // Listed first: X is its last element, and it appears twice (the first record must win).
                Upload("a.json", low.Select(id => User(id, "a")).Append(User(x, "a-first")).Append(User(x, "a-second"))),
                // X is its first element: it reaches the merge before page a's X, but must lose.
                Upload("b.json", new[] { User(x, "b"), User(high, "b-high") }),
                Upload("c.json", new[] { User(low[0], "c"), User(x, "c"), User(x, "c-2") }),
            };
            var mergeCase = Case("TieBreak", uploads);

            var (bytes, count, logger) = await RunAsync(target, writeOnce, mergeCase);

            var members = JsonSerializer.Deserialize<Models.ServiceBus.GroupMembership>(bytes).SourceMembers;
            Assert.AreEqual(7, count);
            CollectionAssert.AreEqual(low.Append(x).Append(high).ToList(), members.Select(m => m.ObjectId).ToList());
            Assert.AreEqual("a-first", members.Single(m => m.ObjectId == x).DisplayName);
            Assert.AreEqual("a", members.Single(m => m.ObjectId == low[0]).DisplayName);
            AssertMatchesOracle(mergeCase, bytes);
            Assert.AreEqual(0, logger.Entries.Count(e => e.Level == LogLevel.Warning), "Sorted uploads must stream.");
        }

        /// <summary>
        /// An older release's upload that is out of order only at its very end is detected after the streaming
        /// attempt has staged blocks (the output is larger than one 4 MiB block). Nothing from that attempt is
        /// published; the materializing merge writes the correct bytes and a warning is logged.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(Targets), DynamicDataSourceType.Method)]
        public async Task UnsortedUploadDetectedLate_FallsBackAndWritesMaterializedBytes(string target, bool writeOnce)
        {
            var rng = new Random(606);
            var ids = Enumerable.Range(0, 24_000).Select(_ => NewId(rng)).OrderBy(id => id, CanonicalObjectIdComparer.Instance).ToList();
            var padding = new string('p', 200);
            var uploads = new List<MergeUpload>();
            for (var p = 0; p < 6; p++)
            {
                // Contiguous identifier ranges, so the last upload is consumed last.
                var page = ids.Skip(p * 4_000).Take(4_000).Select(id => User(id, padding)).ToList();
                if (p == 5)
                {
                    page.Add(User(ids[10], "late duplicate, out of order"));
                    page.Add(User(Guid.Empty, "late new member, out of order"));
                }

                uploads.Add(new MergeUpload($"{p:D8}.json", JsonSerializer.SerializeToUtf8Bytes(page, _readerOptions)));
            }

            var mergeCase = Case("LateUnsorted", uploads);
            var (bytes, count, logger) = await RunAsync(target, writeOnce, mergeCase);

            Assert.IsTrue(bytes.Length > 4 * 1024 * 1024, "The case must be larger than one staged block.");
            Assert.AreEqual(24_001, count);
            AssertMatchesOracle(mergeCase, bytes);
            var warning = logger.Entries.Single(e => e.Level == LogLevel.Warning);
            StringAssert.Contains(warning.Message, "00000005.json");
        }

        [TestMethod]
        public async Task WriteOnce_StreamingPath_ExistingDestination_ReportsExistingCountAndKeepsBlob()
        {
            var fake = new AzureBlobContainerFake();
            var existing = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Models.ServiceBus.GroupMembership
            {
                SourceMembers = new List<AzureADUser> { new() { ObjectId = Guid.NewGuid() }, new() { ObjectId = Guid.NewGuid() } }
            }));
            fake.Put(MergeCase.OutputPath, existing);
            var mergeCase = MergeGoldenCases.Build("DuplicatesAcrossUploads", sortedPages: true);
            foreach (var upload in mergeCase.Uploads)
            {
                fake.Put(MergeCase.Prefix + upload.Name, upload.Bytes);
            }

            // The fake commits unconditionally, so model the service: the conditional commit is rejected.
            var repository = new BlobStorageRepository(
                fake.Container.Object,
                path => RejectingBlockBlobClient(),
                new RecordingLogger<MergeStreamingBehaviorTests>());
            fake.Container.Setup(c => c.GetBlobClient(MergeCase.OutputPath)).Returns(ExistingBlobClient(existing));

            var result = await repository.MergeAndStreamUserBlobsWriteOnceAsync(
                MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]");

            Assert.IsTrue(result.AlreadyExisted);
            Assert.AreEqual(2, result.Count);
        }

        /// <summary>
        /// Slice I: the legacy overwriting merge (no file timestamp) and a concurrent duplicate of it. The duplicate
        /// commits first, which discards this merge's uncommitted blocks, so this merge's unconditional commit fails
        /// with <c>InvalidBlockList</c>. The blob exists, so the merge is done: it returns the existing blob's member
        /// count, as the write-once merge does, and the duplicate's bytes stay published.
        /// </summary>
        [TestMethod]
        public async Task Overwrite_ConcurrentDuplicateCommitsFirst_InvalidBlockList_ReturnsExistingCount()
        {
            var fake = new AzureBlobContainerFake();
            var mergeCase = MergeGoldenCases.Build("DuplicatesAcrossUploads", sortedPages: true);
            foreach (var upload in mergeCase.Uploads)
            {
                fake.Put(MergeCase.Prefix + upload.Name, upload.Bytes);
            }

            var repository = fake.CreateRepository(new RecordingLogger<MergeStreamingBehaviorTests>());
            var duplicateCount = -1;
            var duplicateRan = false;
            fake.BeforeCommitBlockListAsync = async path =>
            {
                if (duplicateRan || path != MergeCase.OutputPath)
                {
                    return;
                }

                duplicateRan = true;
                duplicateCount = await repository.MergeAndStreamUserBlobsAsync(
                    MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]");
            };

            var count = await repository.MergeAndStreamUserBlobsAsync(
                MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]");

            Assert.IsTrue(duplicateRan, "The concurrent duplicate did not run.");
            var published = fake.GetBytes(MergeCase.OutputPath);
            Assert.IsNotNull(published);
            AssertMatchesOracle(mergeCase, published);
            var members = JsonSerializer.Deserialize<Models.ServiceBus.GroupMembership>(published).SourceMembers;
            Assert.AreEqual(members.Count, duplicateCount);
            Assert.AreEqual(members.Count, count, "The merge did not report the existing blob's member count.");
        }

        /// <summary>
        /// Slice I: an <c>InvalidBlockList</c> commit failure of the legacy overwriting merge when the blob does not
        /// exist is a real failure and is rethrown.
        /// </summary>
        [TestMethod]
        public async Task Overwrite_InvalidBlockList_BlobMissing_Rethrows()
        {
            var fake = new AzureBlobContainerFake();
            var mergeCase = MergeGoldenCases.Build("DuplicatesAcrossUploads", sortedPages: true);
            foreach (var upload in mergeCase.Uploads)
            {
                fake.Put(MergeCase.Prefix + upload.Name, upload.Bytes);
            }

            var repository = fake.CreateRepository(new RecordingLogger<MergeStreamingBehaviorTests>());
            fake.BeforeCommitBlockListAsync = _ => throw new Azure.RequestFailedException(400, "The specified block list is invalid.", "InvalidBlockList", null);

            var failure = await Record(() => repository.MergeAndStreamUserBlobsAsync(
                MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]"));

            Assert.IsInstanceOfType(failure, typeof(Azure.RequestFailedException));
            Assert.AreEqual("InvalidBlockList", ((Azure.RequestFailedException)failure).ErrorCode);
            Assert.IsNull(fake.GetBytes(MergeCase.OutputPath));
        }

        [TestMethod]
        public async Task Reader_HandlesOneByteReadsBomWhitespaceNullsAndLargeElements()
        {
            var big = new string('x', 200_000);
            var ids = Enumerable.Range(1, 4).Select(i => Guid.Parse($"0000000{i}-0000-0000-0000-000000000000")).ToArray();
            var json = $"  [ null , {{\"ObjectId\":\"{ids[0]}\",\"DisplayName\":\"{big}\"}},\r\n\t{{\"objectId\":\"{ids[1]}\",\"Properties\":{{\"a\":[1,2.50,{{\"b\":null}}]}}}} , null,{{\"ObjectId\":\"{ids[2]}\"}},{{\"ObjectId\":\"{ids[3]}\",\"Mail\":\"\\u00e9\\\"\"}} ]  ";
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();

            var expected = JsonSerializer.Deserialize<List<AzureADUser>>(bytes.AsSpan(3), UserUploadMerge.ReadOptions)
                .Where(u => u != null)
                .Select(u => JsonSerializer.Serialize(u))
                .ToList();

            await using var reader = new UserArrayStreamReader(new OneByteStream(bytes), "bom.json");
            var actual = new List<string>();
            AzureADUser user;
            while ((user = await reader.ReadNextAsync(CancellationToken.None)) != null)
            {
                actual.Add(JsonSerializer.Serialize(user));
            }

            CollectionAssert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow("", typeof(JsonException))]
        [DataRow("   ", typeof(JsonException))]
        [DataRow("{}", typeof(JsonException))]
        [DataRow("[1]", typeof(JsonException))]
        [DataRow("[{\"ObjectId\":\"00000000-0000-0000-0000-000000000001\"}", typeof(JsonException))]
        [DataRow("[] []", typeof(JsonException))]
        [DataRow("null", typeof(Exception))]
        public async Task Reader_RejectsWhatTheMaterializingMergeRejects(string json, Type expected)
        {
            // The materializing merge's behaviour for the same bytes.
            var materialized = await Record(async () =>
            {
                var users = await JsonSerializer.DeserializeAsync<List<AzureADUser>>(new MemoryStream(Encoding.UTF8.GetBytes(json)), UserUploadMerge.ReadOptions);
                if (users == null)
                {
                    throw new Exception("Failed to deserialize blob: x");
                }
            });
            Assert.IsNotNull(materialized, "The materializing merge accepted the input.");
            Assert.IsInstanceOfType(materialized, expected);

            var streamed = await Record(async () =>
            {
                await using var reader = new UserArrayStreamReader(new MemoryStream(Encoding.UTF8.GetBytes(json)), "x");
                while (await reader.ReadNextAsync(CancellationToken.None) != null)
                {
                }
            });
            Assert.IsNotNull(streamed, "The streaming reader accepted the input.");
            Assert.AreEqual(materialized.GetType(), streamed.GetType());
        }

        [TestMethod]
        public void ReadBufferSize_KeepsAllInputsNearABudget()
        {
            Assert.AreEqual(4 * 1024 * 1024, UserUploadMerge.ReadBufferSize(1));
            Assert.AreEqual(32 * 1024 * 1024 / 140, UserUploadMerge.ReadBufferSize(140));
            Assert.AreEqual(64 * 1024, UserUploadMerge.ReadBufferSize(10_000));
            Assert.AreEqual(4 * 1024 * 1024, UserUploadMerge.ReadBufferSize(0));
        }

        [TestMethod]
        public void SortStable_OrdersCanonicallyAndKeepsEqualIdentifiersInOrder()
        {
            var a = Guid.Parse("a0000000-0000-0000-0000-000000000000");
            var b = Guid.Parse("0b000000-0000-0000-0000-000000000000");
            var members = new List<AzureADUser>();
            for (var i = 0; i < 50; i++)
            {
                members.Add(User(i % 2 == 0 ? a : b, $"m{i}"));
            }

            members.Insert(7, null);
            CanonicalMemberSort.SortStable(members);

            Assert.IsNull(members[0]);
            CollectionAssert.AreEqual(
                Enumerable.Range(0, 50).Where(i => i % 2 == 1).Select(i => $"m{i}").Concat(Enumerable.Range(0, 50).Where(i => i % 2 == 0).Select(i => $"m{i}")).ToList(),
                members.Skip(1).Select(m => m.DisplayName).ToList());

            CanonicalMemberSort.SortStable<AzureADUser>(null);
            var single = new List<AzureADUser> { User(a, "only") };
            CanonicalMemberSort.SortStable(single);
            Assert.AreEqual("only", single[0].DisplayName);
        }

        private static async Task<(byte[] Bytes, int Count, RecordingLogger<MergeStreamingBehaviorTests> Logger)> RunAsync(
            string target,
            bool writeOnce,
            MergeCase mergeCase)
        {
            var logger = new RecordingLogger<MergeStreamingBehaviorTests>();
            if (target == "production")
            {
                var fake = new AzureBlobContainerFake();
                foreach (var upload in mergeCase.Uploads)
                {
                    fake.Put(MergeCase.Prefix + upload.Name, upload.Bytes);
                }

                var repository = fake.CreateRepository(logger);
                var count = writeOnce
                    ? (await repository.MergeAndStreamUserBlobsWriteOnceAsync(MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]")).Count
                    : await repository.MergeAndStreamUserBlobsAsync(MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]");
                return (fake.GetBytes(MergeCase.OutputPath), count, logger);
            }

            var storage = new InMemoryBlobStorageRepository { MergeLogger = logger };
            foreach (var upload in mergeCase.Uploads)
            {
                storage.PutBytes(MergeCase.Prefix + upload.Name, upload.Bytes);
            }

            var storedCount = writeOnce
                ? (await storage.MergeAndStreamUserBlobsWriteOnceAsync(MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]")).Count
                : await storage.MergeAndStreamUserBlobsAsync(MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId, false, false, "[]");
            return (storage.GetBytes(MergeCase.OutputPath), storedCount, logger);
        }

        private static void AssertMatchesOracle(MergeCase mergeCase, byte[] actual)
        {
            var oracle = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(MergeStreamingGoldenTests.Oracle(mergeCase)));
            Assert.AreEqual(MergeStreamingGoldenTests.Sha256(oracle), MergeStreamingGoldenTests.Sha256(actual), $"{mergeCase.Name}: bytes differ from the materializing merge.");
        }

        private static MergeCase Case(string name, List<MergeUpload> uploads) =>
            new(name, uploads, new AzureADGroup { ObjectId = Guid.Parse("11111111-2222-3333-4444-555555555555") }, Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020100"), Guid.Parse("12345678-1234-1234-1234-123456789abc"), false, false, "[]");

        private static MergeUpload Upload(string name, IEnumerable<AzureADUser> users) =>
            new(name, JsonSerializer.SerializeToUtf8Bytes(users.ToList(), _readerOptions));

        private static AzureADUser User(Guid id, string marker) => new() { ObjectId = id, DisplayName = marker };

        private static Guid NewId(Random rng)
        {
            var bytes = new byte[16];
            rng.NextBytes(bytes);
            return new Guid(bytes);
        }

        private static async Task<Exception> Record(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Azure.Storage.Blobs.Specialized.BlockBlobClient RejectingBlockBlobClient()
        {
            var client = new Moq.Mock<Azure.Storage.Blobs.Specialized.BlockBlobClient>();
            client.Setup(c => c.StageBlockAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<Stream>(), Moq.It.IsAny<Azure.Storage.Blobs.Models.BlockBlobStageBlockOptions>(), Moq.It.IsAny<CancellationToken>()))
                .ReturnsAsync(Moq.Mock.Of<Azure.Response<Azure.Storage.Blobs.Models.BlockInfo>>());
            client.Setup(c => c.CommitBlockListAsync(Moq.It.IsAny<IEnumerable<string>>(), Moq.It.Is<Azure.Storage.Blobs.Models.CommitBlockListOptions>(o => o.Conditions != null && o.Conditions.IfNoneMatch == Azure.ETag.All), Moq.It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Azure.RequestFailedException(409, "exists", "BlobAlreadyExists", null));
            client.Setup(c => c.ExistsAsync(Moq.It.IsAny<CancellationToken>()))
                .ReturnsAsync(Azure.Response.FromValue(true, Moq.Mock.Of<Azure.Response>()));
            return client.Object;
        }

        private static Azure.Storage.Blobs.BlobClient ExistingBlobClient(byte[] content)
        {
            var client = new Moq.Mock<Azure.Storage.Blobs.BlobClient>();
            client.Setup(c => c.ExistsAsync(Moq.It.IsAny<CancellationToken>()))
                .ReturnsAsync(Azure.Response.FromValue(true, Moq.Mock.Of<Azure.Response>()));
            client.Setup(c => c.OpenReadAsync(Moq.It.IsAny<Azure.Storage.Blobs.Models.BlobOpenReadOptions>(), Moq.It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(content, writable: false));
            return client.Object;
        }

        /// <summary>Returns at most one byte per read, so every token and element spans reads.</summary>
        private sealed class OneByteStream : MemoryStream
        {
            public OneByteStream(byte[] content) : base(content, writable: false)
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));

            public override int Read(Span<byte> buffer) => base.Read(buffer.Slice(0, Math.Min(1, buffer.Length)));

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                base.ReadAsync(buffer.Slice(0, Math.Min(1, buffer.Length)), cancellationToken);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                base.ReadAsync(buffer, offset, Math.Min(1, count), cancellationToken);
        }
    }
}
