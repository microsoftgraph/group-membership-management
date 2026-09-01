// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    /// <summary>
    /// Presents bytes consumed while detecting plain JSON versus Base64/Brotli followed by the
    /// unread source, so the decoder receives the complete payload without seeking.
    /// Disposing this stream does not dispose the caller-owned source.
    /// </summary>
    internal sealed class PrefixedStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly int _prefixLength;
        private readonly Stream _source;
        private int _position;

        public PrefixedStream(byte[] prefix, int prefixLength, Stream source)
        {
            _prefix = prefix;
            _prefixLength = prefixLength;
            _source = source;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < _prefixLength)
            {
                var take = Math.Min(count, _prefixLength - _position);
                Array.Copy(_prefix, _position, buffer, offset, take);
                _position += take;
                return take;
            }

            return _source.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < _prefixLength)
            {
                var take = Math.Min(buffer.Length, _prefixLength - _position);
                _prefix.AsMemory(_position, take).CopyTo(buffer);
                _position += take;
                return take;
            }

            return await _source.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
