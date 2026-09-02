// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using MembershipAggregator.Services.Entities;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Entities;
using Models.Helpers;
using Models.ServiceBus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class MembershipMergeEngineFileParityTests
    {
        private const int RandomIterations = 2_000;

        [TestMethod]
        public async Task MixedProductionFormatsPreserveExactSensitiveMembershipDelta()
        {
            var firstSourceGroup = ParseGuid(101);
            var secondSourceGroup = ParseGuid(102);
            var thirdSourceGroup = ParseGuid(103);
            var fourthSourceGroup = ParseGuid(104);
            var unchanged = ParseGuid(1);
            var added = ParseGuid(2);
            var removed = ParseGuid(3);
            var excluded = ParseGuid(4);
            var excludedFromDestination = ParseGuid(5);
            var addedWithSourceGroupList = ParseGuid(6);
            var inputs = new[]
            {
                Input(
                    "include-raw",
                    MembershipMergeInputKind.Included,
                    MembershipFileFormat.RawJson,
                    User(Guid.Empty, firstSourceGroup),
                    User(unchanged, firstSourceGroup),
                    User(added, firstSourceGroup),
                    User(added, Guid.Empty, firstSourceGroup, thirdSourceGroup),
                    User(excluded, firstSourceGroup),
                    User(excludedFromDestination, firstSourceGroup)),
                Input(
                    "include-compressed",
                    MembershipMergeInputKind.Included,
                    MembershipFileFormat.Base64Brotli,
                    User(added, secondSourceGroup),
                    User(addedWithSourceGroupList, Guid.Empty, fourthSourceGroup)),
                Input(
                    "exclude-raw",
                    MembershipMergeInputKind.Excluded,
                    MembershipFileFormat.RawJson,
                    User(excluded),
                    User(excludedFromDestination)),
                Input(
                    "destination-compressed",
                    MembershipMergeInputKind.Destination,
                    MembershipFileFormat.Base64Brotli,
                    User(unchanged),
                    TeamsUser(removed, "conversation-member-removed"),
                    TeamsUser(excludedFromDestination, "conversation-member-excluded"))
            };

            await AssertFileBackedMergeAsync(inputs, "sensitive", 700_001);
        }

        [TestMethod]
        public async Task FileBackedMergeMatchesIndependentOracleAcrossRandomInputs()
        {
            var random = new Random(837_421);

            for (var iteration = 0; iteration < RandomIterations; iteration++)
            {
                var inputs = RandomInputs(random, iteration);
                try
                {
                    await AssertFileBackedMergeAsync(inputs, $"random-{iteration}", 710_000 + iteration);
                }
                catch (AssertFailedException exception)
                {
                    Assert.Fail($"Iteration {iteration} failed: {exception.Message}");
                }
            }
        }

        [TestMethod]
        public async Task HierarchicalFileBackedMergeMatchesIndependentOracle()
        {
            var inputs = Enumerable.Range(0, 20)
                .Select(index => Input(
                    $"include-{index}",
                    MembershipMergeInputKind.Included,
                    index % 2 == 0
                        ? MembershipFileFormat.RawJson
                        : MembershipFileFormat.Base64Brotli,
                    User(ParseGuid(index % 9 + 1), ParseGuid(1_000 + index))))
                .Concat(new[]
                {
                    Input(
                        "exclude",
                        MembershipMergeInputKind.Excluded,
                        MembershipFileFormat.RawJson,
                        User(ParseGuid(2)),
                        User(ParseGuid(4))),
                    Input(
                        "destination",
                        MembershipMergeInputKind.Destination,
                        MembershipFileFormat.Base64Brotli,
                        TeamsUser(ParseGuid(2), "conversation-member-two"),
                        TeamsUser(ParseGuid(8), "conversation-member-eight"),
                        TeamsUser(ParseGuid(20), "conversation-member-twenty"))
                })
                .ToArray();

            var diagnostics = await AssertFileBackedMergeAsync(inputs, "hierarchical", 720_001);

            Assert.IsTrue(diagnostics.UsedHierarchicalMerge);
            Assert.IsTrue(diagnostics.PassCount >= 2);
            Assert.IsTrue(diagnostics.PeakOpenStreams <= MembershipMergeOptions.DefaultMaxStreamsPerPass);
        }

        [TestMethod]
        public async Task EqualCardinalityFileReplacementBetweenPassesPublishesNoResult()
        {
            var store = new MembershipMergeTestStore();
            var envelope = Envelope(730_001);
            var initialMember = User(ParseGuid(1), ParseGuid(101));
            var replacementMember = User(ParseGuid(3), ParseGuid(101));
            store.AddRaw("changing-source", envelope, initialMember);
            await store.AddCompressedAsync("destination", envelope, User(ParseGuid(2)));
            var replaced = false;
            store.ReadFailure = (path, attempt, memberIndex) =>
            {
                if (!replaced
                    && path == "changing-source"
                    && attempt == 1
                    && memberIndex == 0)
                {
                    store.AddRaw("changing-source", envelope, replacementMember);
                    replaced = true;
                }

                return null;
            };
            var request = new MembershipMergeRequest(
                new[]
                {
                    MembershipMergeInput.FromPath("changing-source", MembershipMergeInputKind.Included),
                    MembershipMergeInput.FromPath("destination", MembershipMergeInputKind.Destination)
                },
                "file-mutation-output",
                "file-mutation",
                ParseGuid(730_002),
                envelope);

            var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Engine(store).ExecuteAsync(request));

            Assert.IsTrue(replaced);
            StringAssert.Contains(exception.Message, "changed while writing delta outputs");
            Assert.IsFalse(store.ContainsMembership($"{request.AttemptPrefix}-additions.json"));
            Assert.IsFalse(store.ContainsMembership($"{request.AttemptPrefix}-removals.json"));
            Assert.IsFalse(store.ContainsText($"{request.OutputPrefix}/{request.IdempotencyKey}-manifest.json"));
        }

        private static async Task<MembershipMergeDiagnostics> AssertFileBackedMergeAsync(
            IReadOnlyList<FileInput> fileInputs,
            string testId,
            int attemptValue)
        {
            var store = new MembershipMergeTestStore();
            var envelope = Envelope(attemptValue);
            // Inputs must follow the production ordering contract; the expected result is ordered independently.
            var orderedInputs = fileInputs
                .Select(input => input with
                {
                    Members = input.Members
                        .OrderBy(member => member.ObjectId, CanonicalObjectIdComparer.Instance)
                        .ToArray()
                })
                .ToArray();
            var expected = CalculateExpected(orderedInputs);
            var mergeInputs = new List<MembershipMergeInput>(orderedInputs.Length);

            foreach (var input in orderedInputs)
            {
                if (input.Format == MembershipFileFormat.RawJson)
                {
                    store.AddRaw(input.Path, envelope, input.Members.ToArray());
                }
                else
                {
                    await store.AddCompressedAsync(input.Path, envelope, input.Members.ToArray());
                }

                var bytes = store.ReadBytes(input.Path);
                Assert.IsTrue(bytes.Length > 0);
                if (input.Format == MembershipFileFormat.RawJson)
                {
                    Assert.AreEqual((byte)'{', bytes[0]);
                }
                else
                {
                    Assert.AreNotEqual((byte)'{', bytes[0]);
                }

                mergeInputs.Add(MembershipMergeInput.FromPath(input.Path, input.Kind));
            }

            var request = new MembershipMergeRequest(
                mergeInputs,
                $"file-parity-output/{testId}",
                $"file-parity-{testId}",
                ParseGuid(attemptValue),
                envelope);
            var diagnostics = new MembershipMergeDiagnostics();
            var result = await Engine(store).ExecuteAsync(request, diagnostics);
            var additions = await store.ReadMembersAsync(result.AdditionsPath);
            var removals = await store.ReadMembersAsync(result.RemovalsPath);

            Assert.AreNotEqual((byte)'{', store.ReadBytes(result.AdditionsPath)[0]);
            Assert.AreNotEqual((byte)'{', store.ReadBytes(result.RemovalsPath)[0]);
            Assert.AreEqual(expected.SourceMemberCount, result.SourceMemberCount);
            Assert.AreEqual(expected.DestinationMemberCount, result.DestinationMemberCount);
            Assert.AreEqual(expected.Additions.Count, result.AddCount);
            Assert.AreEqual(expected.Removals.Count, result.RemoveCount);
            Assert.AreEqual(expected.Status, result.Status);
            AssertMembersEqual(expected.Additions, additions, MembershipAction.Add);
            AssertMembersEqual(expected.Removals, removals, MembershipAction.Remove);
            await AssertEnvelopeEqualAsync(envelope, store, result.AdditionsPath);
            await AssertEnvelopeEqualAsync(envelope, store, result.RemovalsPath);

            var manifest = JsonSerializer.Deserialize<MembershipMergeManifest>(store.ReadText(result.ManifestPath));
            Assert.IsNotNull(manifest);
            Assert.AreEqual(result.AdditionsPath, manifest.AdditionsPath);
            Assert.AreEqual(result.RemovalsPath, manifest.RemovalsPath);
            Assert.AreEqual(result.SourceMemberCount, manifest.SourceMemberCount);
            Assert.AreEqual(result.DestinationMemberCount, manifest.DestinationMemberCount);
            Assert.AreEqual(result.AddCount, manifest.AddCount);
            Assert.AreEqual(result.RemoveCount, manifest.RemoveCount);
            Assert.AreEqual(result.Status, manifest.Status);

            return diagnostics;
        }

        private static ExpectedDelta CalculateExpected(IReadOnlyList<FileInput> inputs)
        {
            // Do not call merge-engine helpers here; shared logic could let the same defect pass both sides.
            var includedIds = inputs
                .Where(input => input.Kind == MembershipMergeInputKind.Included)
                .SelectMany(input => input.Members)
                .Select(member => member.ObjectId)
                .ToHashSet();
            var excludedIds = inputs
                .Where(input => input.Kind == MembershipMergeInputKind.Excluded)
                .SelectMany(input => input.Members)
                .Select(member => member.ObjectId)
                .ToHashSet();
            var sourceIds = includedIds
                .Where(objectId => !excludedIds.Contains(objectId))
                .ToHashSet();
            var destinationIds = new HashSet<Guid>();
            var destinationProperties = new Dictionary<Guid, string>();

            foreach (var input in inputs.Where(input => input.Kind == MembershipMergeInputKind.Destination))
            {
                foreach (var member in input.Members)
                {
                    destinationIds.Add(member.ObjectId);
                    var propertiesJson = GetPropertiesJson(member.Properties);
                    if (!destinationProperties.TryGetValue(member.ObjectId, out var existingProperties)
                        || existingProperties == null)
                    {
                        destinationProperties[member.ObjectId] = propertiesJson;
                    }
                }
            }

            var additions = sourceIds
                .Where(objectId => !destinationIds.Contains(objectId))
                .OrderBy(objectId => objectId.ToString("D"), StringComparer.Ordinal)
                .Select(objectId => new ExpectedMember(objectId, GetSourceGroups(inputs, objectId), null))
                .ToArray();
            var removals = destinationIds
                .Where(objectId => !sourceIds.Contains(objectId))
                .OrderBy(objectId => objectId.ToString("D"), StringComparer.Ordinal)
                .Select(objectId => new ExpectedMember(
                    objectId,
                    Array.Empty<Guid>(),
                    destinationProperties.TryGetValue(
                        objectId,
                        out var propertiesJson)
                            ? propertiesJson
                            : null))
                .ToArray();
            var status = additions.Length == 0 && removals.Length == 0
                ? MembershipDeltaStatus.NoChanges
                : MembershipDeltaStatus.Ok;

            return new ExpectedDelta(additions, removals, sourceIds.Count, destinationIds.Count, status);
        }

        private static IReadOnlyList<Guid> GetSourceGroups(IEnumerable<FileInput> inputs, Guid objectId)
        {
            var sourceGroups = new HashSet<Guid>();
            foreach (var member in inputs
                .Where(input => input.Kind == MembershipMergeInputKind.Included)
                .SelectMany(input => input.Members)
                .Where(member => member.ObjectId == objectId))
            {
                if (member.SourceGroups != null && member.SourceGroups.Count > 0)
                {
                    foreach (var sourceGroup in member.SourceGroups)
                    {
                        sourceGroups.Add(sourceGroup);
                    }
                }

                if (member.SourceGroup != Guid.Empty
                    || member.SourceGroups == null
                    || member.SourceGroups.Count == 0)
                {
                    sourceGroups.Add(member.SourceGroup);
                }
            }

            return sourceGroups
                .OrderBy(sourceGroup => sourceGroup.ToString("D"), StringComparer.Ordinal)
                .ToArray();
        }

        private static void AssertMembersEqual(
            IReadOnlyList<ExpectedMember> expected, IReadOnlyList<AzureADUser> actual, MembershipAction action)
        {
            Assert.AreEqual(expected.Count, actual.Count, $"{action} count changed.");
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.AreEqual(
                    expected[index].ObjectId,
                    actual[index].ObjectId,
                    $"{action} ObjectId changed at index {index}.");
                Assert.AreEqual(action, actual[index].MembershipAction);
                CollectionAssert.AreEqual(
                    expected[index].SourceGroups.ToArray(),
                    (actual[index].SourceGroups ?? new List<Guid>()).ToArray(),
                    $"{action} SourceGroups changed for {expected[index].ObjectId}.");
                Assert.AreEqual(
                    expected[index].PropertiesJson,
                    GetPropertiesJson(actual[index].Properties),
                    $"{action} destination properties changed for {expected[index].ObjectId}.");
            }
        }

        private static async Task AssertEnvelopeEqualAsync(GroupMembership expected, MembershipMergeTestStore store, string path)
        {
            var actual = await store.ReadEnvelopeAsync(path);

            Assert.IsNotNull(actual);
            Assert.AreEqual(expected.Destination.ObjectId, actual.Destination.ObjectId);
            Assert.AreEqual(expected.RunId, actual.RunId);
            Assert.AreEqual(expected.SyncJobId, actual.SyncJobId);
            Assert.AreEqual(expected.MembershipObtainerDryRunEnabled, actual.MembershipObtainerDryRunEnabled);
            Assert.AreEqual(expected.Exclusionary, actual.Exclusionary);
            Assert.AreEqual(expected.Query, actual.Query);
            Assert.AreEqual(expected.ProjectedMemberCount, actual.ProjectedMemberCount);
            Assert.AreEqual(expected.TotalMembersToAdd, actual.TotalMembersToAdd);
            Assert.AreEqual(expected.TotalMembersToRemove, actual.TotalMembersToRemove);
        }

        private static IReadOnlyList<FileInput> RandomInputs(Random random, int iteration)
        {
            var inputs = new List<FileInput>();
            var memberPool = Enumerable.Range(0, random.Next(1, 24))
                .Select(index => ParseGuid(iteration * 100 + index + 1))
                .ToArray();
            var sourceGroupPool = Enumerable.Range(0, 5)
                .Select(index => ParseGuid(900_000 + index))
                .Prepend(Guid.Empty)
                .ToArray();
            var inclusionCount = random.Next(1, 5);

            for (var inputIndex = 0; inputIndex < inclusionCount; inputIndex++)
            {
                inputs.Add(Input(
                    $"random-{iteration}-include-{inputIndex}",
                    MembershipMergeInputKind.Included,
                    SelectFormat(iteration + inputIndex),
                    RandomMembers(
                        random,
                        memberPool,
                        sourceGroupPool,
                        includeDestinationProperties: false)));
            }

            var exclusionCount = random.Next(0, 3);
            for (var inputIndex = 0; inputIndex < exclusionCount; inputIndex++)
            {
                inputs.Add(Input(
                    $"random-{iteration}-exclude-{inputIndex}",
                    MembershipMergeInputKind.Excluded,
                    SelectFormat(iteration + inclusionCount + inputIndex),
                    RandomMembers(
                        random,
                        memberPool,
                        sourceGroupPool,
                        includeDestinationProperties: false)));
            }

            inputs.Add(Input(
                $"random-{iteration}-destination",
                MembershipMergeInputKind.Destination,
                SelectFormat(iteration + inclusionCount + exclusionCount),
                RandomMembers(
                    random,
                    memberPool,
                    sourceGroupPool,
                    includeDestinationProperties: true)));
            return inputs;
        }

        private static AzureADUser[] RandomMembers(
            Random random,
            IReadOnlyList<Guid> memberPool,
            IReadOnlyList<Guid> sourceGroupPool,
            bool includeDestinationProperties)
        {
            var members = new List<AzureADUser>();
            var count = random.Next(0, memberPool.Count * 2 + 1);

            for (var index = 0; index < count; index++)
            {
                var objectId = memberPool[random.Next(memberPool.Count)];
                if (includeDestinationProperties && random.Next(0, 3) == 0)
                {
                    members.Add(TeamsUser(objectId, $"conversation-{objectId:N}-{index}"));
                    continue;
                }

                var sourceGroup = sourceGroupPool[random.Next(sourceGroupPool.Count)];
                if (random.Next(0, 4) == 0)
                {
                    members.Add(User(
                        objectId,
                        sourceGroup,
                        sourceGroupPool[random.Next(sourceGroupPool.Count)],
                        sourceGroupPool[random.Next(sourceGroupPool.Count)]));
                }
                else
                {
                    members.Add(User(objectId, sourceGroup));
                }
            }

            return members.ToArray();
        }

        private static FileInput Input(
            string path, MembershipMergeInputKind kind, MembershipFileFormat format, params AzureADUser[] members) =>
            new FileInput(path, kind, format, members);

        private static MembershipFileFormat SelectFormat(int value) =>
            value % 2 == 0
                ? MembershipFileFormat.RawJson
                : MembershipFileFormat.Base64Brotli;

        private static MembershipMergeEngine Engine(MembershipMergeTestStore store) =>
            new MembershipMergeEngine(store.Repository.Object, Options.Create(new MembershipMergeOptions()));

        private static GroupMembership Envelope(int value) => new GroupMembership
        {
            Destination = new AzureADGroup { ObjectId = ParseGuid(value + 10_000) },
            RunId = ParseGuid(value + 20_000),
            SyncJobId = ParseGuid(value + 30_000),
            MembershipObtainerDryRunEnabled = true,
            Exclusionary = false,
            Query = $"query-{value}",
            ProjectedMemberCount = value + 40_000,
            TotalMembersToAdd = value + 50_000,
            TotalMembersToRemove = value + 60_000,
            SourceMembers = new List<AzureADUser>()
        };

        private static AzureADUser User(Guid objectId, Guid sourceGroup = default, params Guid[] sourceGroups) =>
            new AzureADUser
            {
                ObjectId = objectId,
                SourceGroup = sourceGroup,
                SourceGroups = sourceGroups.Length == 0
                    ? null
                    : new List<Guid>(sourceGroups)
            };

        private static AzureADTeamsUser TeamsUser(Guid objectId, string conversationMemberId) =>
            new AzureADTeamsUser
            {
                ObjectId = objectId,
                ConversationMemberId = conversationMemberId
            };

        private static string GetPropertiesJson(object properties)
        {
            if (properties == null)
            {
                return null;
            }

            return properties is JsonElement element
                ? element.GetRawText()
                : JsonSerializer.Serialize(properties, properties.GetType());
        }

        private static Guid ParseGuid(int value) =>
            Guid.Parse($"00000000-0000-0000-0000-{value:X12}");

        private enum MembershipFileFormat
        {
            RawJson,
            Base64Brotli
        }

        private sealed record FileInput(
            string Path,
            MembershipMergeInputKind Kind,
            MembershipFileFormat Format,
            IReadOnlyList<AzureADUser> Members);

        private sealed record ExpectedMember(
            Guid ObjectId,
            IReadOnlyList<Guid> SourceGroups,
            string PropertiesJson);

        private sealed record ExpectedDelta(
            IReadOnlyList<ExpectedMember> Additions,
            IReadOnlyList<ExpectedMember> Removals,
            int SourceMemberCount,
            int DestinationMemberCount,
            MembershipDeltaStatus Status);
    }
}
