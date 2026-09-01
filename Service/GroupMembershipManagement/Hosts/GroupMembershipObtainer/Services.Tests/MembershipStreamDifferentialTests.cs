// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Tests.FunctionApps
{
    /// <summary>Compares streaming results with the existing deserializer.</summary>
    [TestClass]
    public class MembershipStreamDifferentialTests
    {
        private const int ReaderBufferSize = 64 * 1024;

        private const int MembersExceedingBuffer = 1500;

        private static readonly Guid _destinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid _runId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid _syncJobId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid _memberId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid _decoyId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        public enum Layout
        {
            /// <summary>Every envelope property follows the members.</summary>
            MembersFirst,

            /// <summary>Scalars precede the member array, which is what the new writer emits.</summary>
            ScalarsFirst
        }

        public enum BlobEncoding
        {
            Plain,
            Compressed
        }

        private sealed record Reference(bool Threw, string Failure, IReadOnlyList<string> Members, bool Exclusionary, Guid RunId, string Envelope);

        private sealed record Actual(
            bool Threw,
            string Failure,
            IReadOnlyList<string> Members,
            bool ExclusionaryWhenClassified,
            Guid RunIdWhenClassified,
            Guid DestinationWhenClassified,
            string EnvelopeWhenClassified,
            int MembersYieldedBeforeClassification);

        private static string EnvelopeSnapshot(GroupMembership envelope) =>
            envelope == null
                ? "<none>"
                : $"destination={(envelope.Destination == null ? "-" : envelope.Destination.ObjectId.ToString("D"))};" +
                  $"syncJob={(envelope.SyncJob == null ? "-" : "present")};" +
                  $"runId={envelope.RunId:D};syncJobId={envelope.SyncJobId:D};exclusionary={envelope.Exclusionary};" +
                  $"dryRun={envelope.MembershipObtainerDryRunEnabled};isLast={envelope.IsLastMessage};" +
                  $"index={envelope.MessageIndex};total={envelope.TotalMessageCount};query={envelope.Query ?? "-"};" +
                  $"projected={envelope.ProjectedMemberCount?.ToString() ?? "-"};" +
                  $"toAdd={envelope.TotalMembersToAdd?.ToString() ?? "-"};" +
                  $"toRemove={envelope.TotalMembersToRemove?.ToString() ?? "-"}";

        #region Payload construction

        private static string MemberJson(Guid objectId, Guid? sourceGroup = null, int? action = null, string extra = null)
        {
            var parts = new List<string> { $"\"ObjectId\":\"{objectId:D}\"" };
            if (sourceGroup.HasValue) parts.Add($"\"SourceGroup\":\"{sourceGroup.Value:D}\"");
            if (action.HasValue) parts.Add($"\"MembershipAction\":{action.Value}");
            if (extra != null) parts.Add(extra);
            return "{" + string.Join(",", parts) + "}";
        }

        private static string BuildJson(Layout layout, IEnumerable<string> members, bool exclusionary, string extraScalars = null)
        {
            var array = "\"SourceMembers\":[" + string.Join(",", members) + "]";
            var scalars = new List<string>
            {
                $"\"Destination\":{{\"ObjectId\":\"{_destinationId:D}\"}}",
                $"\"RunId\":\"{_runId:D}\"",
                $"\"SyncJobId\":\"{_syncJobId:D}\"",
                $"\"Exclusionary\":{(exclusionary ? "true" : "false")}"
            };
            if (extraScalars != null) scalars.Add(extraScalars);

            var ordered = layout == Layout.MembersFirst
                ? new[] { array }.Concat(scalars)
                : scalars.Concat(new[] { array });

            return "{" + string.Join(",", ordered) + "}";
        }

        private static byte[] Encode(string json, BlobEncoding encoding) =>
            Encoding.UTF8.GetBytes(encoding == BlobEncoding.Compressed ? TextCompressor.Compress(json) : json);

        private static List<string> GeneratedMembers(int count, Random random)
        {
            var members = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                members.Add(MemberJson(NextGuid(random), NextGuid(random)));
            }
            return members;
        }

        private static Guid NextGuid(Random random)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            return new Guid(bytes);
        }

        #endregion

        #region Permuted payload construction

        private enum RootKind
        {
            Scalar,

            Object,

            Members,

            Unknown
        }

        private sealed record RootProperty(string Name, string Json, RootKind Kind);

        private sealed record PermutedPayload(string Json, string Order, bool ReportsEarly, bool MustBeRefused, string OffendingProperty);

        private static List<RootProperty> EnvelopeCatalog(Random random) => new()
        {
            new("RunId", $"\"{NextGuid(random):D}\"", RootKind.Scalar),
            new("SyncJobId", $"\"{NextGuid(random):D}\"", RootKind.Scalar),
            new("Exclusionary", random.Next(2) == 0 ? "true" : "false", RootKind.Scalar),
            new("MembershipObtainerDryRunEnabled", random.Next(2) == 0 ? "true" : "false", RootKind.Scalar),
            new("IsLastMessage", random.Next(2) == 0 ? "true" : "false", RootKind.Scalar),
            new("MessageIndex", random.Next(1, 50).ToString(CultureInfo.InvariantCulture), RootKind.Scalar),
            new("TotalMessageCount", random.Next(1, 50).ToString(CultureInfo.InvariantCulture), RootKind.Scalar),
            new("ProjectedMemberCount", random.Next(0, 5000).ToString(CultureInfo.InvariantCulture), RootKind.Scalar),
            new("TotalMembersToAdd", random.Next(0, 5000).ToString(CultureInfo.InvariantCulture), RootKind.Scalar),
            new("TotalMembersToRemove", random.Next(0, 5000).ToString(CultureInfo.InvariantCulture), RootKind.Scalar),
            new("Query", $"\"query-{random.Next(1000)}\"", RootKind.Scalar),
            new("Destination", $"{{\"ObjectId\":\"{NextGuid(random):D}\"}}", RootKind.Object),
            new("SyncJob", "{}", RootKind.Object)
        };

        /// <summary>Builds a random property order and its expected result.</summary>
        private static PermutedPayload BuildPermutedPayload(Random random, int memberCount)
        {
            var chosen = EnvelopeCatalog(random).Where(_ => random.Next(4) > 0).ToList();

            chosen.Add(new RootProperty(
                "SourceMembers",
                "[" + string.Join(",", GeneratedMembers(memberCount, random)) + "]",
                RootKind.Members));

            var unknownCount = random.Next(0, 3);
            for (var i = 0; i < unknownCount; i++)
            {
                chosen.Add(new RootProperty($"NotOnTheModel{i}", UnknownValue(random), RootKind.Unknown));
            }

            for (var i = chosen.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (chosen[i], chosen[j]) = (chosen[j], chosen[i]);
            }

            // Keep accepted and rejected layouts represented.
            switch (random.Next(3))
            {
                case 0:
                    break;
                case 1:
                    MoveMembers(chosen, random, toEnd: true);
                    break;
                default:
                    MoveMembers(chosen, random, toEnd: false);
                    break;
            }

            var membersAt = chosen.FindIndex(p => p.Kind == RootKind.Members);
            var reportsEarly = chosen.Take(membersAt).Any(p => p.Kind == RootKind.Scalar);
            var trailing = chosen.Skip(membersAt + 1).FirstOrDefault(p => p.Kind == RootKind.Scalar || p.Kind == RootKind.Object);

            return new PermutedPayload(
                "{" + string.Join(",", chosen.Select(p => $"\"{p.Name}\":{p.Json}")) + "}",
                string.Join(",", chosen.Select(p => p.Name)),
                reportsEarly,
                reportsEarly && trailing != null,
                trailing?.Name);
        }

        private static void MoveMembers(List<RootProperty> properties, Random random, bool toEnd)
        {
            var at = properties.FindIndex(p => p.Kind == RootKind.Members);
            var members = properties[at];
            properties.RemoveAt(at);

            if (toEnd)
            {
                properties.Add(members);
                return;
            }

            var firstScalar = properties.FindIndex(p => p.Kind == RootKind.Scalar);
            properties.Insert(firstScalar < 0 ? properties.Count : random.Next(firstScalar + 1), members);
        }

        private static string UnknownValue(Random random) => random.Next(5) switch
        {
            0 => "null",
            1 => "123",
            2 => "\"text\"",
            3 => "{\"nested\":{\"deeper\":[1,2,3]}}",
            _ => "[{\"ObjectId\":\"" + NextGuid(random).ToString("D") + "\"}]"
        };

        #endregion

        #region Reference and actual

        /// <summary>Reads compressed or plain JSON with the existing deserializer.</summary>
        private static Reference ReadWithProductionDeserializer(byte[] blob)
        {
            try
            {
                var content = Encoding.UTF8.GetString(blob);
                string json;
                try
                {
                    json = TextCompressor.Decompress(content);
                }
                catch (FormatException)
                {
                    json = content;
                }

                var membership = JsonSerializer.Deserialize<GroupMembership>(json);
                var members = (membership.SourceMembers ?? new List<AzureADUser>()).Select(Describe).ToList();
                return new Reference(false, null, members, membership.Exclusionary, membership.RunId, EnvelopeSnapshot(membership));
            }
            catch (Exception ex)
            {
                return new Reference(true, ex.GetType().Name, Array.Empty<string>(), false, Guid.Empty, "<threw>");
            }
        }

        private static async Task<Actual> ReadWithStreamAsync(byte[] blob, int chunkSize)
        {
            var members = new List<string>();
            var exclusionary = false;
            var runId = Guid.Empty;
            var destination = Guid.Empty;
            var envelopeSnapshot = "<never classified>";
            var yieldedBeforeClassification = -1;

            try
            {
                using var source = new ChunkedStream(blob, chunkSize);
                await foreach (var member in MembershipStream.ReadAsync(source, envelope =>
                {
                    // Capture exactly what the callback received.
                    exclusionary = envelope.Exclusionary;
                    runId = envelope.RunId;
                    destination = envelope.Destination?.ObjectId ?? Guid.Empty;
                    envelopeSnapshot = EnvelopeSnapshot(envelope);
                    yieldedBeforeClassification = members.Count;
                }))
                {
                    members.Add(Describe(member));
                }

                return new Actual(false, null, members, exclusionary, runId, destination, envelopeSnapshot, yieldedBeforeClassification);
            }
            catch (Exception ex)
            {
                return new Actual(true, ex.GetType().Name, members, exclusionary, runId, destination, envelopeSnapshot, yieldedBeforeClassification);
            }
        }

        private static string Describe(AzureADUser member) =>
            $"{member.ObjectId:D}|{member.SourceGroup:D}|" +
            $"{(member.SourceGroups == null ? "-" : string.Join(",", member.SourceGroups))}|" +
            $"{(member.MembershipAction.HasValue ? ((int)member.MembershipAction.Value).ToString() : "-")}";

        /// <summary>Limits every read to <c>chunkSize</c> bytes.</summary>
        private sealed class ChunkedStream : Stream
        {
            private readonly byte[] _content;
            private readonly int _chunkSize;
            private int _position;

            public ChunkedStream(byte[] content, int chunkSize)
            {
                _content = content;
                _chunkSize = Math.Max(1, chunkSize);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _content.Length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count) => Take(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer) => Take(buffer);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default) =>
                new ValueTask<int>(Take(buffer.Span));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken) =>
                Task.FromResult(Take(buffer.AsSpan(offset, count)));

            private int Take(Span<byte> destination)
            {
                var take = Math.Min(Math.Min(destination.Length, _chunkSize), _content.Length - _position);
                if (take <= 0) return 0;
                _content.AsSpan(_position, take).CopyTo(destination);
                _position += take;
                return take;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        #endregion

        #region Comparison

        /// <summary>Compares one streaming read with the existing deserializer.</summary>
        private static void AssertMembersMatchProduction(byte[] blob, Actual actual, string description, bool expectSuccess = true)
        {
            var expected = ReadWithProductionDeserializer(blob);

            if (expectSuccess)
            {
                Assert.IsFalse(
                    expected.Threw,
                    $"{description}: the deserializer rejected a payload this test builds as valid, with {expected.Failure}. " +
                    "The generator or the harness is wrong here, not the reader.");
                Assert.IsFalse(
                    actual.Threw,
                    $"{description}: the reader rejected a payload the deserializer accepts, with {actual.Failure}.");
            }

            Assert.AreEqual(
                expected.Threw,
                actual.Threw,
                $"{description}: the reader and the deserializer disagree on whether this input is valid. " +
                $"deserializer threw={expected.Threw} ({expected.Failure ?? "none"}), reader threw={actual.Threw} ({actual.Failure ?? "none"}).");

            if (expected.Threw)
            {
                Console.WriteLine($"{description}: both rejected the input. deserializer={expected.Failure}, reader={actual.Failure}.");
                return;
            }

            Assert.AreEqual(
                expected.Members.Count,
                actual.Members.Count,
                $"{description}: member count differs. deserializer={expected.Members.Count}, reader={actual.Members.Count}.");

            for (var i = 0; i < expected.Members.Count; i++)
            {
                Assert.AreEqual(
                    expected.Members[i],
                    actual.Members[i],
                    $"{description}: member {i} differs. Format is ObjectId|SourceGroup|SourceGroups|MembershipAction.");
            }

            Assert.AreEqual(
                expected.Envelope,
                actual.EnvelopeWhenClassified,
                $"{description}: the envelope the reader reported differs from the deserializer's.");
        }

        private static string DivergenceOnValidPayload(Reference expected, Actual actual, string description)
        {
            if (expected.Threw && actual.Threw)
                return $"{description}: BOTH rejected a payload built as valid (deserializer={expected.Failure}, reader={actual.Failure})";
            if (expected.Threw)
                return $"{description}: the deserializer rejected a payload built as valid ({expected.Failure})";
            if (actual.Threw)
                return $"{description}: the reader rejected a payload the deserializer accepts ({actual.Failure})";
            if (!expected.Members.SequenceEqual(actual.Members))
                return $"{description}: members differ";
            return null;
        }

        #endregion

        #region Structural matrix

        public static IEnumerable<object[]> StructuralCases()
        {
            foreach (var layout in new[] { Layout.MembersFirst, Layout.ScalarsFirst })
            {
                foreach (var encoding in new[] { BlobEncoding.Plain, BlobEncoding.Compressed })
                {
                    foreach (var exclusionary in new[] { true, false })
                    {
                        foreach (var chunkSize in new[] { 1, 7, int.MaxValue })
                        {
                            yield return new object[] { layout, encoding, false, exclusionary, chunkSize };
                        }

                        foreach (var chunkSize in new[] { 8 * 1024, ReaderBufferSize, int.MaxValue })
                        {
                            yield return new object[] { layout, encoding, true, exclusionary, chunkSize };
                        }
                    }
                }
            }
        }

        [TestMethod]
        [DynamicData(nameof(StructuralCases), DynamicDataSourceType.Method)]
        public async Task MemberDataMatchesProductionDeserializer(Layout layout, BlobEncoding encoding, bool large, bool exclusionary, int chunkSize)
        {
            var random = new Random(20260824);
            var members = GeneratedMembers(large ? MembersExceedingBuffer : 3, random);
            var blob = Encode(BuildJson(layout, members, exclusionary), encoding);

            var actual = await ReadWithStreamAsync(blob, chunkSize);

            AssertMembersMatchProduction(blob, actual, $"{layout}/{encoding}/large={large}/chunk={chunkSize}");
        }

        [TestMethod]
        [DynamicData(nameof(StructuralCases), DynamicDataSourceType.Method)]
        public async Task ClassificationReportsTheCorrectExclusionaryValue(Layout layout, BlobEncoding encoding, bool large, bool exclusionary, int chunkSize)
        {
            var random = new Random(20260824);
            var members = GeneratedMembers(large ? MembersExceedingBuffer : 3, random);
            var blob = Encode(BuildJson(layout, members, exclusionary), encoding);

            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsFalse(actual.Threw, $"{layout}/{encoding}/large={large}/chunk={chunkSize}: reading threw unexpectedly.");

            Assert.AreEqual(
                exclusionary,
                actual.ExclusionaryWhenClassified,
                $"{layout}/{encoding}/large={large}/chunk={chunkSize}: the reader classified this stream as " +
                $"Exclusionary={actual.ExclusionaryWhenClassified} when it is {exclusionary}. It reported this after " +
                $"yielding {actual.MembersYieldedBeforeClassification} of {members.Count} members. An exclusionary " +
                "part read as inclusionary turns removals into additions.");
        }

        [TestMethod]
        [DynamicData(nameof(StructuralCases), DynamicDataSourceType.Method)]
        public async Task ClassificationReportsTheCorrectRunId(Layout layout, BlobEncoding encoding, bool large, bool exclusionary, int chunkSize)
        {
            var random = new Random(20260824);
            var members = GeneratedMembers(large ? MembersExceedingBuffer : 3, random);
            var blob = Encode(BuildJson(layout, members, exclusionary), encoding);

            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsFalse(actual.Threw, $"{layout}/{encoding}/large={large}/chunk={chunkSize}: reading threw unexpectedly.");
            Assert.AreEqual(
                _runId,
                actual.RunIdWhenClassified,
                $"{layout}/{encoding}/large={large}/chunk={chunkSize}: RunId was reported as " +
                $"{actual.RunIdWhenClassified} after yielding {actual.MembersYieldedBeforeClassification} of " +
                $"{members.Count} members.");
        }

        public static IEnumerable<object[]> InvalidDeclaredPropertyShapes()
        {
            yield return new object[] { "root array", "[{\"SourceMembers\":[]}]" };
            yield return new object[] { "Destination array", "{\"Destination\":[],\"SourceMembers\":[]}" };
            yield return new object[] { "Destination string", "{\"Destination\":\"wrong\",\"SourceMembers\":[]}" };
            yield return new object[] { "SyncJob array", "{\"SyncJob\":[],\"SourceMembers\":[]}" };
            yield return new object[] { "SyncJob number", "{\"SyncJob\":1,\"SourceMembers\":[]}" };
            yield return new object[] { "SourceMembers object", "{\"SourceMembers\":{}}" };
            yield return new object[] { "SourceMembers number", "{\"SourceMembers\":1}" };
            yield return new object[] { "SourceMembers string", "{\"SourceMembers\":\"wrong\"}" };
        }

        public static IEnumerable<object[]> DeclaredPropertyTokenCases()
        {
            var properties = new[]
            {
                "Destination",
                "SourceMembers",
                "RunId",
                "SyncJobId",
                "MembershipObtainerDryRunEnabled",
                "Exclusionary",
                "Query",
                "SyncJob",
                "ProjectedMemberCount",
                "TotalMembersToAdd",
                "TotalMembersToRemove",
                "MessageIndex",
                "IsLastMessage",
                "TotalMessageCount"
            };
            var tokens = new[] { "null", "true", "1", "\"text\"", "{}", "[]" };

            foreach (var property in properties)
            {
                foreach (var token in tokens)
                {
                    var members = string.Equals(property, "SourceMembers", StringComparison.Ordinal)
                        ? string.Empty
                        : ",\"SourceMembers\":[]";
                    yield return new object[] { property, token, $"{{\"{property}\":{token}{members}}}" };
                }
            }
        }

        [TestMethod]
        [DynamicData(nameof(DeclaredPropertyTokenCases), DynamicDataSourceType.Method)]
        public async Task EveryDeclaredPropertyTokenShapeIsHandledIntentionally(
            string property,
            string token,
            string json)
        {
            var blob = Encode(json, BlobEncoding.Plain);
            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);
            var description = $"{property}={token}";

            if (string.Equals(property, "SourceMembers", StringComparison.Ordinal)
                && string.Equals(token, "null", StringComparison.Ordinal))
            {
                Assert.IsFalse(expected.Threw, "the production deserializer no longer accepts null SourceMembers.");
                Assert.IsTrue(
                    actual.Threw,
                    "null SourceMembers was accepted as an empty membership instead of being rejected as malformed.");
                return;
            }

            if (expected.Threw)
            {
                Assert.IsTrue(
                    actual.Threw,
                    $"{description}: the production deserializer rejects this shape, but the reader accepted it.");
                return;
            }

            AssertMembersMatchProduction(blob, actual, description);
        }

        [TestMethod]
        [DynamicData(nameof(InvalidDeclaredPropertyShapes), DynamicDataSourceType.Method)]
        public async Task InvalidDeclaredPropertyShapesAreRejectedLikeTheDeserializer(string name, string json)
        {
            var blob = Encode(json, BlobEncoding.Plain);
            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsTrue(
                expected.Threw,
                $"{name}: the test is invalid because the production deserializer accepted the payload.");
            Assert.IsTrue(
                actual.Threw,
                $"{name}: the production deserializer rejects this payload, but the streaming reader accepted it.");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(7)]
        [DataRow(int.MaxValue)]
        public async Task NullThenObjectUsesTheLastDestinationValue(int chunkSize)
        {
            var json = "{" +
                "\"Destination\":null," +
                $"\"Destination\":{{\"ObjectId\":\"{_destinationId:D}\"}}," +
                "\"SourceMembers\":[]" +
                "}";
            var blob = Encode(json, BlobEncoding.Plain);
            var actual = await ReadWithStreamAsync(blob, chunkSize);

            AssertMembersMatchProduction(blob, actual, $"Destination null then object, chunk={chunkSize}");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(7)]
        [DataRow(int.MaxValue)]
        public async Task ObjectThenNullUsesTheLastDestinationValue(int chunkSize)
        {
            var json = "{" +
                $"\"Destination\":{{\"ObjectId\":\"{_destinationId:D}\"}}," +
                "\"Destination\":null," +
                "\"SourceMembers\":[]" +
                "}";
            var blob = Encode(json, BlobEncoding.Plain);
            var actual = await ReadWithStreamAsync(blob, chunkSize);

            AssertMembersMatchProduction(blob, actual, $"Destination object then null, chunk={chunkSize}");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(7)]
        [DataRow(int.MaxValue)]
        public async Task NullThenArrayUsesTheLastMembersValue(int chunkSize)
        {
            var json = "{" +
                "\"SourceMembers\":null," +
                $"\"SourceMembers\":[{MemberJson(_memberId)}]" +
                "}";
            var blob = Encode(json, BlobEncoding.Plain);
            var actual = await ReadWithStreamAsync(blob, chunkSize);

            AssertMembersMatchProduction(blob, actual, $"SourceMembers null then array, chunk={chunkSize}");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(7)]
        [DataRow(int.MaxValue)]
        public async Task NullAfterStreamedMembersIsRejected(int chunkSize)
        {
            var json = "{" +
                $"\"SourceMembers\":[{MemberJson(_memberId)}]," +
                "\"SourceMembers\":null" +
                "}";
            var blob = Encode(json, BlobEncoding.Plain);
            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsFalse(expected.Threw, $"chunk={chunkSize}: the production deserializer rejected the payload.");
            Assert.AreEqual(0, expected.Members.Count, $"chunk={chunkSize}: the deserializer did not apply its last-value rule.");
            Assert.IsTrue(
                actual.Threw,
                $"chunk={chunkSize}: the reader yielded members from the first array and then silently accepted a null replacement.");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(7)]
        [DataRow(int.MaxValue)]
        public async Task NullObjectPropertyAfterEarlyClassificationIsRejected(int chunkSize)
        {
            var json = "{" +
                $"\"Destination\":{{\"ObjectId\":\"{_destinationId:D}\"}}," +
                "\"Exclusionary\":true," +
                "\"SourceMembers\":[]," +
                "\"Destination\":null" +
                "}";
            var blob = Encode(json, BlobEncoding.Plain);
            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsFalse(expected.Threw, $"chunk={chunkSize}: the production deserializer rejected the payload.");
            Assert.IsTrue(
                actual.Threw,
                $"chunk={chunkSize}: the reader reported Destination={actual.DestinationWhenClassified} and then accepted a null replacement.");
        }

        [TestMethod]
        public async Task DecoyScalarsInAnUnknownNestedObjectDoNotChangeClassification()
        {
            var json = BuildJson(
                Layout.ScalarsFirst,
                new[] { MemberJson(_memberId) },
                true,
                $"\"Decoy\":{{\"Exclusionary\":false,\"RunId\":\"{_decoyId:D}\",\"SyncJobId\":\"{_decoyId:D}\",\"ObjectId\":\"{_decoyId:D}\"}}");
            var blob = Encode(json, BlobEncoding.Plain);

            var reference = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsFalse(actual.Threw, "reading threw unexpectedly.");
            Assert.AreEqual(
                reference.Exclusionary,
                actual.ExclusionaryWhenClassified,
                "a decoy Exclusionary one level down overwrote the envelope's own value.");
            Assert.AreEqual(
                reference.RunId,
                actual.RunIdWhenClassified,
                "a decoy RunId one level down overwrote the envelope's own value.");
            Assert.AreEqual(
                _destinationId,
                actual.DestinationWhenClassified,
                "a decoy ObjectId one level down overwrote the destination group.");
        }

        [TestMethod]
        public async Task AnObjectIdInsideAnUnrelatedArrayDoesNotReplaceTheMember()
        {
            var member = MemberJson(_memberId, extra: $"\"Also\":[{{\"ObjectId\":\"{_decoyId:D}\"}}]");
            var blob = Encode(BuildJson(Layout.ScalarsFirst, new[] { member }, false), BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            AssertMembersMatchProduction(blob, actual, "ObjectId inside an unrelated array");
        }

        [TestMethod]
        public async Task AnArrayValuedObjectIdIsNotTreatedAsTheIdentity()
        {
            var blob = Encode(
                BuildJson(Layout.ScalarsFirst, new[] { $"{{\"ObjectId\":[\"{_decoyId:D}\"]}}" }, false),
                BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            AssertMembersMatchProduction(blob, actual, "array-valued ObjectId", expectSuccess: false);
        }

        [DataTestMethod]
        [DataRow("{\"RunId\":\"00000000-0000-0000-0000-000000000001\"}", "no SourceMembers property at all")]
        [DataRow("{\"sourcemembers\":[]}", "SourceMembers in lower case")]
        [DataRow("{\"SOURCEMEMBERS\":[]}", "SourceMembers in upper case")]
        [DataRow("{\"Sourcemembers\":[]}", "SourceMembers with only the first letter capitalised")]
        public async Task AMissingOrMiscasedMembersArrayIsRejected(string json, string description)
        {
            var blob = Encode(json, BlobEncoding.Plain);

            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsFalse(expected.Threw, $"{description}: the production deserializer no longer accepts this shape.");
            Assert.IsTrue(
                actual.Threw,
                $"{description}: the reader treated a missing SourceMembers array as an empty membership.");
        }

        [TestMethod]
        public async Task AnExplicitlyEmptyMembersArrayIsStillAccepted()
        {
            var blob = Encode("{\"SourceMembers\":[]}", BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsFalse(
                actual.Threw,
                $"an empty SourceMembers array is the legitimate way to express an empty membership, " +
                $"but the reader rejected it with {actual.Failure}.");
            Assert.AreEqual(0, actual.Members.Count, "an empty array yielded members.");
        }

        /// <summary>
        /// Released blobs place Destination before SourceMembers and the remaining fields after it.
        /// </summary>
        [DataTestMethod]
        [DataRow(BlobEncoding.Plain, 1)]
        [DataRow(BlobEncoding.Plain, 7)]
        [DataRow(BlobEncoding.Plain, int.MaxValue)]
        [DataRow(BlobEncoding.Compressed, 13)]
        [DataRow(BlobEncoding.Compressed, int.MaxValue)]
        public async Task TheLayoutProductionAlreadyWroteStillReads(BlobEncoding encoding, int chunkSize)
        {
            var blob = Encode(LegacyDeclarationOrderJson(), encoding);

            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsFalse(
                expected.Threw,
                "the deserializer rejected the layout production already writes, so this test no longer " +
                "models a real blob and its premise needs revisiting.");
            Assert.IsFalse(
                actual.Threw,
                $"the reader rejected the layout every stored blob already has, failing with {actual.Failure}. " +
                "Every membership blob written before the property order changed would be unreadable.");
            CollectionAssert.AreEqual(
                expected.Members.ToList(),
                actual.Members.ToList(),
                "the reader and the deserializer disagree on the members of a blob in the released layout.");
            Assert.AreEqual(
                expected.Envelope,
                actual.EnvelopeWhenClassified,
                "the reader and the deserializer disagree on the envelope of a blob in the released layout.");
        }

        private static string LegacyDeclarationOrderJson()
        {
            var members = string.Join(",", new[]
            {
                MemberJson(_memberId, _destinationId),
                MemberJson(_decoyId, _destinationId)
            });

            return "{" +
                $"\"Destination\":{{\"ObjectId\":\"{_destinationId:D}\"}}," +
                $"\"SourceMembers\":[{members}]," +
                $"\"RunId\":\"{_runId:D}\"," +
                $"\"SyncJobId\":\"{_syncJobId:D}\"," +
                "\"MembershipObtainerDryRunEnabled\":false," +
                "\"Exclusionary\":true," +
                "\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\"}]\"," +
                $"\"SyncJob\":{{\"Id\":\"{_syncJobId:D}\",\"TargetOfficeGroupId\":\"{_destinationId:D}\"}}," +
                "\"ProjectedMemberCount\":2," +
                "\"TotalMembersToAdd\":1," +
                "\"TotalMembersToRemove\":0," +
                "\"MessageIndex\":1," +
                "\"IsLastMessage\":true," +
                "\"TotalMessageCount\":1" +
                "}";
        }

        [TestMethod]
        public async Task ANullSourceMembersIsRejected()
        {
            var blob = Encode($"{{\"SourceMembers\":null,\"Unrelated\":[{{\"ObjectId\":\"{_decoyId:D}\"}}]}}", BlobEncoding.Plain);

            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsFalse(expected.Threw, "the production deserializer no longer accepts null SourceMembers.");
            Assert.IsTrue(actual.Threw, "null SourceMembers was treated as an empty membership.");
            Assert.AreEqual(0, actual.Members.Count, "the unrelated array was mistaken for the member list.");
        }

        [TestMethod]
        public async Task ANullMemberIsRejectedInsteadOfBeingYielded()
        {
            const string json = "{\"SourceMembers\":[null]}";
            var blob = Encode(json, BlobEncoding.Plain);

            var expected = JsonSerializer.Deserialize<GroupMembership>(json);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.AreEqual(1, expected.SourceMembers.Count, "the production deserializer no longer accepts a null member.");
            Assert.IsNull(expected.SourceMembers[0], "the production deserializer changed the null member.");
            Assert.IsTrue(actual.Threw, "the reader yielded a null member from the membership array.");
        }

        /// <summary>A failed read may yield members; callers must discard them.</summary>
        [TestMethod]
        public async Task ATruncatedBlobFailsEvenAfterYieldingMembers()
        {
            var truncated = $"{{\"SourceMembers\":[{MemberJson(_memberId)},{{\"ObjectId\":\"{_decoyId:D}";
            var blob = Encode(truncated, BlobEncoding.Plain);

            var reference = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsTrue(reference.Threw, "the deserializer accepted a truncated blob.");
            Assert.IsTrue(
                actual.Threw,
                $"the reader accepted a truncated blob and returned {actual.Members.Count} members. " +
                "A partial read must fail so the consumer can discard the part.");
        }

        #endregion

        #region Value handling

        public static IEnumerable<object[]> ValueCases()
        {
            yield return new object[] { "valid ObjectId", $"{{\"ObjectId\":\"{_memberId:D}\"}}", false };
            yield return new object[] { "ObjectId omitted", "{}", false };
            yield return new object[] { "ObjectId explicitly all zero", "{\"ObjectId\":\"00000000-0000-0000-0000-000000000000\"}", false };
            yield return new object[] { "ObjectId null", "{\"ObjectId\":null}", true };
            yield return new object[] { "ObjectId a number", "{\"ObjectId\":123}", true };
            yield return new object[] { "ObjectId an empty string", "{\"ObjectId\":\"\"}", true };
            yield return new object[] { "ObjectId malformed", "{\"ObjectId\":\"not-a-guid\"}", true };
            yield return new object[] { "MembershipAction as a number", $"{{\"ObjectId\":\"{_memberId:D}\",\"MembershipAction\":2}}", false };
            yield return new object[] { "MembershipAction explicitly null", $"{{\"ObjectId\":\"{_memberId:D}\",\"MembershipAction\":null}}", false };
            yield return new object[] { "MembershipAction as a string", $"{{\"ObjectId\":\"{_memberId:D}\",\"MembershipAction\":\"Add\"}}", true };
            yield return new object[] { "SourceGroup malformed", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroup\":\"nope\"}}", true };
            yield return new object[] { "SourceGroup explicitly null", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroup\":null}}", true };
            yield return new object[] { "SourceGroups explicitly null", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroups\":null}}", false };
            yield return new object[] { "SourceGroups empty", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroups\":[]}}", false };
            yield return new object[] { "SourceGroups populated", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroups\":[\"{_runId:D}\",\"{_syncJobId:D}\"]}}", false };
            yield return new object[] { "SourceGroups with an invalid entry", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroups\":[\"nope\"]}}", true };
            yield return new object[] { "SourceGroups with a null entry", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroups\":[null]}}", true };
            yield return new object[] { "SourceGroups with a structured entry", $"{{\"ObjectId\":\"{_memberId:D}\",\"SourceGroups\":[{{}}]}}", true };
        }

        [TestMethod]
        [DynamicData(nameof(ValueCases), DynamicDataSourceType.Method)]
        public async Task ValueHandlingMatchesProductionDeserializer(string name, string memberBody, bool shouldBeRejected)
        {
            var blob = Encode(BuildJson(Layout.ScalarsFirst, new[] { memberBody }, false), BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            AssertMembersMatchProduction(blob, actual, name, expectSuccess: !shouldBeRejected);
            Assert.AreEqual(
                shouldBeRejected,
                actual.Threw,
                $"{name}: expected rejected={shouldBeRejected} but the reader threw={actual.Threw}. " +
                "Only an omitted or all-zero ObjectId may become Guid.Empty; a present but invalid value " +
                "must be rejected, because Guid.Empty is a signal the updater acts on.");
        }

        [TestMethod]
        public async Task NestedObjectInsideAMemberDoesNotReplaceItsIdentity()
        {
            var member = MemberJson(_memberId, extra: $"\"Properties\":{{\"ObjectId\":\"{_decoyId:D}\"}}");
            var blob = Encode(BuildJson(Layout.ScalarsFirst, new[] { member }, false), BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            AssertMembersMatchProduction(blob, actual, "nested object inside a member");
        }

        [TestMethod]
        public async Task NestedObjectInTheEnvelopeDoesNotReplaceEnvelopeValues()
        {
            var json = BuildJson(
                Layout.ScalarsFirst,
                new[] { MemberJson(_memberId) },
                false,
                $"\"Unrelated\":{{\"ObjectId\":\"{_decoyId:D}\",\"RunId\":\"{_decoyId:D}\"}}");
            var blob = Encode(json, BlobEncoding.Plain);

            var reference = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsFalse(actual.Threw, "reading threw unexpectedly.");
            Assert.AreEqual(
                reference.RunId,
                actual.RunIdWhenClassified,
                "an unrelated nested object changed an envelope value.");
        }

        [TestMethod]
        [DataRow(1, DisplayName = "one byte at a time")]
        [DataRow(7, DisplayName = "seven bytes at a time")]
        [DataRow(int.MaxValue, DisplayName = "whole payload in one read")]
        public async Task AScalarArrivingAfterAnEarlyDescriptionIsRejected(int chunkSize)
        {
            var blob = Encode(
                $"{{\"Exclusionary\":true,\"SourceMembers\":[{MemberJson(_memberId)}],\"RunId\":\"{_runId:D}\"}}",
                BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsTrue(
                actual.Threw,
                $"chunk={chunkSize}: the reader described the stream and then read a RunId that would have changed " +
                $"the description, reporting {actual.RunIdWhenClassified} instead of failing.");
        }

        [TestMethod]
        [DataRow(1, DisplayName = "one byte at a time")]
        [DataRow(5, DisplayName = "five bytes at a time")]
        [DataRow(int.MaxValue, DisplayName = "whole payload in one read")]
        public async Task ARepeatedClassificationAfterAnEarlyDescriptionIsRejected(int chunkSize)
        {
            var blob = Encode(
                $"{{\"Exclusionary\":true,\"RunId\":\"{_runId:D}\",\"SourceMembers\":[{MemberJson(_memberId)}],\"Exclusionary\":false}}",
                BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsTrue(
                actual.Threw,
                $"chunk={chunkSize}: the reader reported Exclusionary={actual.ExclusionaryWhenClassified} and then read " +
                "a later value that contradicts it.");
        }

        [TestMethod]
        [DataRow(1, DisplayName = "one byte at a time")]
        [DataRow(7, DisplayName = "seven bytes at a time")]
        [DataRow(int.MaxValue, DisplayName = "whole payload in one read")]
        public async Task AnObjectValuedPropertyArrivingAfterAnEarlyDescriptionIsRejected(int chunkSize)
        {
            var blob = Encode(
                $"{{\"Exclusionary\":true,\"SourceMembers\":[{MemberJson(_memberId)}],\"Destination\":{{\"ObjectId\":\"{_destinationId:D}\"}}}}",
                BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsTrue(
                actual.Threw,
                $"chunk={chunkSize}: the reader described the stream with Destination=" +
                $"{actual.DestinationWhenClassified} and then read a Destination of {_destinationId}, " +
                "changing the description after handing it over.");
        }

        [TestMethod]
        public async Task TheWriterNeverEmitsAPropertyTwiceInTheSameObject()
        {
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = _destinationId },
                RunId = _runId,
                SyncJobId = _syncJobId,
                Exclusionary = true,
                MembershipObtainerDryRunEnabled = true,
                IsLastMessage = true,
                MessageIndex = 7,
                TotalMessageCount = 9,
                Query = "[{\"type\":\"SqlMembership\"}]",
                SourceMembers = new List<AzureADUser>()
            };

            var members = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = _memberId, SourceGroup = _runId, MembershipAction = MembershipAction.Add },
                new AzureADUser { ObjectId = Guid.Empty, SourceGroups = new List<Guid> { _syncJobId, Guid.Empty } },
                new AzureADUser { ObjectId = _decoyId, MembershipAction = MembershipAction.Remove }
            };

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, AsAsync(members));

            var json = TextCompressor.Decompress(Encoding.UTF8.GetString(destination.ToArray()));
            var repeated = RepeatedPropertyNames(json);

            Assert.AreEqual(
                0,
                repeated.Count,
                "the writer emitted a property more than once in the same object: " + string.Join(", ", repeated));
        }

        private static List<string> RepeatedPropertyNames(string json)
        {
            var repeated = new List<string>();
            var seenAtDepth = new Dictionary<int, HashSet<string>>();
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            var depth = 0;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        depth++;
                        seenAtDepth[depth] = new HashSet<string>(StringComparer.Ordinal);
                        break;

                    case JsonTokenType.EndObject:
                        seenAtDepth.Remove(depth);
                        depth--;
                        break;

                    case JsonTokenType.PropertyName:
                        if (!seenAtDepth[depth].Add(reader.GetString()))
                            repeated.Add($"depth {depth}: {reader.GetString()}");
                        break;
                }
            }

            return repeated;
        }

        [TestMethod]
        public async Task ADestinationIdentifierGivenAnArrayIsRejected()
        {
            var blob = Encode(
                $"{{\"Destination\":{{\"ObjectId\":[\"{_destinationId:D}\"]}},\"SourceMembers\":[{MemberJson(_memberId)}]}}",
                BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            AssertMembersMatchProduction(blob, actual, "array-valued Destination.ObjectId", expectSuccess: false);
        }

        [TestMethod]
        public async Task AnArrayElementInsideTheMemberArrayDoesNotTruncateTheMembers()
        {
            var blob = Encode(
                $"{{\"SourceMembers\":[[],{MemberJson(_memberId)}]}}",
                BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            Assert.IsTrue(
                actual.Threw,
                $"a nested array element was tolerated and the read returned {actual.Members.Count} members. " +
                "Silently returning an empty membership would read downstream as everyone having left.");
        }

        [TestMethod]
        public async Task ABareValueInsideTheMemberArrayIsRejected()
        {
            var blob = Encode($"{{\"SourceMembers\":[\"{_memberId:D}\"]}}", BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            AssertMembersMatchProduction(blob, actual, "bare value as a member", expectSuccess: false);
        }

        [TestMethod]
        [DataRow(0, DisplayName = "no leading whitespace")]
        [DataRow(1023, DisplayName = "leading whitespace just under the probe limit")]
        [DataRow(1024, DisplayName = "leading whitespace exactly at the probe limit")]
        public async Task LeadingWhitespaceIsAcceptedRegardlessOfLength(int spaces)
        {
            var json = new string(' ', spaces) + BuildJson(Layout.ScalarsFirst, new[] { MemberJson(_memberId) }, false);
            var blob = Encode(json, BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);

            AssertMembersMatchProduction(blob, actual, $"{spaces} leading spaces");
        }

        #endregion

        #region Member array duplication and absence

        /// <summary>A second member array is rejected because yielded members cannot be replaced.</summary>
        [TestMethod]
        [DataRow(1)]
        [DataRow(7)]
        [DataRow(int.MaxValue)]
        public async Task ARepeatedSourceMembersArrayIsRejectedRatherThanConcatenated(int chunkSize)
        {
            var json = "{" +
                $"\"RunId\":\"{_runId:D}\"," +
                $"\"SourceMembers\":[{MemberJson(_memberId)}]," +
                $"\"SourceMembers\":[{MemberJson(_decoyId)}]" +
                "}";

            var actual = await ReadWithStreamAsync(Encode(json, BlobEncoding.Plain), chunkSize);

            Assert.IsTrue(
                actual.Threw,
                $"chunk={chunkSize}: a second member array was accepted, and the reader reported " +
                $"[{string.Join(",", actual.Members)}] where the deserializer reports only the second array.");
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(int.MaxValue)]
        public async Task AnEnvelopeWithoutMembersIsRejectedBeforeClassification(int chunkSize)
        {
            var json = "{" +
                $"\"Destination\":{{\"ObjectId\":\"{_destinationId:D}\"}}," +
                $"\"RunId\":\"{_runId:D}\"," +
                "\"Exclusionary\":true" +
                "}";
            var blob = Encode(json, BlobEncoding.Plain);

            var expected = ReadWithProductionDeserializer(blob);
            var actual = await ReadWithStreamAsync(blob, chunkSize);

            Assert.IsFalse(expected.Threw, $"chunk={chunkSize}: the production deserializer rejected the payload.");
            Assert.IsTrue(actual.Threw, $"chunk={chunkSize}: the reader accepted an envelope with no member array.");
            Assert.AreEqual(0, actual.Members.Count, $"chunk={chunkSize}: members were reported for an envelope that has none");
            Assert.AreEqual(
                "<never classified>",
                actual.EnvelopeWhenClassified,
                $"chunk={chunkSize}: the malformed envelope was reported before being rejected.");
        }

        #endregion

        #region Fuzz

        [TestMethod]
        public async Task RandomisedPayloadsMatchProductionDeserializer()
        {
            var random = new Random(8675309);
            var failures = new List<string>();

            for (var iteration = 0; iteration < 250; iteration++)
            {
                var layout = random.Next(2) == 0 ? Layout.MembersFirst : Layout.ScalarsFirst;
                var encoding = random.Next(2) == 0 ? BlobEncoding.Plain : BlobEncoding.Compressed;
                var exclusionary = random.Next(2) == 0;
                var count = random.Next(0, 400);
                var chunkSize = random.Next(4) switch
                {
                    0 => 1,
                    1 => 512,
                    2 => ReaderBufferSize,
                    _ => int.MaxValue
                };

                var blob = Encode(BuildJson(layout, GeneratedMembers(count, random), exclusionary), encoding);
                var expected = ReadWithProductionDeserializer(blob);
                var actual = await ReadWithStreamAsync(blob, chunkSize);

                var divergence = DivergenceOnValidPayload(expected, actual, $"iteration {iteration}: {layout}/{encoding}/count={count}/chunk={chunkSize}");
                if (divergence != null) failures.Add(divergence);
            }

            Assert.AreEqual(0, failures.Count, "randomised payloads diverged from the deserializer: " + string.Join("; ", failures));
        }

        /// <summary>Tests mixed property orders that the two fixed layouts cannot produce.</summary>
        [TestMethod]
        public async Task PermutedRootPropertiesMatchTheDeserializerOrAreRefusedForReportingEarly()
        {
            var random = new Random(20260827);
            var failures = new List<string>();
            var refused = 0;
            var acceptedWithMembersLast = 0;
            var acceptedDescribingItselfLate = 0;

            for (var iteration = 0; iteration < 3000; iteration++)
            {
                var encoding = random.Next(2) == 0 ? BlobEncoding.Plain : BlobEncoding.Compressed;
                var chunkSize = random.Next(4) switch
                {
                    0 => 1,
                    1 => 7,
                    2 => 512,
                    _ => int.MaxValue
                };

                var payload = BuildPermutedPayload(random, random.Next(0, 40));
                var blob = Encode(payload.Json, encoding);
                var where = $"iteration {iteration} [{payload.Order}] {encoding}/chunk={chunkSize}";

                var expected = ReadWithProductionDeserializer(blob);
                if (expected.Threw)
                {
                    failures.Add($"{where}: the generator built a payload the deserializer rejects ({expected.Failure})");
                    continue;
                }

                var actual = await ReadWithStreamAsync(blob, chunkSize);

                if (payload.MustBeRefused)
                {
                    if (!actual.Threw)
                        failures.Add($"{where}: described itself before the members and then carried '{payload.OffendingProperty}' after them, and the reader allowed it");
                    else if (!string.Equals(actual.Failure, nameof(JsonException), StringComparison.Ordinal))
                        failures.Add($"{where}: expected {nameof(JsonException)} for '{payload.OffendingProperty}' after the members, got {actual.Failure}");
                    else
                        refused++;

                    continue;
                }

                var divergence = DivergenceOnValidPayload(expected, actual, where);
                if (divergence != null)
                {
                    failures.Add(divergence);
                    continue;
                }

                if (!string.Equals(expected.Envelope, actual.EnvelopeWhenClassified, StringComparison.Ordinal))
                {
                    failures.Add($"{where}: envelope differs (deserializer={expected.Envelope}, reader={actual.EnvelopeWhenClassified})");
                    continue;
                }

                if (payload.OffendingProperty == null) acceptedWithMembersLast++;
                if (!payload.ReportsEarly) acceptedDescribingItselfLate++;
            }

            Assert.AreEqual(
                0,
                failures.Count,
                $"{failures.Count} of 3000 permuted payloads failed. First few: " + string.Join(" | ", failures.Take(10)));

            // Require meaningful coverage of every expected outcome.
            Assert.IsTrue(refused > 200, $"only {refused} payloads reached the refusal, so that rule is barely covered");
            Assert.IsTrue(acceptedWithMembersLast > 500, $"only {acceptedWithMembersLast} payloads carried nothing after the members");
            Assert.IsTrue(acceptedDescribingItselfLate > 200, $"only {acceptedDescribingItselfLate} payloads described themselves after the members");
        }

        #endregion

        #region Scale and boundary placement

        private static int FirstMemberCountExceedingTheBuffer(Layout layout, BlobEncoding encoding)
        {
            const int ceiling = 20000;

            if (EncodedSize(layout, encoding, ceiling) <= ReaderBufferSize)
            {
                Assert.Fail($"{layout}/{encoding}: even {ceiling} members still fit in one buffer pass, so the search bound is wrong.");
            }

            var low = 1;
            var high = ceiling;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (EncodedSize(layout, encoding, middle) > ReaderBufferSize) high = middle;
                else low = middle + 1;
            }
            return low;
        }

        private static int EncodedSize(Layout layout, BlobEncoding encoding, int memberCount) =>
            Encode(BuildJson(layout, GeneratedMembers(memberCount, new Random(4242)), true), encoding).Length;

        public static IEnumerable<object[]> LayoutAndEncoding()
        {
            foreach (var layout in new[] { Layout.MembersFirst, Layout.ScalarsFirst })
            {
                foreach (var encoding in new[] { BlobEncoding.Plain, BlobEncoding.Compressed })
                {
                    yield return new object[] { layout, encoding };
                }
            }
        }

        [TestMethod]
        [DynamicData(nameof(LayoutAndEncoding), DynamicDataSourceType.Method)]
        public async Task MemberDataSurvivesEitherSideOfTheBufferBoundary(Layout layout, BlobEncoding encoding)
        {
            var boundary = FirstMemberCountExceedingTheBuffer(layout, encoding);
            var failures = new List<string>();

            for (var count = Math.Max(0, boundary - 15); count <= boundary + 15; count++)
            {
                var random = new Random(20260824);
                var blob = Encode(BuildJson(layout, GeneratedMembers(count, random), true), encoding);
                var expected = ReadWithProductionDeserializer(blob);
                var actual = await ReadWithStreamAsync(blob, int.MaxValue);

                var divergence = DivergenceOnValidPayload(expected, actual, $"{count} members");
                if (divergence != null) failures.Add(divergence);
            }

            Assert.AreEqual(
                0,
                failures.Count,
                $"{layout}/{encoding}: the reader diverged from the deserializer around the {boundary}-member " +
                $"buffer boundary at: {string.Join(", ", failures)}.");
        }

        [TestMethod]
        [DynamicData(nameof(LayoutAndEncoding), DynamicDataSourceType.Method)]
        public async Task MemberDataSurvivesAcrossOrdersOfMagnitude(Layout layout, BlobEncoding encoding)
        {
            var failures = new List<string>();

            foreach (var count in new[] { 0, 1, 2, 3, 10, 100, 1000, 5000, 10000 })
            {
                var random = new Random(20260824);
                var blob = Encode(BuildJson(layout, GeneratedMembers(count, random), true), encoding);
                var expected = ReadWithProductionDeserializer(blob);
                var actual = await ReadWithStreamAsync(blob, int.MaxValue);

                var divergence = DivergenceOnValidPayload(expected, actual, $"{count} members");
                if (divergence != null) failures.Add(divergence);
            }

            Assert.AreEqual(0, failures.Count, $"{layout}/{encoding}: diverged at {string.Join(", ", failures)}.");
        }

        [TestMethod]
        [DynamicData(nameof(LayoutAndEncoding), DynamicDataSourceType.Method)]
        public async Task MemberDataSurvivesEveryReadBoundaryPlacement(Layout layout, BlobEncoding encoding)
        {
            var random = new Random(20260824);
            var blob = Encode(BuildJson(layout, GeneratedMembers(1000, random), true), encoding);
            var expected = ReadWithProductionDeserializer(blob);
            var failures = new List<string>();

            foreach (var chunkSize in new[] { 1, 2, 3, 5, 7, 13, 31, 127, 1023, 4095, 4096, 65535, ReaderBufferSize, 65537, int.MaxValue })
            {
                var actual = await ReadWithStreamAsync(blob, chunkSize);

                var divergence = DivergenceOnValidPayload(expected, actual, $"chunk={chunkSize}");
                if (divergence != null) failures.Add(divergence);
            }

            Assert.AreEqual(
                0,
                failures.Count,
                $"{layout}/{encoding}: a 1000-member payload lost or altered members at {string.Join(", ", failures)}. " +
                "Each chunk size places the read boundary at a different point inside the payload.");
        }

        [TestMethod]
        [DynamicData(nameof(LayoutAndEncoding), DynamicDataSourceType.Method)]
        public void TheMeasuredBufferBoundaryIsWithinTheRangeTheseTestsCover(Layout layout, BlobEncoding encoding)
        {
            var boundary = FirstMemberCountExceedingTheBuffer(layout, encoding);

            Assert.IsTrue(
                EncodedSize(layout, encoding, boundary) > ReaderBufferSize,
                $"{layout}/{encoding}: {boundary} members was reported as the first count exceeding the buffer but does not.");
            Assert.IsTrue(
                boundary <= 1 || EncodedSize(layout, encoding, boundary - 1) <= ReaderBufferSize,
                $"{layout}/{encoding}: {boundary - 1} members already exceeds the buffer, so the boundary is not minimal.");
        }

        [TestMethod]
        [DynamicData(nameof(LayoutAndEncoding), DynamicDataSourceType.Method)]
        public async Task EveryEnvelopeFieldIsReadAsTheDeserializerReadsIt(Layout layout, BlobEncoding encoding)
        {
            var extras = "\"MembershipObtainerDryRunEnabled\":true,\"IsLastMessage\":true," +
                         "\"MessageIndex\":7,\"TotalMessageCount\":9," +
                         "\"Query\":\"[{\\\"type\\\":\\\"SqlMembership\\\"}]\"";
            var json = BuildJson(layout, new[] { MemberJson(_memberId, _runId, 2) }, true, extras);
            var blob = Encode(json, encoding);

            var actual = await ReadWithStreamAsync(blob, 7);

            AssertMembersMatchProduction(blob, actual, $"{layout}/{encoding} full envelope");
        }

        #endregion

        #region Harness self-check

        private static async IAsyncEnumerable<AzureADUser> AsAsync(IEnumerable<AzureADUser> members)
        {
            foreach (var member in members) yield return member;
            await Task.CompletedTask;
        }

        /// <summary>Checks writer output with the existing deserializer.</summary>
        [TestMethod]
        public async Task WriterOutputDeserializesToWhatWasWritten()
        {
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = _destinationId },
                RunId = _runId,
                SyncJobId = _syncJobId,
                Exclusionary = true,
                MembershipObtainerDryRunEnabled = true,
                IsLastMessage = true,
                MessageIndex = 7,
                TotalMessageCount = 9,
                Query = "[{\"type\":\"SqlMembership\"}]",
                SourceMembers = new List<AzureADUser>()
            };

            var members = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = _memberId, SourceGroup = _runId, MembershipAction = MembershipAction.Add },
                new AzureADUser { ObjectId = Guid.Empty, SourceGroups = new List<Guid> { _syncJobId, Guid.Empty } },
                new AzureADUser { ObjectId = _decoyId, MembershipAction = MembershipAction.Remove }
            };

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, AsAsync(members));

            var text = TextCompressor.Decompress(Encoding.UTF8.GetString(destination.ToArray()));
            var round = JsonSerializer.Deserialize<GroupMembership>(text);

            Assert.AreEqual(
                EnvelopeSnapshot(envelope),
                EnvelopeSnapshot(round),
                "the envelope did not survive the write.");
            CollectionAssert.AreEqual(
                members.Select(Describe).ToList(),
                (round.SourceMembers ?? new List<AzureADUser>()).Select(Describe).ToList(),
                "the members did not survive the write.");
        }

        [TestMethod]
        public async Task WriterOutputIsAcceptedByTheReaderAndAgreesWithTheDeserializer()
        {
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = _destinationId },
                RunId = _runId,
                SyncJobId = _syncJobId,
                Exclusionary = true,
                Query = "q",
                SourceMembers = new List<AzureADUser>()
            };

            var members = Enumerable.Range(0, 2000)
                .Select(_ => new AzureADUser { ObjectId = Guid.NewGuid(), SourceGroup = Guid.NewGuid() })
                .ToList();

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, AsAsync(members));
            var blob = destination.ToArray();

            var actual = await ReadWithStreamAsync(blob, 7);

            AssertMembersMatchProduction(blob, actual, "writer output read back at a 7 byte chunk");
            Assert.AreEqual(
                0,
                actual.MembersYieldedBeforeClassification,
                "the writer places its scalars first, so its own output must classify before any member.");
        }

        [TestMethod]
        public async Task ComparisonDetectsAnInjectedDifference()
        {
            var random = new Random(20260824);
            var members = GeneratedMembers(50, random);
            var blob = Encode(BuildJson(Layout.ScalarsFirst, members, false), BlobEncoding.Plain);

            var actual = await ReadWithStreamAsync(blob, int.MaxValue);
            var corrupted = actual.Members.ToList();
            corrupted[3] = $"{_decoyId:D}|{Guid.Empty:D}|-|-";

            Assert.ThrowsException<AssertFailedException>(
                () => AssertMembersMatchProduction(blob, actual with { Members = corrupted }, "injected difference"),
                "the comparison passed on a deliberately corrupted member, so a green run from it would mean nothing.");
        }

        [TestMethod]
        public async Task ChunkedStreamActuallyLimitsEachRead()
        {
            var blob = Encode(BuildJson(Layout.ScalarsFirst, GeneratedMembers(200, new Random(1)), false), BlobEncoding.Plain);
            using var source = new ChunkedStream(blob, 7);

            var buffer = new byte[4096];
            var first = await source.ReadAsync(buffer.AsMemory());
            var second = source.Read(buffer, 0, buffer.Length);

            Assert.AreEqual(7, first, "the asynchronous read path ignored the chunk limit.");
            Assert.AreEqual(7, second, "the synchronous read path ignored the chunk limit.");
        }

        [TestMethod]
        public void GeneratedLargePayloadActuallyExceedsTheReaderBuffer()
        {
            var random = new Random(20260824);
            var json = BuildJson(Layout.MembersFirst, GeneratedMembers(MembersExceedingBuffer, random), true);

            Assert.IsTrue(
                Encoding.UTF8.GetByteCount(json) > ReaderBufferSize,
                "the large payload no longer exceeds the reader's buffer, so every size-dependent test above " +
                "has quietly stopped testing anything.");
        }

        #endregion
    }
}
