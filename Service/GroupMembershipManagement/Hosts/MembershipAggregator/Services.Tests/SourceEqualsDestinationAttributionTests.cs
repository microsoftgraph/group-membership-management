// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using MembershipAggregator.Services.Entities;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    /// <summary>
    /// FR-010 owner decision (I14): GMO now sets SourceGroup on a source part whose group equals the destination
    /// (only the destination part is left without one). The merge decides membership only from its
    /// included/excluded/destination inputs, so the included/excluded result is identical before and after; only the
    /// source attribution of additions changes.
    /// </summary>
    [TestClass]
    public sealed class SourceEqualsDestinationAttributionTests
    {
        private static readonly Guid DestinationGroup = Guid.Parse("00000000-0000-0000-0000-00000000d000");
        private static readonly Guid OtherSource = Guid.Parse("00000000-0000-0000-0000-00000000a000");

        [TestMethod]
        public async Task SourcePartEqualToDestination_WithSourceGroup_ProducesIdenticalMembershipResult()
        {
            var before = await MergeAsync(sourcePartSourceGroup: Guid.Empty, "before");
            var after = await MergeAsync(sourcePartSourceGroup: DestinationGroup, "after");

            Assert.AreEqual(before.Result.Status, after.Result.Status);
            Assert.AreEqual(before.Result.SourceMemberCount, after.Result.SourceMemberCount);
            Assert.AreEqual(before.Result.DestinationMemberCount, after.Result.DestinationMemberCount);
            Assert.AreEqual(before.Result.AddCount, after.Result.AddCount);
            Assert.AreEqual(before.Result.RemoveCount, after.Result.RemoveCount);
            CollectionAssert.AreEqual(Ids(before.Additions), Ids(after.Additions), "Included members changed.");
            CollectionAssert.AreEqual(Ids(before.Removals), Ids(after.Removals), "Removed members changed.");

            // The expected result itself: members 1 (already in destination) and 4 (excluded) are not added;
            // 2 and 3 are added; 5 is removed (in the destination, not included).
            CollectionAssert.AreEqual(new[] { Id(2), Id(3) }, Ids(after.Additions));
            CollectionAssert.AreEqual(new[] { Id(5) }, Ids(after.Removals));

            // Only the attribution of the added member read from the source part changes.
            var addedFromSourcePartBefore = before.Additions.Single(m => m.ObjectId == Id(2));
            var addedFromSourcePartAfter = after.Additions.Single(m => m.ObjectId == Id(2));
            CollectionAssert.AreEqual(new[] { Guid.Empty }, addedFromSourcePartBefore.SourceGroups.ToArray());
            CollectionAssert.AreEqual(new[] { DestinationGroup }, addedFromSourcePartAfter.SourceGroups.ToArray());
        }

        private static async Task<MergeOutcome> MergeAsync(Guid sourcePartSourceGroup, string testId)
        {
            var store = new MembershipMergeTestStore();
            var envelope = new GroupMembership
            {
                Destination = new AzureADGroup { ObjectId = DestinationGroup },
                RunId = Guid.Parse("00000000-0000-0000-0000-000000000077"),
                SyncJobId = Guid.Parse("00000000-0000-0000-0000-000000000078"),
                Query = "query",
                SourceMembers = new List<AzureADUser>()
            };

            // Part 1: a source part whose group is the destination group itself.
            store.AddRaw("source-equals-destination", envelope, Sorted(
                User(Id(1), sourcePartSourceGroup),
                User(Id(2), sourcePartSourceGroup),
                User(Id(4), sourcePartSourceGroup)));
            // Part 2: another source part.
            store.AddRaw("other-source", envelope, Sorted(User(Id(3), OtherSource)));
            // Part 3: an exclusionary part.
            store.AddRaw("exclusion", envelope, Sorted(User(Id(4), OtherSource)));
            // Part 4: the destination part, read without SourceGroup.
            store.AddRaw("destination", envelope, Sorted(User(Id(1), Guid.Empty), User(Id(5), Guid.Empty)));

            var request = new MembershipMergeRequest(
                new[]
                {
                    MembershipMergeInput.FromPath("source-equals-destination", MembershipMergeInputKind.Included),
                    MembershipMergeInput.FromPath("other-source", MembershipMergeInputKind.Included),
                    MembershipMergeInput.FromPath("exclusion", MembershipMergeInputKind.Excluded),
                    MembershipMergeInput.FromPath("destination", MembershipMergeInputKind.Destination)
                },
                $"source-equals-destination-output/{testId}",
                $"source-equals-destination-{testId}",
                Guid.Parse("00000000-0000-0000-0000-000000000079"),
                envelope);
            var engine = new MembershipMergeEngine(store.Repository.Object, Options.Create(new MembershipMergeOptions()));
            var result = await engine.ExecuteAsync(request, new MembershipMergeDiagnostics());
            return new MergeOutcome(
                result,
                await store.ReadMembersAsync(result.AdditionsPath),
                await store.ReadMembersAsync(result.RemovalsPath));
        }

        private static Guid[] Ids(IEnumerable<AzureADUser> members) => members.Select(m => m.ObjectId).ToArray();

        private static AzureADUser[] Sorted(params AzureADUser[] members) =>
            members.OrderBy(m => m.ObjectId, CanonicalObjectIdComparer.Instance).ToArray();

        private static AzureADUser User(Guid objectId, Guid sourceGroup) =>
            new AzureADUser { ObjectId = objectId, SourceGroup = sourceGroup };

        private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:X12}");

        private sealed record MergeOutcome(
            MembershipMergeResult Result,
            IReadOnlyList<AzureADUser> Additions,
            IReadOnlyList<AzureADUser> Removals);
    }
}
