// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.BlobStorage;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Tests.FunctionApps
{
    [TestClass]
    public class MembershipStreamWriterParityTests
    {
        private static readonly Guid _destinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid _runId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid _syncJobId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid _memberId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid _sourceGroupId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        private static readonly Guid _otherSourceGroupId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        private static readonly JsonSerializerOptions _productionOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        [TestMethod]
        public async Task WriterOutputIsByteIdenticalToTheProductionSerializer()
        {
            var failures = new List<string>();

            foreach (var shape in MemberShapes())
            {
                var expectedEnvelope = Envelope();
                expectedEnvelope.SourceMembers = new List<AzureADUser> { shape.Member };
                var expected = JsonSerializer.Serialize(expectedEnvelope, _productionOptions);

                using var destination = new MemoryStream();
                await MembershipStream.WriteAsync(destination, Envelope(), AsAsync(shape.Member));
                var actual = TextCompressor.Decompress(Encoding.UTF8.GetString(destination.ToArray()));

                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    failures.Add($"[{shape.Name}]\n  expected: {expected}\n  actual:   {actual}");
                }
            }

            Assert.AreEqual(
                0,
                failures.Count,
                $"{failures.Count} of {CountShapes()} member shapes did not match the production serializer:\n\n"
                    + string.Join("\n\n", failures));
        }

        [TestMethod]
        public async Task AnEmptySourceGroupsListSurvivesTheRoundTrip()
        {
            var member = new AzureADUser { ObjectId = _memberId, SourceGroups = new List<Guid>() };

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, Envelope(), AsAsync(member));

            var text = TextCompressor.Decompress(Encoding.UTF8.GetString(destination.ToArray()));
            var round = JsonSerializer.Deserialize<GroupMembership>(text);

            Assert.IsNotNull(round.SourceMembers[0].SourceGroups, "an empty list came back as null.");
            Assert.AreEqual(0, round.SourceMembers[0].SourceGroups.Count);
        }

        [TestMethod]
        public async Task EveryMemberPropertySurvivesTheRoundTrip()
        {
            var member = new AzureADUser
            {
                ObjectId = _memberId,
                Mail = "member@contoso.com",
                UserPrincipalName = "member@contoso.com",
                DisplayName = "A Member",
                OnPremisesImmutableId = "immutable-id-1",
                Properties = new Dictionary<string, object>
                {
                    ["Department"] = "Engineering",
                    ["EmployeeNumber"] = 42
                },
                MembershipAction = MembershipAction.Add,
                SourceGroup = _sourceGroupId,
                SourceGroups = new List<Guid> { _sourceGroupId, _otherSourceGroupId }
            };

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, Envelope(), AsAsync(member));

            var read = new List<AzureADUser>();
            using var source = new MemoryStream(destination.ToArray());
            await foreach (var each in MembershipStream.ReadAsync(source, _ => { }))
            {
                read.Add(each);
            }

            Assert.AreEqual(1, read.Count, "the round trip did not return exactly one member.");
            Assert.AreEqual(
                JsonSerializer.Serialize(member, _productionOptions),
                JsonSerializer.Serialize(read[0], _productionOptions),
                "a member property was lost or altered between the writer and the reader.");
        }

        [TestMethod]
        public async Task WriterRejectsANullMemberInsteadOfSilentlyDroppingIt()
        {
            using var destination = new MemoryStream();

            await Assert.ThrowsExceptionAsync<JsonException>(
                () => MembershipStream.WriteAsync(destination, Envelope(), AsAsync((AzureADUser)null)));
        }

        [TestMethod]
        public async Task WriterProducesACompleteBrotliStream()
        {
            var member = new AzureADUser
            {
                ObjectId = _memberId,
                SourceGroup = _sourceGroupId,
                MembershipAction = MembershipAction.Add
            };
            var expectedEnvelope = Envelope();
            expectedEnvelope.SourceMembers = new List<AzureADUser> { member };
            var expected = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(expectedEnvelope, _productionOptions));

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, Envelope(), AsAsync(member));

            var compressed = Convert.FromBase64String(Encoding.UTF8.GetString(destination.ToArray()));
            var actual = new byte[expected.Length];
            var decoder = new BrotliDecoder();
            var status = decoder.Decompress(compressed, actual, out var bytesConsumed, out var bytesWritten);

            Assert.AreEqual(OperationStatus.Done, status, "the Brotli payload was not finalized.");
            Assert.AreEqual(compressed.Length, bytesConsumed, "the Brotli decoder did not consume the whole payload.");
            Assert.AreEqual(expected.Length, bytesWritten, "the decompressed JSON length changed.");
            CollectionAssert.AreEqual(expected, actual, "the independently decoded JSON differs from the production serializer.");
        }

        [TestMethod]
        public async Task EveryDestinationPropertySurvivesTheRoundTrip()
        {
            var envelope = Envelope();
            envelope.Destination = new AzureADGroup
            {
                ObjectId = _destinationId,
                Type = "Microsoft 365",
                Name = "Destination Group",
                Email = "destination@contoso.com",
                Visibility = "Private"
            };

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, AsAsync(new AzureADUser { ObjectId = _memberId }));

            GroupMembership classified = null;
            using var source = new MemoryStream(destination.ToArray());
            await foreach (var _ in MembershipStream.ReadAsync(source, e => classified = Clone(e)))
            {
            }

            Assert.IsNotNull(classified, "the envelope was never reported.");
            Assert.AreEqual(
                JsonSerializer.Serialize(envelope.Destination, _productionOptions),
                JsonSerializer.Serialize(classified.Destination, _productionOptions),
                "a destination property was lost or altered between the writer and the reader.");
        }

        [TestMethod]
        public async Task AMembershipWithNoMembersSurvivesTheRoundTrip()
        {
            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, Envelope(), AsAsync());

            GroupMembership classified = null;
            var read = new List<AzureADUser>();
            using var source = new MemoryStream(destination.ToArray());
            await foreach (var each in MembershipStream.ReadAsync(source, e => classified = Clone(e)))
            {
                read.Add(each);
            }

            Assert.IsNotNull(classified, "an empty membership was never classified.");
            Assert.AreEqual(0, read.Count, "an empty membership yielded members.");
            Assert.AreEqual(
                _destinationId,
                classified.Destination.ObjectId,
                "the envelope of an empty membership did not survive.");
        }

        [TestMethod]
        public async Task EveryEnvelopePropertySurvivesTheRoundTrip()
        {
            var envelope = FullyPopulatedEnvelope();

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, AsAsync(new AzureADUser { ObjectId = _memberId }));

            GroupMembership classified = null;
            using var source = new MemoryStream(destination.ToArray());
            await foreach (var _ in MembershipStream.ReadAsync(source, e => classified = Clone(e)))
            {
            }

            Assert.IsNotNull(classified, "the envelope was never reported.");

            var lost = new List<string>();
            foreach (var property in typeof(GroupMembership).GetProperties())
            {
                // Streamed one at a time rather than carried on the reported envelope.
                if (string.Equals(property.Name, nameof(GroupMembership.SourceMembers), StringComparison.Ordinal))
                    continue;

                var expected = JsonSerializer.Serialize(property.GetValue(envelope), _productionOptions);
                var actual = JsonSerializer.Serialize(property.GetValue(classified), _productionOptions);

                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    lost.Add($"{property.Name} (wrote {expected}, read {actual})");
            }

            Assert.AreEqual(
                0,
                lost.Count,
                "envelope properties were lost or altered between the writer and the reader: " + string.Join(", ", lost));
        }

        private static GroupMembership Clone(GroupMembership envelope) =>
            JsonSerializer.Deserialize<GroupMembership>(JsonSerializer.Serialize(envelope, _productionOptions), _productionOptions);

        [TestMethod]
        public async Task TheWrittenBlobIsNoLargerThanTheProductionSerializerProduces()
        {
            const int memberCount = 20_000;
            var members = Members(memberCount);

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, Envelope(), AsAsync(members));
            var streamedSize = destination.Length;

            var wholeEnvelope = Envelope();
            wholeEnvelope.SourceMembers = new List<AzureADUser>(members);
            var legacySize = TextCompressor.Compress(JsonSerializer.Serialize(wholeEnvelope, _productionOptions)).Length;

            var ratio = (double)streamedSize / legacySize;

            Assert.IsTrue(
                ratio <= 1.10,
                $"the streamed blob was {ratio:F3}x the size of the production serializer's output "
                    + $"({streamedSize:N0} bytes against {legacySize:N0}) for {memberCount:N0} members. "
                    + "A ratio well above 1 means the members are reaching the compressor one at a time.");
        }

        private static AzureADUser[] Members(int count)
        {
            var random = new Random(20260826);
            var members = new AzureADUser[count];
            var bytes = new byte[16];

            for (var i = 0; i < count; i++)
            {
                random.NextBytes(bytes);
                members[i] = new AzureADUser { ObjectId = new Guid(bytes) };
            }

            return members;
        }

        private static IEnumerable<(string Name, AzureADUser Member)> MemberShapes()
        {
            yield return ("object id only", new AzureADUser { ObjectId = _memberId });
            yield return ("all zero object id", new AzureADUser { ObjectId = Guid.Empty });
            yield return ("source group set", new AzureADUser { ObjectId = _memberId, SourceGroup = _sourceGroupId });
            yield return ("source group all zero", new AzureADUser { ObjectId = _memberId, SourceGroup = Guid.Empty });
            yield return ("source groups null", new AzureADUser { ObjectId = _memberId, SourceGroups = null });
            yield return ("source groups empty", new AzureADUser { ObjectId = _memberId, SourceGroups = new List<Guid>() });
            yield return ("source groups one", new AzureADUser { ObjectId = _memberId, SourceGroups = new List<Guid> { _sourceGroupId } });
            yield return ("source groups many", new AzureADUser { ObjectId = _memberId, SourceGroups = new List<Guid> { _sourceGroupId, _otherSourceGroupId } });
            yield return ("source groups containing all zero", new AzureADUser { ObjectId = _memberId, SourceGroups = new List<Guid> { Guid.Empty } });
            yield return ("membership action add", new AzureADUser { ObjectId = _memberId, MembershipAction = MembershipAction.Add });
            yield return ("membership action remove", new AzureADUser { ObjectId = _memberId, MembershipAction = MembershipAction.Remove });
            yield return ("identity fields populated", new AzureADUser
            {
                ObjectId = _memberId,
                Mail = "someone@contoso.com",
                UserPrincipalName = "someone@contoso.com",
                DisplayName = "Someone",
                OnPremisesImmutableId = "immutable-id"
            });
            yield return ("every field populated", new AzureADUser
            {
                ObjectId = _memberId,
                Mail = "someone@contoso.com",
                UserPrincipalName = "someone@contoso.com",
                DisplayName = "Someone",
                OnPremisesImmutableId = "immutable-id",
                Properties = new Dictionary<string, object>
                {
                    ["Department"] = "Engineering",
                    ["EmployeeNumber"] = 42
                },
                MembershipAction = MembershipAction.Add,
                SourceGroup = _sourceGroupId,
                SourceGroups = new List<Guid> { _sourceGroupId, _otherSourceGroupId }
            });
        }

        private static int CountShapes()
        {
            var count = 0;
            foreach (var _ in MemberShapes())
            {
                count++;
            }

            return count;
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

        private static GroupMembership FullyPopulatedEnvelope() => new GroupMembership
        {
            Destination = new AzureADGroup
            {
                ObjectId = _destinationId,
                Type = "Microsoft 365",
                Name = "Destination Group",
                Email = "destination@contoso.com",
                Visibility = "Private"
            },
            RunId = _runId,
            SyncJobId = _syncJobId,
            MembershipObtainerDryRunEnabled = true,
            Exclusionary = true,
            Query = "[{\"type\":\"SqlMembership\"}]",
            SyncJob = new SyncJob
            {
                Id = _syncJobId,
                TargetOfficeGroupId = _destinationId,
                Requestor = "requestor@contoso.com",
                Destination = "[{\"type\":\"GroupMembership\"}]",
                AllowEmptyDestination = true,
                MembershipType = "GroupMembership",
                Status = "InProgress",
                Period = 24,
                Query = "[{\"type\":\"SqlMembership\"}]",
                ThresholdPercentageForAdditions = 100,
                ThresholdPercentageForRemovals = 20
            },
            ProjectedMemberCount = 4_242,
            TotalMembersToAdd = 17,
            TotalMembersToRemove = 9,
            MessageIndex = 3,
            IsLastMessage = true,
            TotalMessageCount = 5,
            SourceMembers = new List<AzureADUser>()
        };

        private static async IAsyncEnumerable<AzureADUser> AsAsync(params AzureADUser[] members)
        {
            foreach (var member in members)
            {
                yield return member;
            }

            await Task.CompletedTask;
        }
    }
}
