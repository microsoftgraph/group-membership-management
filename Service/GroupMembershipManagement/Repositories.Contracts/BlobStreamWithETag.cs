// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    /// <summary>
    /// A blob's content stream together with the ETag and last-modified time of the version it reads.
    /// </summary>
    public sealed class BlobStreamWithETag : IAsyncDisposable, IDisposable
    {
        public BlobStreamWithETag(Stream content, string eTag, DateTimeOffset lastModified)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            ETag = eTag;
            LastModified = lastModified;
        }

        public Stream Content { get; }

        /// <summary>The ETag of the version read, for an <c>If-Match</c> write.</summary>
        public string ETag { get; }

        public DateTimeOffset LastModified { get; }

        public ValueTask DisposeAsync() => Content.DisposeAsync();

        public void Dispose() => Content.Dispose();
    }
}
