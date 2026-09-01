// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.FunctionApps
{
    [TestClass]
    public class BlobStorageRepositoryMembershipTests
    {
        private static GroupMembership Envelope() => new GroupMembership
        {
            Destination = new AzureADGroup { ObjectId = Guid.Parse("11111111-1111-1111-1111-111111111111") },
            RunId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SyncJobId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SourceMembers = new List<AzureADUser>()
        };

        private static async IAsyncEnumerable<AzureADUser> Members(
            IEnumerable<AzureADUser> members,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return member;
            }

            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<AzureADUser> FailingMembers(
            Exception failure,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AzureADUser { ObjectId = Guid.NewGuid() };
            await Task.Yield();
            throw failure;
        }

        private static async IAsyncEnumerable<AzureADUser> CancellingMembers(
            CancellationTokenSource cancellation,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AzureADUser { ObjectId = Guid.NewGuid() };
            cancellation.Cancel();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }

        [TestMethod]
        public async Task WriteMembershipAsyncPublishesCompleteContentAndMetadataTogether()
        {
            byte[] published = null;
            CommitBlockListOptions publishedOptions = null;
            var blobClient = StagedBlob((content, options) =>
            {
                published = content;
                publishedOptions = options;
            });
            var repository = MembershipRepository(blobClient);
            var metadata = new Dictionary<string, string> { ["status"] = "complete" };

            await repository.WriteMembershipAsync(
                "membership.json",
                Envelope(),
                Members(new[] { new AzureADUser { ObjectId = Guid.Parse("44444444-4444-4444-4444-444444444444") } }),
                metadata);

            Assert.IsNotNull(published);
            Assert.AreEqual("complete", publishedOptions.Metadata["status"]);
            var json = TextCompressor.Decompress(Encoding.UTF8.GetString(published));
            var membership = JsonSerializer.Deserialize<GroupMembership>(json);
            Assert.AreEqual(1, membership.SourceMembers.Count);
            Assert.AreEqual(Guid.Parse("44444444-4444-4444-4444-444444444444"), membership.SourceMembers[0].ObjectId);
            blobClient.Verify(client => client.SetMetadataAsync(
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task WriteMembershipAsyncDoesNotPublishWhenMemberEnumerationFails()
        {
            var originalContent = new byte[] { 1, 2, 3 };
            var publishedContent = originalContent;
            var blobClient = StagedBlob((content, options) => publishedContent = content);
            var repository = MembershipRepository(blobClient);
            var failure = new InvalidOperationException("enumeration failed");

            var actual = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                repository.WriteMembershipAsync("membership.json", Envelope(), FailingMembers(failure)));

            Assert.AreSame(failure, actual);
            CollectionAssert.AreEqual(originalContent, publishedContent);
            VerifyNotPublished(blobClient);
        }

        [TestMethod]
        public async Task WriteMembershipAsyncDoesNotPublishWhenCancelled()
        {
            var originalContent = new byte[] { 1, 2, 3 };
            var publishedContent = originalContent;
            var blobClient = StagedBlob((content, options) => publishedContent = content);
            var repository = MembershipRepository(blobClient);
            using var cancellation = new CancellationTokenSource();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                repository.WriteMembershipAsync(
                    "membership.json",
                    Envelope(),
                    CancellingMembers(cancellation),
                    cancellationToken: cancellation.Token));

            CollectionAssert.AreEqual(originalContent, publishedContent);
            VerifyNotPublished(blobClient);
        }

        [TestMethod]
        public async Task BlockStagingCommitsMultipleBlocksInOrder()
        {
            var expected = new byte[5 * 1024 * 1024];
            new Random(847_231).NextBytes(expected);
            byte[] published = null;
            var blockIds = new List<string>();
            var blobClient = StagedBlob(
                (content, options) => published = content,
                blockId => blockIds.Add(blockId));

            await using (var stream = new BlockBlobStagingWriteStream(blobClient.Object))
            {
                await stream.WriteAsync(expected);
                await stream.CommitAsync(null, CancellationToken.None);
            }

            CollectionAssert.AreEqual(expected, published);
            Assert.IsTrue(blockIds.All(blockId => blockId.Length == 64));
            blobClient.Verify(client => client.StageBlockAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<BlockBlobStageBlockOptions>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task DisposingUncommittedBlocksDoesNotPublishThem()
        {
            var published = false;
            var blobClient = StagedBlob((content, options) => published = true);

            await using (var stream = new BlockBlobStagingWriteStream(blobClient.Object))
            {
                await stream.WriteAsync(new byte[5 * 1024 * 1024]);
            }

            Assert.IsFalse(published);
            VerifyNotPublished(blobClient);
        }

        [TestMethod]
        public async Task ConcurrentStagingUsesIndependentBlocks()
        {
            var firstContent = Enumerable.Repeat((byte)0x11, 5 * 1024 * 1024).ToArray();
            var secondContent = Enumerable.Repeat((byte)0x22, 5 * 1024 * 1024).ToArray();
            var published = new List<byte[]>();
            var blobClient = StagedBlob((content, options) => published.Add(content));

            await using var first = new BlockBlobStagingWriteStream(blobClient.Object);
            await using var second = new BlockBlobStagingWriteStream(blobClient.Object);
            await first.WriteAsync(firstContent);
            await second.WriteAsync(secondContent);
            await first.CommitAsync(null, CancellationToken.None);
            await second.CommitAsync(null, CancellationToken.None);

            Assert.AreEqual(2, published.Count);
            CollectionAssert.AreEqual(firstContent, published[0]);
            CollectionAssert.AreEqual(secondContent, published[1]);
        }

        private static Mock<BlockBlobClient> StagedBlob(
            Action<byte[], CommitBlockListOptions> publish,
            Action<string> onBlockStaged = null)
        {
            var blocks = new Dictionary<string, byte[]>();
            var blobClient = new Mock<BlockBlobClient>();
            blobClient.Setup(client => client.StageBlockAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<BlockBlobStageBlockOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Stream, BlockBlobStageBlockOptions, CancellationToken>((
                    blockId,
                    content,
                    options,
                    cancellationToken) =>
                {
                    using var copy = new MemoryStream();
                    content.CopyTo(copy);
                    blocks[blockId] = copy.ToArray();
                    onBlockStaged?.Invoke(blockId);
                })
                .ReturnsAsync(Mock.Of<Response<BlockInfo>>());
            blobClient.Setup(client => client.CommitBlockListAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CommitBlockListOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<string>, CommitBlockListOptions, CancellationToken>((
                    blockIds,
                    options,
                    cancellationToken) =>
                {
                    using var content = new MemoryStream();
                    foreach (var blockId in blockIds)
                    {
                        content.Write(blocks[blockId]);
                    }

                    publish(content.ToArray(), options);
                })
                .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());
            return blobClient;
        }

        private static BlobStorageRepository MembershipRepository(
            Mock<BlockBlobClient> blobClient)
        {
            var container = new Mock<BlobContainerClient>();
            return new BlobStorageRepository(
                container.Object,
                path => path == "membership.json"
                    ? blobClient.Object
                    : throw new InvalidOperationException($"Unexpected path '{path}'."));
        }

        private static BlobStorageRepository Repository(Mock<BlobClient> blobClient)
        {
            var container = new Mock<BlobContainerClient>();
            container.Setup(client => client.GetBlobClient("membership.json")).Returns(blobClient.Object);
            return new BlobStorageRepository(container.Object);
        }

        private static void VerifyNotPublished(Mock<BlockBlobClient> blobClient) =>
            blobClient.Verify(client => client.CommitBlockListAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CommitBlockListOptions>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }
}
