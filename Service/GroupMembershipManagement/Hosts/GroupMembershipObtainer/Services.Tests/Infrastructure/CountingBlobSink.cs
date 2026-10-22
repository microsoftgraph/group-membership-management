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
using System.Security.Cryptography;
using System.Threading;

namespace Tests.Services.Infrastructure
{
    /// <summary>
    /// Memory-lean blob store for allocation tests of the production <see cref="BlobStorageRepository"/>. It mocks
    /// the same Azure SDK calls as <see cref="AzureBlobContainerFake"/>, but holds each input once (reads return a
    /// read-only <see cref="MemoryStream"/> over it) and treats every output as a sink: uploaded and staged bytes
    /// are hashed and counted, never retained, so the measured heap is the code under test plus the inputs.
    /// </summary>
    internal sealed class CountingBlobSink
    {
        private readonly Dictionary<string, byte[]> _inputs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SinkOutput> _outputs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Staging> _staging = new(StringComparer.Ordinal);

        public CountingBlobSink()
        {
            Container = new Mock<BlobContainerClient>();
            Container.Setup(c => c.GetBlobClient(It.IsAny<string>()))
                .Returns<string>(path => BlobClient(path).Object);
            Container.Protected()
                .Setup<BlockBlobClient>("GetBlockBlobClientCore", ItExpr.IsAny<string>())
                .Returns<string>(path => BlockBlobClient(path).Object);
            Container.Setup(c => c.GetBlobsAsync(
                    It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<BlobTraits, BlobStates, string, CancellationToken>((traits, states, prefix, token) =>
                    AsyncPageable<BlobItem>.FromPages(new[]
                    {
                        Page<BlobItem>.FromValues(
                            _inputs.Keys.Concat(_outputs.Keys)
                                .Where(name => prefix == null || name.StartsWith(prefix, StringComparison.Ordinal))
                                .OrderBy(name => name, StringComparer.Ordinal)
                                .Select(name => BlobsModelFactory.BlobItem(name: name))
                                .ToList(),
                            null,
                            Mock.Of<Response>())
                    }));
        }

        public Mock<BlobContainerClient> Container { get; }

        public long InputBytes => _inputs.Values.Sum(bytes => (long)bytes.Length);

        public SinkOutput GetOutput(string path) => _outputs.TryGetValue(path, out var output) ? output : null;

        public void PutInput(string path, byte[] content) => _inputs[path] = content;

        private bool Exists(string path) => _inputs.ContainsKey(path) || _outputs.ContainsKey(path);

        private Stream OpenRead(string path) =>
            _inputs.TryGetValue(path, out var bytes)
                ? new MemoryStream(bytes, writable: false)
                : throw new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);

        private Mock<BlobClient> BlobClient(string path)
        {
            var client = new Mock<BlobClient>();
            client.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(Exists(path), Mock.Of<Response>()));
            client.Setup(b => b.OpenReadAsync(It.IsAny<BlobOpenReadOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OpenRead(path));
            client.Setup(b => b.OpenReadAsync(It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => OpenRead(path));
            client.Setup(b => b.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BinaryData content, bool overwrite, CancellationToken token) =>
                {
                    if (!overwrite && Exists(path))
                        throw new RequestFailedException(409, "The specified blob already exists.", "BlobAlreadyExists", null);
                    var span = content.ToMemory().Span;
                    _outputs[path] = new SinkOutput(span.Length, Convert.ToHexString(SHA256.HashData(span)), 0);
                    return Mock.Of<Response<BlobContentInfo>>();
                });
            return client;
        }

        private Mock<BlockBlobClient> BlockBlobClient(string path)
        {
            var client = new Mock<BlockBlobClient>();
            client.Setup(b => b.StageBlockAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<BlockBlobStageBlockOptions>(), It.IsAny<CancellationToken>()))
                .Callback<string, Stream, BlockBlobStageBlockOptions, CancellationToken>((id, content, options, token) => Stage(path, id, content))
                .ReturnsAsync(Mock.Of<Response<BlockInfo>>());
            client.Setup(b => b.CommitBlockListAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CommitBlockListOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<string> ids, CommitBlockListOptions options, CancellationToken token) =>
                {
                    if (options?.Conditions?.IfNoneMatch == ETag.All && Exists(path))
                        throw new RequestFailedException(409, "The specified blob already exists.", "BlobAlreadyExists", null);
                    var list = ids.ToList();
                    var staging = _staging.TryGetValue(path, out var st) ? st : new Staging();
                    if (!list.SequenceEqual(staging.Order))
                        throw new InvalidOperationException($"The sink requires the committed blocks to be exactly the staged blocks of {path}.");
                    _outputs[path] = new SinkOutput(staging.Length, Convert.ToHexString(staging.Hash.GetHashAndReset()), list.Count);
                    staging.Hash.Dispose();
                    _staging.Remove(path);
                    return Mock.Of<Response<BlobContentInfo>>();
                });
            client.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(Exists(path), Mock.Of<Response>()));
            return client;
        }

        private void Stage(string path, string blockId, Stream content)
        {
            if (!_staging.TryGetValue(path, out var staging))
            {
                staging = new Staging();
                _staging[path] = staging;
            }

            var buffer = new byte[81920];
            int read;
            while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
            {
                staging.Hash.AppendData(buffer, 0, read);
                staging.Length += read;
            }

            staging.Order.Add(blockId);
        }

        internal sealed record SinkOutput(long Length, string Sha256, int Blocks);

        private sealed class Staging
        {
            public readonly List<string> Order = new();
            public long Length;
            public readonly IncrementalHash Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }
    }
}
