// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.FunctionApps
{
    [TestClass]
    public class MembershipStreamBufferingTests
    {
        private const int LargeMemberCount = 500_000;

        private static readonly Guid _destinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid _runId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid _syncJobId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        [TestMethod]
        public async Task BytesReachTheDestinationBeforeTheLastMemberIsProduced()
        {
            using var destination = new ProgressStream();
            long bytesWrittenAtHalfway = -1;

            await MembershipStream.WriteAsync(
                destination,
                Envelope(),
                Members(LargeMemberCount, index =>
                {
                    if (index == LargeMemberCount / 2)
                    {
                        bytesWrittenAtHalfway = destination.BytesWritten;
                    }
                }));

            Assert.IsTrue(
                bytesWrittenAtHalfway > 0,
                "nothing had reached the destination by the halfway member, so the payload is being "
                    + "buffered rather than streamed.");
        }

        [TestMethod]
        public async Task MembersAttachedToTheEnvelopeAreNeverSerialized()
        {
            var envelope = Envelope();
            envelope.SourceMembers = new List<AzureADUser>(LargeMemberCount);
            for (var i = 0; i < LargeMemberCount; i++)
            {
                envelope.SourceMembers.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
            }

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, Members(3));

            var written = JsonSerializer.Deserialize<GroupMembership>(
                TextCompressor.Decompress(Encoding.UTF8.GetString(destination.ToArray())));

            Assert.AreEqual(3, written.SourceMembers.Count, "the list attached to the envelope was serialized.");
            Assert.AreEqual(
                LargeMemberCount,
                envelope.SourceMembers.Count,
                "the writer changed the envelope.");
        }

        private static async IAsyncEnumerable<AzureADUser> Members(int count, Action<int> onEach = null)
        {
            for (var i = 0; i < count; i++)
            {
                onEach?.Invoke(i);
                yield return new AzureADUser { ObjectId = Guid.NewGuid() };
            }

            await Task.CompletedTask;
        }

        private static GroupMembership Envelope() => new GroupMembership
        {
            Destination = new AzureADGroup { ObjectId = _destinationId },
            RunId = _runId,
            SyncJobId = _syncJobId,
            Exclusionary = true,
            IsLastMessage = true,
            MessageIndex = 3,
            TotalMessageCount = 5,
            Query = "[{\"type\":\"SqlMembership\"}]",
            SourceMembers = new List<AzureADUser>()
        };

        private sealed class ProgressStream : Stream
        {
            public long BytesWritten { get; private set; }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => BytesWritten;
            public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

            public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                BytesWritten += count;
                return Task.CompletedTask;
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                BytesWritten += buffer.Length;
                return ValueTask.CompletedTask;
            }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
