// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Moq;
using Moq.Protected;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Services.Infrastructure
{
    /// <summary>
    /// Backs the production <see cref="BlobStorageRepository"/> with mocked Azure SDK clients over an
    /// in-memory byte store, so differential tests can compare production bytes with the in-memory double.
    /// Only the SDK calls made by the repository operations under test are modelled.
    /// </summary>
    internal sealed class AzureBlobContainerFake
    {
        private readonly Dictionary<string, StoredBlob> _blobs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, byte[]>> _stagedBlocks =
            new(StringComparer.Ordinal);

        public AzureBlobContainerFake()
        {
            Container = new Mock<BlobContainerClient>();
            Container
                .Setup(client => client.GetBlobClient(It.IsAny<string>()))
                .Returns<string>(path => BlobClient(path).Object);
            Container
                .Protected()
                .Setup<BlockBlobClient>("GetBlockBlobClientCore", ItExpr.IsAny<string>())
                .Returns<string>(path => BlockBlobClient(path).Object);
            Container
                .Setup(client => client.GetBlobsAsync(
                    It.IsAny<BlobTraits>(),
                    It.IsAny<BlobStates>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns<BlobTraits, BlobStates, string, CancellationToken>(
                    (traits, states, prefix, cancellationToken) =>
                        AsyncPageable<BlobItem>.FromPages(new[]
                        {
                            Page<BlobItem>.FromValues(
                                ListNames(prefix)
                                    .Select(name => BlobsModelFactory.BlobItem(name: name))
                                    .ToList(),
                                null,
                                Mock.Of<Response>())
                        }));
        }

        public Mock<BlobContainerClient> Container { get; }

        /// <summary>
        /// Runs when a block-list commit starts, before it resolves its blocks, with the blob path; for example to let a
        /// concurrent duplicate writer commit first. As in Azure, a commit discards the blob's other uncommitted blocks,
        /// and a commit naming a block that is no longer staged fails with <c>InvalidBlockList</c>.
        /// </summary>
        public Func<string, Task> BeforeCommitBlockListAsync { get; set; }

        public BlobStorageRepository CreateRepository(Microsoft.Extensions.Logging.ILogger logger = null) =>
            new(Container.Object, path => BlockBlobClient(path).Object, logger);

        public void Put(string path, byte[] content, IDictionary<string, string> metadata = null)
        {
            _blobs[path] = new StoredBlob(content.ToArray(), Copy(metadata));
        }

        public byte[] GetBytes(string path) =>
            _blobs.TryGetValue(path, out var blob) ? blob.Content.ToArray() : null;

        public IDictionary<string, string> GetMetadata(string path) =>
            _blobs.TryGetValue(path, out var blob) ? Copy(blob.Metadata) : null;

        private IEnumerable<string> ListNames(string prefix) =>
            _blobs.Keys
                .Where(name => prefix == null || name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

        private Mock<BlobClient> BlobClient(string path)
        {
            var client = new Mock<BlobClient>();
            client
                .Setup(blob => blob.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(_blobs.ContainsKey(path), Mock.Of<Response>()));
            client
                .Setup(blob => blob.OpenReadAsync(
                    It.IsAny<BlobOpenReadOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OpenRead(path));
            client
                .Setup(blob => blob.OpenReadAsync(
                    It.IsAny<long>(),
                    It.IsAny<int?>(),
                    It.IsAny<BlobRequestConditions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OpenRead(path));
            client
                .Setup(blob => blob.OpenWriteAsync(
                    It.IsAny<bool>(),
                    It.IsAny<BlobOpenWriteOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((bool overwrite, BlobOpenWriteOptions options, CancellationToken cancellationToken) =>
                    new CommitOnDisposeStream(bytes => Put(path, bytes, options?.Metadata)));
            client
                .Setup(blob => blob.UploadAsync(
                    It.IsAny<BinaryData>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((BinaryData content, bool overwrite, CancellationToken cancellationToken) =>
                {
                    if (!overwrite && _blobs.ContainsKey(path))
                    {
                        throw new RequestFailedException(409, "The specified blob already exists.", "BlobAlreadyExists", null);
                    }

                    Put(path, content.ToArray());
                    return Mock.Of<Response<BlobContentInfo>>();
                });
            client
                .Setup(blob => blob.UploadAsync(
                    It.IsAny<BinaryData>(),
                    It.IsAny<BlobUploadOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((BinaryData content, BlobUploadOptions options, CancellationToken cancellationToken) =>
                {
                    if (options?.Conditions?.IfNoneMatch == ETag.All && _blobs.ContainsKey(path))
                    {
                        throw new RequestFailedException(409, "The specified blob already exists.", "BlobAlreadyExists", null);
                    }

                    Put(path, content.ToArray(), options?.Metadata);
                    return Mock.Of<Response<BlobContentInfo>>();
                });
            return client;
        }

        private Mock<BlockBlobClient> BlockBlobClient(string path)
        {
            var client = new Mock<BlockBlobClient>();
            client
                .Setup(blob => blob.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(_blobs.ContainsKey(path), Mock.Of<Response>()));
            client
                .Setup(blob => blob.StageBlockAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<BlockBlobStageBlockOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Stream, BlockBlobStageBlockOptions, CancellationToken>(
                    (blockId, content, options, cancellationToken) => Stage(path, blockId, content))
                .ReturnsAsync(Mock.Of<Response<BlockInfo>>());
#pragma warning disable CS0618 // The SDK binds two-argument StageBlockAsync calls to this overload.
            client
                .Setup(blob => blob.StageBlockAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<BlobRequestConditions>(),
                    It.IsAny<IProgress<long>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Stream, byte[], BlobRequestConditions, IProgress<long>, CancellationToken>(
                    (blockId, content, hash, conditions, progress, cancellationToken) => Stage(path, blockId, content))
                .ReturnsAsync(Mock.Of<Response<BlockInfo>>());
#pragma warning restore CS0618
            client
                .Setup(blob => blob.CommitBlockListAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CommitBlockListOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (IEnumerable<string> blockIds, CommitBlockListOptions options, CancellationToken cancellationToken) =>
                {
                    if (BeforeCommitBlockListAsync != null)
                    {
                        await BeforeCommitBlockListAsync(path);
                    }

                    var staged = _stagedBlocks.TryGetValue(path, out var blocks)
                        ? blocks
                        : new Dictionary<string, byte[]>();
                    using var content = new MemoryStream();
                    foreach (var blockId in blockIds)
                    {
                        if (!staged.TryGetValue(blockId, out var block))
                        {
                            throw new RequestFailedException(400, "The specified block list is invalid.", "InvalidBlockList", null);
                        }

                        content.Write(block);
                    }

                    _stagedBlocks.Remove(path);
                    Put(path, content.ToArray(), options?.Metadata);
                    return Mock.Of<Response<BlobContentInfo>>();
                });
            return client;
        }

        private void Stage(string path, string blockId, Stream content)
        {
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            if (!_stagedBlocks.TryGetValue(path, out var blocks))
            {
                blocks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                _stagedBlocks[path] = blocks;
            }

            blocks[blockId] = copy.ToArray();
        }

        private Stream OpenRead(string path)
        {
            if (!_blobs.TryGetValue(path, out var blob))
            {
                throw new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);
            }

            return new MemoryStream(blob.Content, writable: false);
        }

        private static Dictionary<string, string> Copy(IDictionary<string, string> metadata) =>
            metadata == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(metadata);

        private sealed record StoredBlob(byte[] Content, Dictionary<string, string> Metadata);

        private sealed class CommitOnDisposeStream : MemoryStream
        {
            private readonly Action<byte[]> _commit;
            private bool _committed;

            public CommitOnDisposeStream(Action<byte[]> commit)
            {
                _commit = commit;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_committed)
                {
                    _committed = true;
                    _commit(ToArray());
                }

                base.Dispose(disposing);
            }
        }
    }
}
