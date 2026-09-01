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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.FunctionApps
{
    /// <summary>Tests streaming reads and writes of membership blobs.</summary>
    [TestClass]
    public class MembershipStreamTests
    {
        private static GroupMembership Envelope(bool exclusionary = false) => new GroupMembership
        {
            Destination = new AzureADGroup { ObjectId = Guid.Parse("11111111-1111-1111-1111-111111111111") },
            RunId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SyncJobId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Exclusionary = exclusionary,
            Query = "[{\"type\":\"SqlMembership\"}]",
            SourceMembers = new List<AzureADUser>()
        };

        private static async IAsyncEnumerable<AzureADUser> Stream(IEnumerable<AzureADUser> members)
        {
            foreach (var member in members)
            {
                yield return member;
            }
            await Task.CompletedTask;
        }

        private static async Task<byte[]> WriteToBytesAsync(GroupMembership envelope, IEnumerable<AzureADUser> members)
        {
            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, Stream(members));
            return destination.ToArray();
        }

        private static async Task<(GroupMembership Envelope, List<AzureADUser> Members)> ReadAsync(byte[] blob)
        {
            using var source = new MemoryStream(blob);
            GroupMembership envelope = null;
            var members = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(source, e => envelope = e))
            {
                members.Add(member);
            }
            return (envelope, members);
        }

        /// <summary>Builds the released serializer layout with fields after the members.</summary>
        private static byte[] LegacyBlob(GroupMembership envelope, IEnumerable<AzureADUser> members)
        {
            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            var json = new StringBuilder();
            json.Append("{\"Destination\":").Append(JsonSerializer.Serialize(envelope.Destination, options));
            json.Append(",\"SourceMembers\":").Append(JsonSerializer.Serialize(members.ToList(), options));
            json.Append(",\"RunId\":\"").Append(envelope.RunId).Append('"');
            json.Append(",\"SyncJobId\":\"").Append(envelope.SyncJobId).Append('"');
            json.Append(",\"Exclusionary\":").Append(envelope.Exclusionary ? "true" : "false");
            json.Append(",\"Query\":").Append(JsonSerializer.Serialize(envelope.Query, options));
            json.Append('}');

            return Encoding.UTF8.GetBytes(TextCompressor.Compress(json.ToString()));
        }

        /// <summary>Builds the released hand-written layout with members first.</summary>
        private static byte[] LegacyHandBuiltBlob(GroupMembership envelope, IEnumerable<AzureADUser> members)
        {
            var json = new StringBuilder();
            json.Append("{\"SourceMembers\":[");
            json.Append(string.Join(",", members.Select(m => $"{{\"ObjectId\":\"{m.ObjectId}\"}}")));
            json.Append("],\"Destination\":").Append(JsonSerializer.Serialize(envelope.Destination));
            json.Append(",\"RunId\":\"").Append(envelope.RunId).Append('"');
            json.Append(",\"SyncJobId\":\"").Append(envelope.SyncJobId).Append('"');
            json.Append(",\"Exclusionary\":").Append(envelope.Exclusionary ? "true" : "false");
            json.Append(",\"MembershipObtainerDryRunEnabled\":false");
            json.Append(",\"Query\":").Append(JsonSerializer.Serialize(envelope.Query));
            json.Append('}');

            return Encoding.UTF8.GetBytes(TextCompressor.Compress(json.ToString()));
        }

        [TestMethod]
        public async Task ReadsBlobsWrittenByTheHandBuiltWriterWhereEveryScalarFollowsTheMembers()
        {
            var members = Enumerable.Range(1, 50).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var blob = LegacyHandBuiltBlob(Envelope(exclusionary: true), members);

            var (envelope, read) = await ReadAsync(blob);

            CollectionAssert.AreEqual(members.Select(m => m.ObjectId).ToList(), read.Select(m => m.ObjectId).ToList());
            Assert.IsTrue(envelope.Exclusionary);
            Assert.AreEqual(Guid.Parse("22222222-2222-2222-2222-222222222222"), envelope.RunId);
            Assert.AreEqual(Guid.Parse("33333333-3333-3333-3333-333333333333"), envelope.SyncJobId);
            Assert.AreEqual("[{\"type\":\"SqlMembership\"}]", envelope.Query);
            Assert.AreEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), envelope.Destination.ObjectId);
        }

        [TestMethod]
        public async Task ClassifiesAtTheEndWhenTheMembersComeFirst()
        {
            var members = Enumerable.Range(1, 200).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var blob = LegacyHandBuiltBlob(Envelope(exclusionary: true), members);

            using var source = new MemoryStream(blob);
            var reported = 0;

            await foreach (var _ in MembershipStream.ReadAsync(source, _ => reported++)) { }

            Assert.AreEqual(1, reported, "The envelope must be reported exactly once, however late.");
        }

        [TestMethod]
        public void TheLegacyBlobHelperPlacesTheMembersBeforeTheScalars()
        {
            var blob = LegacyBlob(Envelope(), new[] { new AzureADUser { ObjectId = Guid.NewGuid() } });
            var json = TextCompressor.Decompress(Encoding.UTF8.GetString(blob));

            Assert.IsTrue(
                json.IndexOf("SourceMembers", StringComparison.Ordinal) < json.IndexOf("RunId", StringComparison.Ordinal),
                $"The helper stopped reproducing the shipped layout, so the compatibility tests below no longer " +
                $"cover blobs written by shipped code. It emitted:\n{json}");
        }

        [TestMethod]
        public async Task RoundTripsMembersAndEnvelope()
        {
            var members = Enumerable.Range(1, 500)
                .Select(i => new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroup = Guid.NewGuid() })
                .ToList();

            var blob = await WriteToBytesAsync(Envelope(exclusionary: true), members);
            var (envelope, read) = await ReadAsync(blob);

            CollectionAssert.AreEqual(members.Select(m => m.ObjectId).ToList(), read.Select(m => m.ObjectId).ToList());
            CollectionAssert.AreEqual(members.Select(m => m.SourceGroup).ToList(), read.Select(m => m.SourceGroup).ToList());
            Assert.IsTrue(envelope.Exclusionary);
            Assert.AreEqual(Guid.Parse("22222222-2222-2222-2222-222222222222"), envelope.RunId);
            Assert.AreEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), envelope.Destination.ObjectId);
        }

        [TestMethod]
        public async Task WritesAnOutputTextCompressorCanStillDecompress()
        {
            var members = new[] { new AzureADUser { ObjectId = Guid.NewGuid() } };
            var blob = await WriteToBytesAsync(Envelope(), members);

            var json = TextCompressor.Decompress(Encoding.UTF8.GetString(blob));
            var parsed = JsonSerializer.Deserialize<GroupMembership>(json);

            Assert.AreEqual(1, parsed.SourceMembers.Count);
            Assert.AreEqual(members[0].ObjectId, parsed.SourceMembers[0].ObjectId);
        }

        [TestMethod]
        public async Task ReadsBlobsWrittenInTheExistingLayout()
        {
            var members = Enumerable.Range(1, 50).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var blob = LegacyBlob(Envelope(exclusionary: true), members);

            var (envelope, read) = await ReadAsync(blob);

            CollectionAssert.AreEqual(members.Select(m => m.ObjectId).ToList(), read.Select(m => m.ObjectId).ToList());
            Assert.IsTrue(envelope.Exclusionary, "Exclusionary must survive even when it follows SourceMembers.");
            Assert.AreEqual(Guid.Parse("22222222-2222-2222-2222-222222222222"), envelope.RunId,
                "Every scalar trailing the members must still be bound, not merely tolerated.");
            Assert.AreEqual(Guid.Parse("33333333-3333-3333-3333-333333333333"), envelope.SyncJobId,
                "Every scalar trailing the members must still be bound, not merely tolerated.");
            Assert.AreEqual("[{\"type\":\"SqlMembership\"}]", envelope.Query,
                "Every scalar trailing the members must still be bound, not merely tolerated.");
        }

        [TestMethod]
        public async Task ClassifiesTheStreamBeforeReadingAnyMember()
        {
            var members = Enumerable.Range(1, 200).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var blob = await WriteToBytesAsync(Envelope(exclusionary: true), members);

            using var source = new MemoryStream(blob);
            var membersSeenWhenClassified = -1;
            var reports = 0;
            var seen = 0;

            await foreach (var _ in MembershipStream.ReadAsync(source, e =>
            {
                reports++;
                membersSeenWhenClassified = seen;
            }))
            {
                seen++;
            }

            Assert.AreEqual(1, reports, "The envelope was not reported exactly once.");
            Assert.AreEqual(0, membersSeenWhenClassified,
                "The envelope must be known before any member is yielded, which is the point of writing scalars first.");
        }

        [TestMethod]
        public async Task ClassifiesAnInclusionaryStreamBeforeReadingAnyMember()
        {
            var members = Enumerable.Range(1, 200).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var blob = await WriteToBytesAsync(Envelope(exclusionary: false), members);

            using var source = new MemoryStream(blob);
            var membersSeenWhenClassified = -1;
            var reports = 0;
            var seen = 0;

            await foreach (var _ in MembershipStream.ReadAsync(source, e =>
            {
                reports++;
                membersSeenWhenClassified = seen;
            }))
            {
                seen++;
            }

            Assert.AreEqual(1, reports, "The envelope was not reported exactly once.");
            Assert.AreEqual(0, membersSeenWhenClassified,
                "An inclusionary stream was not classified until after its members had been handed out, so "
                    + "the early report only works for the rarer exclusionary case.");
        }

        [TestMethod]
        public async Task MissingExclusionaryReadsAsFalse()
        {
            var json = "{\"SourceMembers\":[{\"ObjectId\":\"44444444-4444-4444-4444-444444444444\"}],\"RunId\":\"22222222-2222-2222-2222-222222222222\"}";
            var blob = Encoding.UTF8.GetBytes(TextCompressor.Compress(json));

            var (envelope, read) = await ReadAsync(blob);

            Assert.IsFalse(envelope.Exclusionary);
            Assert.AreEqual(1, read.Count);
        }

        [TestMethod]
        public async Task MissingObjectIdIsPreservedAsEmpty()
        {
            // WhenWritingDefault omits Guid.Empty, so the reader must restore it.
            var json = "{\"SourceMembers\":[{\"SourceGroup\":\"55555555-5555-5555-5555-555555555555\"}]}";
            var blob = Encoding.UTF8.GetBytes(TextCompressor.Compress(json));

            var (_, read) = await ReadAsync(blob);

            Assert.AreEqual(1, read.Count, "A member with no ObjectId must still be yielded.");
            Assert.AreEqual(Guid.Empty, read[0].ObjectId);
            Assert.AreEqual(Guid.Parse("55555555-5555-5555-5555-555555555555"), read[0].SourceGroup);
        }

        [TestMethod]
        public async Task AllZeroObjectIdRoundTrips()
        {
            var members = new[] { new AzureADUser { ObjectId = Guid.Empty }, new AzureADUser { ObjectId = Guid.NewGuid() } };
            var blob = await WriteToBytesAsync(Envelope(), members);

            var (_, read) = await ReadAsync(blob);

            Assert.AreEqual(2, read.Count);
            Assert.AreEqual(Guid.Empty, read[0].ObjectId);
        }

        [TestMethod]
        public async Task PreservesMembershipActionIncludingWhenUnset()
        {
            var members = new[]
            {
                new AzureADUser { ObjectId = Guid.NewGuid(), MembershipAction = MembershipAction.Add },
                new AzureADUser { ObjectId = Guid.NewGuid(), MembershipAction = MembershipAction.Remove },
                new AzureADUser { ObjectId = Guid.NewGuid() }
            };

            var blob = await WriteToBytesAsync(Envelope(), members);
            var (_, read) = await ReadAsync(blob);

            Assert.AreEqual(MembershipAction.Add, read[0].MembershipAction);
            Assert.AreEqual(MembershipAction.Remove, read[1].MembershipAction);
            Assert.IsNull(read[2].MembershipAction);
        }

        [TestMethod]
        public async Task PreservesSourceGroups()
        {
            var groups = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var members = new[] { new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroups = groups } };

            var blob = await WriteToBytesAsync(Envelope(), members);
            var (_, read) = await ReadAsync(blob);

            CollectionAssert.AreEqual(groups, read[0].SourceGroups);
        }

        [TestMethod]
        public async Task IgnoresUnknownProperties()
        {
            var json = "{\"Exclusionary\":true,\"Unexpected\":{\"nested\":[1,2,3]},\"SourceMembers\":" +
                       "[{\"ObjectId\":\"44444444-4444-4444-4444-444444444444\",\"Surprise\":\"value\"}],\"Another\":42}";
            var blob = Encoding.UTF8.GetBytes(TextCompressor.Compress(json));

            var (envelope, read) = await ReadAsync(blob);

            Assert.IsTrue(envelope.Exclusionary);
            Assert.AreEqual(1, read.Count);
            Assert.AreEqual(Guid.Parse("44444444-4444-4444-4444-444444444444"), read[0].ObjectId);
        }

        [TestMethod]
        public async Task StripsAUtf8BomEvenWhenSplitAcrossReads()
        {
            var json = "{\"Exclusionary\":true,\"SourceMembers\":[{\"ObjectId\":\"44444444-4444-4444-4444-444444444444\"}]}";
            var withBom = "\uFEFF" + json;
            var blob = Encoding.UTF8.GetBytes(TextCompressor.Compress(withBom));

            using var source = new SingleByteStream(blob);
            GroupMembership envelope = null;
            var members = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(source, e => envelope = e))
            {
                members.Add(member);
            }

            Assert.IsTrue(envelope.Exclusionary);
            Assert.AreEqual(1, members.Count);
        }

        [TestMethod]
        public async Task ReadsCorrectlyWhenTheSourceYieldsOneByteAtATime()
        {
            var members = Enumerable.Range(1, 300).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var blob = await WriteToBytesAsync(Envelope(), members);

            using var source = new SingleByteStream(blob);
            var read = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(source))
            {
                read.Add(member);
            }

            CollectionAssert.AreEqual(members.Select(m => m.ObjectId).ToList(), read.Select(m => m.ObjectId).ToList());
        }

        [TestMethod]
        public async Task ReadsPlainUncompressedJson()
        {
            var json = "{\"SourceMembers\":[{\"ObjectId\":\"44444444-4444-4444-4444-444444444444\"}," +
                       "{\"ObjectId\":\"66666666-6666-6666-6666-666666666666\"}]," +
                       "\"Exclusionary\":true,\"RunId\":\"22222222-2222-2222-2222-222222222222\"}";

            var (envelope, read) = await ReadAsync(Encoding.UTF8.GetBytes(json));

            Assert.AreEqual(2, read.Count);
            Assert.AreEqual(Guid.Parse("44444444-4444-4444-4444-444444444444"), read[0].ObjectId);
            Assert.IsTrue(envelope.Exclusionary);
            Assert.AreEqual(Guid.Parse("22222222-2222-2222-2222-222222222222"), envelope.RunId);
        }

        [TestMethod]
        public async Task ReadsPlainJsonWithABomAndLeadingWhitespace()
        {
            var json = "\uFEFF  \r\n{\"SourceMembers\":[{\"ObjectId\":\"44444444-4444-4444-4444-444444444444\"}]}";

            var (_, read) = await ReadAsync(Encoding.UTF8.GetBytes(json));

            Assert.AreEqual(1, read.Count);
            Assert.AreEqual(Guid.Parse("44444444-4444-4444-4444-444444444444"), read[0].ObjectId);
        }

        [TestMethod]
        public async Task ReadsPlainJsonOneByteAtATime()
        {
            var json = "{\"Exclusionary\":true,\"SourceMembers\":[{\"ObjectId\":\"44444444-4444-4444-4444-444444444444\"}]}";

            using var source = new SingleByteStream(Encoding.UTF8.GetBytes(json));
            GroupMembership envelope = null;
            var members = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(source, e => envelope = e))
            {
                members.Add(member);
            }

            Assert.AreEqual(1, members.Count);
            Assert.IsTrue(envelope.Exclusionary);
        }

        [TestMethod]
        public async Task ReadsARealShapedStagedPartAndItsCompressedCounterpartIdentically()
        {
            var members = Enumerable.Range(1, 100).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var envelope = Envelope(exclusionary: true);
            envelope.SourceMembers = members;

            var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            var json = JsonSerializer.Serialize(envelope, options);

            var (plainEnvelope, plainMembers) = await ReadAsync(Encoding.UTF8.GetBytes(json));
            var (compressedEnvelope, compressedMembers) = await ReadAsync(Encoding.UTF8.GetBytes(TextCompressor.Compress(json)));

            CollectionAssert.AreEqual(
                plainMembers.Select(m => m.ObjectId).ToList(),
                compressedMembers.Select(m => m.ObjectId).ToList());
            Assert.AreEqual(plainEnvelope.Exclusionary, compressedEnvelope.Exclusionary);
            Assert.AreEqual(members.Count, plainMembers.Count);
        }

        [TestMethod]
        public async Task RejectsTruncatedJson()
        {
            var json = "{\"SourceMembers\":[{\"ObjectId\":\"44444444-4444-4444-4444-444444444444\"}";
            var blob = Encoding.UTF8.GetBytes(TextCompressor.Compress(json));

            await AssertRejectsAsync(blob);
        }

        [TestMethod]
        public async Task RejectsMalformedJson()
        {
            var blob = Encoding.UTF8.GetBytes(TextCompressor.Compress("{ this is not valid json }"));

            await AssertRejectsAsync(blob);
        }

        private static async Task AssertRejectsAsync(byte[] blob)
        {
            try
            {
                using var source = new MemoryStream(blob);
                await foreach (var _ in MembershipStream.ReadAsync(source)) { }
            }
            catch (JsonException)
            {
                return;
            }

            Assert.Fail("Expected the malformed blob to be rejected with a JsonException.");
        }

        [TestMethod]
        public async Task PropagatesCancellation()
        {
            var members = Enumerable.Range(1, 5000).Select(_ => new AzureADUser { ObjectId = Guid.NewGuid() }).ToList();
            var blob = await WriteToBytesAsync(Envelope(), members);

            using var cts = new CancellationTokenSource();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            {
                using var source = new MemoryStream(blob);
                await foreach (var _ in MembershipStream.ReadAsync(source, null, cts.Token))
                {
                    cts.Cancel();
                }
            });
        }

        [TestMethod]
        public async Task ReadingLeavesTheSourceOpenAfterSuccess()
        {
            var source = new MemoryStream(await WriteToBytesAsync(Envelope(), new[] { Member(1) }));

            await foreach (var _ in MembershipStream.ReadAsync(source)) { }

            Assert.IsTrue(source.CanRead, "The reader disposed the caller's source stream.");
        }

        [TestMethod]
        public async Task ReadingLeavesTheSourceOpenAfterFailure()
        {
            var source = new MemoryStream(Encoding.UTF8.GetBytes("{\"SourceMembers\":["));
            JsonException failure = null;

            try
            {
                await foreach (var _ in MembershipStream.ReadAsync(source)) { }
            }
            catch (JsonException exception)
            {
                failure = exception;
            }

            Assert.IsNotNull(failure, "The malformed source did not fail.");
            Assert.IsTrue(source.CanRead, "The reader disposed the caller's source stream after a failure.");
        }

        [TestMethod]
        public async Task CallbackFailuresArePropagatedAndLeaveTheSourceOpen()
        {
            var expected = new InvalidOperationException("callback failed");
            var source = new MemoryStream(await WriteToBytesAsync(Envelope(), new[] { Member(1) }));
            InvalidOperationException actual = null;

            try
            {
                await foreach (var _ in MembershipStream.ReadAsync(source, _ => throw expected)) { }
            }
            catch (InvalidOperationException failure)
            {
                actual = failure;
            }

            Assert.AreSame(expected, actual, "The callback failure was replaced or swallowed.");
            Assert.IsTrue(source.CanRead, "The reader disposed the caller's source stream after a callback failure.");
        }

        [TestMethod]
        public async Task WritingLeavesTheDestinationOpenAfterSuccess()
        {
            var destination = new MemoryStream();

            await MembershipStream.WriteAsync(destination, Envelope(), Stream(new[] { Member(1) }));

            Assert.IsTrue(destination.CanWrite, "The writer disposed the caller's destination stream.");
        }

        [TestMethod]
        public async Task WritingLeavesTheDestinationOpenAfterFailure()
        {
            var destination = new MemoryStream();

            await Assert.ThrowsExceptionAsync<JsonException>(
                () => MembershipStream.WriteAsync(destination, Envelope(), Stream(new AzureADUser[] { null })));

            Assert.IsTrue(destination.CanWrite, "The writer disposed the caller's destination stream after a failure.");
        }

        [TestMethod]
        public async Task WritingPropagatesCancellationAndLeavesTheDestinationOpen()
        {
            using var cancellation = new CancellationTokenSource();
            var destination = new MemoryStream();
            OperationCanceledException failure = null;

            try
            {
                await MembershipStream.WriteAsync(
                    destination,
                    Envelope(),
                    CancelAfterOneMember(cancellation),
                    cancellation.Token);
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }

            Assert.IsNotNull(failure, "The cancelled write completed successfully.");
            Assert.IsTrue(destination.CanWrite, "The writer disposed the caller's destination stream after cancellation.");
        }

        [TestMethod]
        public async Task HandlesAnEmptyMemberArray()
        {
            var blob = await WriteToBytesAsync(Envelope(exclusionary: true), Array.Empty<AzureADUser>());
            var (envelope, read) = await ReadAsync(blob);

            Assert.AreEqual(0, read.Count);
            Assert.IsTrue(envelope.Exclusionary);
        }

        [TestMethod]
        public async Task WritingDoesNotModifyTheCallersEnvelope()
        {
            var envelope = Envelope();
            var members = new List<AzureADUser> { Member(1), Member(2) };
            envelope.SourceMembers = members;

            await WriteToBytesAsync(envelope, members);

            Assert.AreSame(members, envelope.SourceMembers, "The writer replaced the caller's member list.");
            Assert.AreEqual(2, envelope.SourceMembers.Count, "The writer altered the caller's member list.");
        }

        [TestMethod]
        public async Task TheEnvelopesOwnMembersNeverReachTheOutput()
        {
            var streamed = new List<AzureADUser> { Member(1), Member(2) };

            var withPopulatedEnvelope = Envelope();
            withPopulatedEnvelope.SourceMembers = new List<AzureADUser> { Member(97), Member(98), Member(99) };

            var withEmptyEnvelope = Envelope();
            withEmptyEnvelope.SourceMembers = new List<AzureADUser>();

            var fromPopulated = await WriteToBytesAsync(withPopulatedEnvelope, streamed);
            var fromEmpty = await WriteToBytesAsync(withEmptyEnvelope, streamed);

            CollectionAssert.AreEqual(fromEmpty, fromPopulated,
                "The envelope's own SourceMembers changed the output; only the streamed members may.");

            var (_, readBack) = await ReadAsync(fromPopulated);
            Assert.AreEqual(2, readBack.Count, "The envelope's members leaked into the blob.");
        }

        [TestMethod]
        public async Task ReadingRejectsASingleTokenLargerThanTheCap()
        {
            var oversized = RawBlobWithQueryOfLength(17 * 1024 * 1024);

            var failure = await Assert.ThrowsExceptionAsync<JsonException>(async () =>
            {
                using var source = new MemoryStream(oversized);
                await foreach (var _ in MembershipStream.ReadAsync(source)) { }
            });

            Assert.IsTrue(failure.Message.Contains("exceeded"), $"Unexpected failure: {failure.Message}");
        }

        [DataRow(35_500, DisplayName = "35.5 KB token")]
        [DataRow(1024 * 1024, DisplayName = "1 MiB token")]
        [TestMethod]
        public async Task ReadingAcceptsLargeButLegitimateTokens(int queryLength)
        {
            var blob = RawBlobWithQueryOfLength(queryLength);

            using var source = new MemoryStream(blob);
            GroupMembership envelope = null;
            var members = new List<AzureADUser>();
            await foreach (var member in MembershipStream.ReadAsync(source, e => envelope = e))
            {
                members.Add(member);
            }

            Assert.IsNotNull(envelope, "The envelope was never reported.");
            Assert.AreEqual(queryLength, envelope.Query.Length, "The Query token was not read intact.");
            Assert.AreEqual(1, members.Count, "The members were not read.");
        }

        private static AzureADUser Member(int seed) => new AzureADUser
        {
            ObjectId = Guid.Parse($"000000{seed:D2}-0000-0000-0000-000000000000")
        };

        private static async IAsyncEnumerable<AzureADUser> CancelAfterOneMember(
            CancellationTokenSource cancellation,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Member(1);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        private static byte[] RawBlobWithQueryOfLength(int queryLength)
        {
            var json = new StringBuilder();
            json.Append("{\"Destination\":{\"ObjectId\":\"11111111-1111-1111-1111-111111111111\"},");
            json.Append("\"RunId\":\"22222222-2222-2222-2222-222222222222\",");
            json.Append("\"Query\":\"").Append('q', queryLength).Append("\",");
            json.Append("\"SourceMembers\":[{\"ObjectId\":\"00000001-0000-0000-0000-000000000000\"}]}");
            return Encoding.UTF8.GetBytes(json.ToString());
        }

        /// <summary>Limits reads to one byte.</summary>
        private sealed class SingleByteStream : Stream
        {
            private readonly byte[] _data;
            private int _position;

            public SingleByteStream(byte[] data) => _data = data;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _data.Length || count == 0) return 0;
                buffer[offset] = _data[_position++];
                return 1;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
