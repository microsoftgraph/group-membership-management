// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    /// <summary>
    /// Buffers one block at a time in memory and leaves the existing blob unchanged until commit.
    /// </summary>
    internal sealed class BlockBlobStagingWriteStream : Stream
    {
        private const int BlockSize = 4 * 1024 * 1024;

        private readonly BlockBlobClient _client;
        private readonly List<string> _blockIds = new List<string>();
        private readonly byte[] _blockIdPrefix = Guid.NewGuid().ToByteArray();
        private byte[] _buffer;
        private int _buffered;
        private int _blockNumber;
        private bool _committed;
        private bool _disposed;

        /// <summary>
        /// Creates a staging stream backed by one pooled block buffer.
        /// </summary>
        public BlockBlobStagingWriteStream(BlockBlobClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _buffer = ArrayPool<byte>.Shared.Rent(BlockSize);
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => !_disposed && !_committed;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Reading is not supported by this write-only stream.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        /// <summary>
        /// Seeking is not supported because blocks are staged sequentially.
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        /// <summary>
        /// Resizing is not supported because staged blocks cannot be truncated.
        /// </summary>
        public override void SetLength(long value) =>
            throw new NotSupportedException();

        /// <summary>
        /// Validates the stream state without staging a partial block.
        /// </summary>
        public override void Flush()
        {
            ThrowIfNotWritable();
        }

        /// <summary>
        /// Validates the stream state and cancellation without staging a partial block.
        /// </summary>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfNotWritable();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Writes bytes synchronously, staging each full block before accepting more data.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count) =>
            Write(new ReadOnlySpan<byte>(buffer, offset, count));

        /// <summary>
        /// Buffers bytes and synchronously stages each complete block.
        /// </summary>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ThrowIfNotWritable();

            while (!buffer.IsEmpty)
            {
                if (_buffered == BlockSize)
                {
                    StageBufferedBlockAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                var bytesToCopy = Math.Min(BlockSize - _buffered, buffer.Length);
                buffer.Slice(0, bytesToCopy).CopyTo(_buffer.AsSpan(_buffered));
                _buffered += bytesToCopy;
                buffer = buffer.Slice(bytesToCopy);
            }
        }

        /// <summary>
        /// Buffers bytes and asynchronously stages each complete block.
        /// </summary>
        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNotWritable();

            while (!buffer.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_buffered == BlockSize)
                {
                    await StageBufferedBlockAsync(cancellationToken);
                }

                var bytesToCopy = Math.Min(BlockSize - _buffered, buffer.Length);
                buffer.Slice(0, bytesToCopy).CopyTo(_buffer.AsMemory(_buffered));
                _buffered += bytesToCopy;
                buffer = buffer.Slice(bytesToCopy);
            }
        }

        /// <summary>
        /// Stages the final partial block and publishes the complete block list with its metadata.
        /// </summary>
        public async Task CommitAsync(
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            ThrowIfNotWritable();
            await StageBufferedBlockAsync(cancellationToken);

            var options = new CommitBlockListOptions
            {
                Metadata = metadata != null && metadata.Count > 0 ? metadata : null
            };
            await _client.CommitBlockListAsync(_blockIds, options, cancellationToken);
            _committed = true;
        }

        /// <summary>
        /// Returns the pooled buffer without publishing uncommitted blocks.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReturnBuffer();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Returns the pooled buffer without publishing uncommitted blocks.
        /// </summary>
        public override ValueTask DisposeAsync()
        {
            ReturnBuffer();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Stages the buffered bytes as the next block and resets the buffer.
        /// </summary>
        private async Task StageBufferedBlockAsync(CancellationToken cancellationToken)
        {
            if (_buffered == 0)
            {
                return;
            }

            // Match the Azure SDK's 48-byte block IDs when overwriting blobs it uploaded.
            var blockIdBytes = new byte[48];
            _blockIdPrefix.CopyTo(blockIdBytes, 0);
            BitConverter.GetBytes(_blockNumber).CopyTo(blockIdBytes, _blockIdPrefix.Length);
            var blockId = Convert.ToBase64String(blockIdBytes);
            using var content = new MemoryStream(
                _buffer,
                0,
                _buffered,
                writable: false,
                publiclyVisible: true);
            await _client.StageBlockAsync(
                blockId,
                content,
                options: null,
                cancellationToken);
            _blockIds.Add(blockId);
            _blockNumber++;
            _buffered = 0;
        }

        /// <summary>
        /// Rejects writes after disposal or publication.
        /// </summary>
        private void ThrowIfNotWritable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed)
            {
                throw new InvalidOperationException("The staged blob has already been committed.");
            }
        }

        /// <summary>
        /// Returns the pooled buffer exactly once and marks the stream as disposed.
        /// </summary>
        private void ReturnBuffer()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
            _buffered = 0;
        }
    }
}
