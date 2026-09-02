// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using MembershipAggregator.Services.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public sealed class MembershipMergeEngineParityTests
    {
        [TestMethod]
        public async Task MergeMatchesTheCurrentAlgorithmForKnownEdgeCases()
        {
            var firstSourceGroup = ParseGuid(101);
            var secondSourceGroup = ParseGuid(102);
            var thirdSourceGroup = ParseGuid(103);
            var unchanged = ParseGuid(1);
            var added = ParseGuid(2);
            var removed = ParseGuid(3);
            var excluded = ParseGuid(4);
            var excludedFromDestination = ParseGuid(5);

            var inputs = new[]
            {
                Input(
                    "include-1",
                    MembershipMergeInputKind.Included,
                    User(unchanged, firstSourceGroup),
                    User(added, firstSourceGroup),
                    User(excluded, firstSourceGroup),
                    User(excludedFromDestination, firstSourceGroup)),
                Input(
                    "include-2",
                    MembershipMergeInputKind.Included,
                    User(added, secondSourceGroup),
                    User(added, thirdSourceGroup),
                    User(unchanged, secondSourceGroup)),
                Input(
                    "exclude",
                    MembershipMergeInputKind.Excluded,
                    User(excluded, thirdSourceGroup),
                    User(excludedFromDestination, thirdSourceGroup)),
                Input(
                    "destination",
                    MembershipMergeInputKind.Destination,
                    User(unchanged),
                    User(removed),
                    User(excludedFromDestination))
            };

            var expected = await LegacyDeltaAsync(inputs);
            var actual = await MergeDeltaAsync(inputs);

            AssertDeltaEqual(expected, actual);
        }

        [TestMethod]
        public async Task MergePreservesEmptyAndDistinctSourceGroups()
        {
            var objectId = ParseGuid(1);
            var sourceGroup = ParseGuid(101);
            var inputs = new[]
            {
                Input(
                    "include-1",
                    MembershipMergeInputKind.Included,
                    User(objectId, Guid.Empty),
                    User(objectId, sourceGroup),
                    User(objectId, sourceGroup)),
                Input(
                    "include-2",
                    MembershipMergeInputKind.Included,
                    User(objectId, Guid.Empty))
            };

            var actual = await MergeDeltaAsync(inputs);

            Assert.AreEqual(1, actual.Additions.Count);
            CollectionAssert.AreEquivalent(
                new[] { Guid.Empty, sourceGroup },
                actual.Additions[0].SourceGroups);
        }

        [TestMethod]
        public async Task MergeUnionsAllSourceGroupsAndCollapsesDuplicates()
        {
            var objectId = ParseGuid(1);
            var firstSourceGroup = ParseGuid(101);
            var secondSourceGroup = ParseGuid(102);
            var thirdSourceGroup = ParseGuid(103);
            var inputs = new[]
            {
                MembershipMergeInput.FromMembers(
                    "include-1",
                    MembershipMergeInputKind.Included,
                    new[]
                    {
                        new AzureADUser
                        {
                            ObjectId = objectId,
                            SourceGroup = firstSourceGroup,
                            SourceGroups = new List<Guid>
                            {
                                firstSourceGroup,
                                secondSourceGroup,
                                secondSourceGroup
                            }
                        }
                    }),
                Input(
                    "include-2",
                    MembershipMergeInputKind.Included,
                    User(objectId, thirdSourceGroup))
            };

            var actual = await MergeDeltaAsync(inputs);

            CollectionAssert.AreEquivalent(
                new[] { firstSourceGroup, secondSourceGroup, thirdSourceGroup },
                actual.Additions.Single().SourceGroups);
        }

        [TestMethod]
        public async Task MergeSetsTheActionOnEveryDeltaMember()
        {
            var added = ParseGuid(1);
            var removed = ParseGuid(2);
            var inputs = new[]
            {
                Input("include", MembershipMergeInputKind.Included, User(added, ParseGuid(101))),
                Input("destination", MembershipMergeInputKind.Destination, User(removed))
            };

            var actual = await MergeDeltaAsync(inputs);

            Assert.IsTrue(actual.Additions.All(member => member.MembershipAction == MembershipAction.Add));
            Assert.IsTrue(actual.Removals.All(member => member.MembershipAction == MembershipAction.Remove));
            CollectionAssert.AreEquivalent(
                new[] { added },
                actual.Additions
                    .Where(member => member.MembershipAction == MembershipAction.Add)
                    .Distinct()
                    .Select(member => member.ObjectId)
                    .ToList());
            CollectionAssert.AreEquivalent(
                new[] { removed },
                actual.Removals
                    .Where(member => member.MembershipAction == MembershipAction.Remove)
                    .Distinct()
                    .Select(member => member.ObjectId)
                    .ToList());
        }

        [TestMethod]
        public async Task MergeMatchesTheCurrentAlgorithmAcrossRandomInputs()
        {
            const int iterations = 2_000;
            var random = new Random(837_421);

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var inputs = RandomInputs(random, iteration);
                var expected = await LegacyDeltaAsync(inputs);
                var actual = await MergeDeltaAsync(inputs);

                try
                {
                    AssertDeltaEqual(expected, actual);
                }
                catch (AssertFailedException exception)
                {
                    Assert.Fail($"Iteration {iteration} failed: {exception.Message}");
                }
            }
        }

        private static async Task<Delta> MergeDeltaAsync(IReadOnlyList<MembershipMergeInput> inputs)
        {
            var repository = new Moq.Mock<IBlobStorageRepository>();
            var engine = new MembershipMergeEngine(
                repository.Object,
                Microsoft.Extensions.Options.Options.Create(new MembershipMergeOptions()));
            var additions = new List<AzureADUser>();
            var removals = new List<AzureADUser>();
            var sourceMemberCount = 0;
            var destinationMemberCount = 0;

            await foreach (var record in engine.MergeRecordsAsync(inputs, new MembershipMergeDiagnostics()))
            {
                if (record.IsSourceMember) sourceMemberCount++;
                if (record.InDestination) destinationMemberCount++;
                if (!record.TryCreateDeltaMember(out var member))
                {
                    continue;
                }

                if (member.MembershipAction == MembershipAction.Add)
                {
                    additions.Add(member);
                }
                else
                {
                    removals.Add(member);
                }
            }

            var status = additions.Count == 0 && removals.Count == 0
                ? MembershipDeltaStatus.NoChanges
                : MembershipDeltaStatus.Ok;
            return new Delta(
                additions,
                removals,
                sourceMemberCount,
                destinationMemberCount,
                status);
        }

        private static async Task<Delta> LegacyDeltaAsync(IReadOnlyList<MembershipMergeInput> inputs)
        {
            var included = await ReadMembersAsync(inputs, MembershipMergeInputKind.Included);
            var excluded = await ReadMembersAsync(inputs, MembershipMergeInputKind.Excluded);
            var destination = await ReadMembersAsync(inputs, MembershipMergeInputKind.Destination);

            var includedAfterExclusions = included.Except(excluded).ToList();
            var includedIds = new HashSet<Guid>(includedAfterExclusions.Select(member => member.ObjectId));
            var source = included
                .Concat(excluded)
                .GroupBy(member => member.ObjectId)
                .Select(group => new AzureADUser
                {
                    ObjectId = group.Key,
                    SourceGroups = group.Select(member => member.SourceGroup).Distinct().ToList()
                })
                .Where(member => includedIds.Contains(member.ObjectId))
                .ToList();

            var additions = new HashSet<AzureADUser>(source);
            additions.ExceptWith(destination);
            foreach (var member in additions)
            {
                member.MembershipAction = MembershipAction.Add;
            }

            var removals = new HashSet<AzureADUser>(destination);
            removals.ExceptWith(source);
            foreach (var member in removals)
            {
                member.MembershipAction = MembershipAction.Remove;
            }

            var status = additions.Count == 0 && removals.Count == 0
                ? MembershipDeltaStatus.NoChanges
                : MembershipDeltaStatus.Ok;
            return new Delta(
                additions.ToList(),
                removals.ToList(),
                source.Select(member => member.ObjectId).Distinct().Count(),
                destination.Select(member => member.ObjectId).Distinct().Count(),
                status);
        }

        private static async Task<List<AzureADUser>> ReadMembersAsync(
            IEnumerable<MembershipMergeInput> inputs,
            MembershipMergeInputKind kind)
        {
            var repository = new Moq.Mock<IBlobStorageRepository>();
            var members = new List<AzureADUser>();
            foreach (var input in inputs.Where(input => input.Kind == kind))
            {
                await foreach (var member in input.OpenMembers(repository.Object, CancellationToken.None))
                {
                    members.Add(new AzureADUser
                    {
                        ObjectId = member.ObjectId,
                        SourceGroup = member.SourceGroup,
                        SourceGroups = member.SourceGroups == null ? null : new List<Guid>(member.SourceGroups)
                    });
                }
            }

            return members;
        }

        private static void AssertDeltaEqual(Delta expected, Delta actual)
        {
            AssertMembersEqual(expected.Additions, actual.Additions, MembershipAction.Add);
            AssertMembersEqual(expected.Removals, actual.Removals, MembershipAction.Remove);
            Assert.AreEqual(expected.SourceMemberCount, actual.SourceMemberCount);
            Assert.AreEqual(expected.DestinationMemberCount, actual.DestinationMemberCount);
            Assert.AreEqual(expected.Status, actual.Status);
        }

        private static void AssertMembersEqual(
            IReadOnlyCollection<AzureADUser> expected,
            IReadOnlyCollection<AzureADUser> actual,
            MembershipAction action)
        {
            Assert.AreEqual(expected.Count, actual.Count, $"{action} count changed.");

            var expectedById = expected.ToDictionary(member => member.ObjectId);
            var actualById = actual.ToDictionary(member => member.ObjectId);
            CollectionAssert.AreEquivalent(expectedById.Keys.ToList(), actualById.Keys.ToList());

            foreach (var objectId in expectedById.Keys)
            {
                Assert.AreEqual(action, actualById[objectId].MembershipAction);
                CollectionAssert.AreEquivalent(
                    expectedById[objectId].SourceGroups ?? new List<Guid>(),
                    actualById[objectId].SourceGroups ?? new List<Guid>(),
                    $"SourceGroups changed for {objectId}.");
            }
        }

        private static IReadOnlyList<MembershipMergeInput> RandomInputs(Random random, int iteration)
        {
            var inputs = new List<MembershipMergeInput>();
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
                    $"include-{inputIndex}",
                    MembershipMergeInputKind.Included,
                    RandomMembers(random, memberPool, sourceGroupPool)));
            }

            var exclusionCount = random.Next(0, 3);
            for (var inputIndex = 0; inputIndex < exclusionCount; inputIndex++)
            {
                inputs.Add(Input(
                    $"exclude-{inputIndex}",
                    MembershipMergeInputKind.Excluded,
                    RandomMembers(random, memberPool, sourceGroupPool)));
            }

            inputs.Add(Input(
                "destination",
                MembershipMergeInputKind.Destination,
                RandomMembers(random, memberPool, sourceGroupPool)));

            return inputs;
        }

        private static AzureADUser[] RandomMembers(
            Random random,
            IReadOnlyList<Guid> memberPool,
            IReadOnlyList<Guid> sourceGroupPool)
        {
            var members = new List<AzureADUser>();
            var count = random.Next(0, memberPool.Count * 2);
            for (var index = 0; index < count; index++)
            {
                members.Add(User(
                    memberPool[random.Next(memberPool.Count)],
                    sourceGroupPool[random.Next(sourceGroupPool.Count)]));
            }

            return members
                .OrderBy(member => member.ObjectId)
                .ToArray();
        }

        private static MembershipMergeInput Input(
            string name,
            MembershipMergeInputKind kind,
            params AzureADUser[] members)
        {
            var ordered = members.OrderBy(member => member.ObjectId).ToArray();
            return MembershipMergeInput.FromMembers(name, kind, ordered);
        }

        private static AzureADUser User(Guid objectId, Guid sourceGroup = default) => new AzureADUser
        {
            ObjectId = objectId,
            SourceGroup = sourceGroup
        };

        private static Guid ParseGuid(int value) =>
            Guid.Parse($"00000000-0000-0000-0000-{value:X12}");

        private sealed record Delta(
            IReadOnlyList<AzureADUser> Additions,
            IReadOnlyList<AzureADUser> Removals,
            int SourceMemberCount,
            int DestinationMemberCount,
            MembershipDeltaStatus Status);
    }
}
