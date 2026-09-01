// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    /// <summary>
    /// Buffers the JSON writer's small writes into bounded chunks before forwarding them to the
    /// Brotli stream, reducing compressor calls without retaining the complete membership.
    /// </summary>
    /// <remarks>
    /// Flush calls do not drain the buffer; <see cref="DrainAsync"/> controls cancellation-aware
    /// mid-stream writes. Disposal writes the remaining bytes but leaves the compressor open so it
    /// can finalize afterward.
    /// </remarks>
    internal sealed class CoalescingWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly byte[] _buffer;
        private int _pending;

        public CoalescingWriteStream(Stream inner, int bufferSize)
        {
            _inner = inner;
            _buffer = new byte[bufferSize];
        }

        public int PendingBytes => _pending;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(new ReadOnlySpan<byte>(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (buffer.Length > 0)
            {
                if (_pending == _buffer.Length)
                {
                    _inner.Write(_buffer, 0, _pending);
                    _pending = 0;
                }

                var take = Math.Min(_buffer.Length - _pending, buffer.Length);
                buffer.Slice(0, take).CopyTo(_buffer.AsSpan(_pending));
                _pending += take;
                buffer = buffer.Slice(take);
            }
        }

        public async ValueTask DrainAsync(CancellationToken cancellationToken)
        {
            if (_pending == 0) return;

            var count = _pending;
            _pending = 0;
            await _inner.WriteAsync(_buffer.AsMemory(0, count), cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _pending > 0)
            {
                _inner.Write(_buffer, 0, _pending);
                _pending = 0;
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await DrainAsync(CancellationToken.None);
            await base.DisposeAsync();
        }
    }
}
