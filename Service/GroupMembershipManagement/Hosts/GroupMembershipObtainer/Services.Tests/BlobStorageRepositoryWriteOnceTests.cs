// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
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
    /// <summary>
    /// FR-019: production write-once uploads stage blocks and publish with one If-None-Match: * commit,
    /// keep the overwriting uploads' bytes and metadata, and map a lost race to AlreadyExisted only after
    /// confirming the blob exists.
    /// </summary>
    [TestClass]
    public class BlobStorageRepositoryWriteOnceTests
    {
        private const string TargetPath = "target.json";

        [TestMethod]
        public async Task UploadGroupMembershipFromGuidsWriteOnce_MatchesLegacyBytesAndCommitsIfNoneMatch()
        {
            var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToHashSet();
            var destination = new AzureADGroup { ObjectId = Guid.NewGuid() };
            var runId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();
            var legacyBytes = new MemoryStream();
            var legacyClient = new Mock<BlobClient>();
            legacyClient
                .Setup(x => x.OpenWriteAsync(true, It.IsAny<BlobOpenWriteOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(legacyBytes);
            var commit = new CommitCapture();
            var repository = Repository(commit.Client, container =>
                container.Setup(x => x.GetBlobClient(TargetPath)).Returns(legacyClient.Object));

            await repository.UploadGroupMembershipFromGuidsAsync(
                TargetPath, ids, destination, runId, syncJobId, true, false, "[]");
            var result = await repository.UploadGroupMembershipFromGuidsWriteOnceAsync(
                TargetPath, ids, destination, runId, syncJobId, true, false, "[]");

            Assert.IsFalse(result.AlreadyExisted);
            Assert.AreEqual(ids.Count, result.Count);
            CollectionAssert.AreEqual(legacyBytes.ToArray(), commit.Content);
            commit.AssertIfNoneMatchAll();
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(25001)]
        public async Task UploadCacheFromGuidsWriteOnce_WritesLegacyCacheBytesAndMetadata(int idCount)
        {
            var ids = Enumerable.Range(0, idCount).Select(_ => Guid.NewGuid()).ToList();
            var metadata = new Dictionary<string, string> { ["RunId"] = "run", ["NumberOfUsers"] = idCount.ToString() };
            var commit = new CommitCapture();
            var repository = Repository(commit.Client);

            var result = await repository.UploadCacheFromGuidsWriteOnceAsync(TargetPath, ids, metadata);

            Assert.IsFalse(result.AlreadyExisted);
            Assert.AreEqual(idCount, result.Count);
            Assert.AreEqual(string.Join(Environment.NewLine, ids), Encoding.UTF8.GetString(commit.Content));
            commit.AssertIfNoneMatchAll();
            if (idCount > 0)
            {
                CollectionAssert.AreEquivalent(metadata.ToArray(), commit.Options.Metadata.ToArray());
            }
        }

        [TestMethod]
        public async Task UploadFileWriteOnce_WritesUtf8ContentAndMetadata()
        {
            var content = "https://graph.microsoft.com/v1.0/groups/delta?$deltatoken=é中";
            var commit = new CommitCapture();
            var repository = Repository(commit.Client);

            var result = await repository.UploadFileWriteOnceAsync(
                TargetPath,
                content,
                new Dictionary<string, string> { ["k"] = "v" });

            Assert.IsFalse(result.AlreadyExisted);
            CollectionAssert.AreEqual(BinaryData.FromString(content).ToArray(), commit.Content);
            Assert.AreEqual("v", commit.Options.Metadata["k"]);
            commit.AssertIfNoneMatchAll();
        }

        [TestMethod]
        public async Task StreamMembershipToCacheWriteOnce_WritesLegacyCacheBytesAndNumberOfUsers()
        {
            var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
            var membership = JsonSerializer.SerializeToUtf8Bytes(new GroupMembership
            {
                SourceMembers = ids.Select(id => new AzureADUser { ObjectId = id }).ToList()
            });
            var source = new Mock<BlobClient>();
            source.Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
            source.Setup(x => x.OpenReadAsync(It.IsAny<BlobOpenReadOptions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(() => new MemoryStream(membership));
            var commit = new CommitCapture();
            var repository = Repository(commit.Client, container =>
                container.Setup(x => x.GetBlobClient("source.json")).Returns(source.Object));

            var result = await repository.StreamMembershipToCacheWriteOnceAsync(
                "source.json",
                TargetPath,
                new Dictionary<string, string> { ["RunId"] = "run" });

            Assert.IsFalse(result.AlreadyExisted);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(string.Join(Environment.NewLine, ids), Encoding.UTF8.GetString(commit.Content));
            Assert.AreEqual("run", commit.Options.Metadata["RunId"]);
            Assert.AreEqual("3", commit.Options.Metadata["NumberOfUsers"]);
            commit.AssertIfNoneMatchAll();
        }

        // sortedPages: true is the uploads the readers now write (both merges stream into staged blocks); false is an
        // older release's uploads (both fall back to the materializing merge). Either way both forms write the legacy
        // bytes, JsonSerializer.Serialize of the merged membership, and the write-once commit is If-None-Match: *.
        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task MergeAndStreamUserBlobsWriteOnce_MatchesLegacyMergeBytes(bool sortedPages)
        {
            var users = Enumerable.Range(0, 4)
                .Select(index => new AzureADUser { ObjectId = Guid.Parse($"{(sortedPages ? index : 9 - index)}0000000-0000-0000-0000-000000000000"), DisplayName = $"user-{index}" })
                .ToList();
            BinaryData legacyUpload = null;
            var legacyDestination = new Mock<BlobClient>();
            legacyDestination
                .Setup(x => x.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()))
                .Callback<BinaryData, bool, CancellationToken>((data, _, _) => legacyUpload = data)
                .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
            var commit = new CommitCapture();
            var destination = new AzureADGroup { ObjectId = Guid.NewGuid() };
            var runId = Guid.NewGuid();
            var syncJobId = Guid.NewGuid();
            var repository = Repository(commit.Client, container =>
            {
                SetupPages(container, users);
                container.Setup(x => x.GetBlobClient(TargetPath)).Returns(legacyDestination.Object);
            });

            var legacyBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new GroupMembership
            {
                SourceMembers = users.OrderBy(user => user.ObjectId.ToString("D"), StringComparer.Ordinal).ToList(),
                Destination = destination,
                RunId = runId,
                SyncJobId = syncJobId,
                Query = "[]"
            }));

            var legacyCount = await repository.MergeAndStreamUserBlobsAsync(
                "prefix/", TargetPath, destination, runId, syncJobId, false, false, "[]");
            if (sortedPages)
            {
                Assert.IsNull(legacyUpload, "Sorted uploads must stream into staged blocks.");
                Assert.IsNull(commit.Options.Conditions, "The overwriting merge commits unconditionally.");
                CollectionAssert.AreEqual(legacyBytes, commit.Content);
            }
            else
            {
                CollectionAssert.AreEqual(legacyBytes, legacyUpload.ToArray());
            }

            var result = await repository.MergeAndStreamUserBlobsWriteOnceAsync(
                "prefix/", TargetPath, destination, runId, syncJobId, false, false, "[]");

            Assert.IsFalse(result.AlreadyExisted);
            Assert.AreEqual(legacyCount, result.Count);
            Assert.AreEqual(users.Count, result.Count);
            CollectionAssert.AreEqual(legacyBytes, commit.Content);
            commit.AssertIfNoneMatchAll();
        }

        [TestMethod]
        public async Task MergeAndStreamUserBlobsWriteOnce_Rejected_ReportsExistingBlobCountNotListing()
        {
            var existingIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
            var existing = JsonSerializer.SerializeToUtf8Bytes(new GroupMembership
            {
                SourceMembers = existingIds.Select(id => new AzureADUser { ObjectId = id }).ToList()
            });
            var existingClient = new Mock<BlobClient>();
            existingClient.Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
                          .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
            existingClient.Setup(x => x.OpenReadAsync(It.IsAny<BlobOpenReadOptions>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(() => new MemoryStream(existing));
            var commit = new CommitCapture(new RequestFailedException(412, "exists", "ConditionNotMet", null), exists: true);
            var repository = Repository(commit.Client, container =>
            {
                SetupPages(container, new List<AzureADUser>());
                container.Setup(x => x.GetBlobClient(TargetPath)).Returns(existingClient.Object);
            });

            var result = await repository.MergeAndStreamUserBlobsWriteOnceAsync(
                "prefix/", TargetPath, new AzureADGroup(), Guid.NewGuid(), Guid.NewGuid(), false, false, null);

            Assert.IsTrue(result.AlreadyExisted);
            Assert.AreEqual(3, result.Count);
        }

        [DataTestMethod]
        [DataRow(409, "BlobAlreadyExists")]
        [DataRow(412, "ConditionNotMet")]
        [DataRow(400, "InvalidBlockList")]
        public async Task WriteOnceRejection_WhenBlobExists_IsAlreadyExisted(int status, string errorCode)
        {
            var commit = new CommitCapture(new RequestFailedException(status, "rejected", errorCode, null), exists: true);
            var repository = Repository(commit.Client);

            var result = await repository.UploadFileWriteOnceAsync(TargetPath, "content");

            Assert.IsTrue(result.AlreadyExisted);
        }

        [DataTestMethod]
        [DataRow(409, "BlobAlreadyExists")]
        [DataRow(412, "ConditionNotMet")]
        [DataRow(400, "InvalidBlockList")]
        public async Task WriteOnceRejection_WhenBlobIsMissing_Rethrows(int status, string errorCode)
        {
            var commit = new CommitCapture(new RequestFailedException(status, "rejected", errorCode, null), exists: false);
            var repository = Repository(commit.Client);

            await Assert.ThrowsExceptionAsync<RequestFailedException>(
                () => repository.UploadCacheFromGuidsWriteOnceAsync(TargetPath, new[] { Guid.NewGuid() }));
        }

        [TestMethod]
        public async Task WriteOnceOtherFailure_RethrowsWithoutExistenceCheck()
        {
            var commit = new CommitCapture(new RequestFailedException(500, "server error", "InternalError", null), exists: true);
            var repository = Repository(commit.Client);

            await Assert.ThrowsExceptionAsync<RequestFailedException>(
                () => repository.UploadFileWriteOnceAsync(TargetPath, "content"));
            commit.Client.Verify(x => x.ExistsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        private static BlobStorageRepository Repository(
            Mock<BlockBlobClient> target,
            Action<Mock<BlobContainerClient>> setupContainer = null)
        {
            var container = new Mock<BlobContainerClient>();
            setupContainer?.Invoke(container);
            return new BlobStorageRepository(
                container.Object,
                path => path == TargetPath
                    ? target.Object
                    : throw new InvalidOperationException($"Unexpected path '{path}'."));
        }

        private static void SetupPages(Mock<BlobContainerClient> container, List<AzureADUser> users)
        {
            var page = Azure.Page<BlobItem>.FromValues(
                new[] { BlobsModelFactory.BlobItem(name: "prefix/page.json") },
                continuationToken: null,
                response: Mock.Of<Response>());
            container.Setup(x => x.GetBlobsAsync(It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Returns(() => AsyncPageable<BlobItem>.FromPages(new[] { page }));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(users);
            var source = new Mock<BlobClient>();
            source.Setup(x => x.OpenReadAsync(It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(() => new MemoryStream(bytes));
            container.Setup(x => x.GetBlobClient("prefix/page.json")).Returns(source.Object);
        }

        private sealed class CommitCapture
        {
            private readonly Dictionary<string, byte[]> _blocks = new();

            public CommitCapture(Exception commitFailure = null, bool exists = false)
            {
                Client.Setup(client => client.StageBlockAsync(
                        It.IsAny<string>(),
                        It.IsAny<Stream>(),
                        It.IsAny<BlockBlobStageBlockOptions>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<string, Stream, BlockBlobStageBlockOptions, CancellationToken>((blockId, content, _, _) =>
                    {
                        using var copy = new MemoryStream();
                        content.CopyTo(copy);
                        _blocks[blockId] = copy.ToArray();
                    })
                    .ReturnsAsync(Mock.Of<Response<BlockInfo>>());
                var commitSetup = Client.Setup(client => client.CommitBlockListAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CommitBlockListOptions>(),
                    It.IsAny<CancellationToken>()));
                if (commitFailure != null)
                {
                    commitSetup.ThrowsAsync(commitFailure);
                }
                else
                {
                    commitSetup
                        .Callback<IEnumerable<string>, CommitBlockListOptions, CancellationToken>((blockIds, options, _) =>
                        {
                            using var content = new MemoryStream();
                            foreach (var blockId in blockIds)
                            {
                                content.Write(_blocks[blockId]);
                            }

                            Content = content.ToArray();
                            Options = options;
                        })
                        .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
                }

                Client.Setup(client => client.ExistsAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));
            }

            public Mock<BlockBlobClient> Client { get; } = new();
            public byte[] Content { get; private set; }
            public CommitBlockListOptions Options { get; private set; }

            public void AssertIfNoneMatchAll()
            {
                Assert.IsNotNull(Options, "The blob was not committed.");
                Assert.AreEqual(ETag.All, Options.Conditions?.IfNoneMatch, "The final commit was not If-None-Match: *.");
            }
        }
    }
}
