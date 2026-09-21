// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.EntityFramework.Contexts;
using System;
using System.Threading.Tasks;

namespace Repositories.EntityFramework.Tests
{
    /// <summary>
    /// T019: Consolidated <see cref="DestinationEntityResolver"/> equivalence tests.
    /// Typed group and Teams-channel identity must match the legacy resolver for equivalent
    /// records, and missing / wrong-type / both-type structures must be rejected with no
    /// per-job fallback (FR-016/FR-017, resolver-json-cutover.md Section Resolver behavior).
    /// The consolidated model is exercised over the EF in-memory provider.
    /// </summary>
    [TestClass]
    public class DestinationEntityResolverTests
    {
        private static readonly Guid GroupTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid ChannelTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

        private static GMMReadContext NewReadContext()
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseInMemoryDatabase($"resolver-{Guid.NewGuid()}")
                .Options;

            var context = new GMMReadContext(options);
            context.MembershipTypes.Add(new MembershipType { Id = GroupTypeId, Name = MembershipTypes.GroupMembership });
            context.MembershipTypes.Add(new MembershipType { Id = ChannelTypeId, Name = MembershipTypes.TeamsChannelMembership });
            context.SaveChanges();
            return context;
        }

        private static void SeedGroup(GMMReadContext context, Guid syncJobId, Guid objectId)
        {
            context.Destinations.Add(new Destination { SyncJobId = syncJobId, DestinationType = MembershipTypes.GroupMembership.ToString() });
            context.GroupDestinations.Add(new GroupDestination { SyncJobId = syncJobId, GroupId = objectId });
            context.SaveChanges();
        }

        private static void SeedChannel(GMMReadContext context, Guid syncJobId, Guid teamObjectId, string channelId)
        {
            context.Destinations.Add(new Destination { SyncJobId = syncJobId, DestinationType = MembershipTypes.TeamsChannelMembership.ToString() });
            context.TeamsChannelDestinations.Add(new TeamsChannelDestination
            {
                SyncJobId = syncJobId,
                TeamId = teamObjectId,
                ChannelId = channelId
            });
            context.SaveChanges();
        }

        [TestMethod]
        public async Task ResolveAsync_Group_ReturnsResolvedGroupDestination_EquivalentToLegacy()
        {
            var syncJobId = Guid.NewGuid();
            var objectId = Guid.NewGuid();
            using var context = NewReadContext();
            SeedGroup(context, syncJobId, objectId);

            var consolidated = await new DestinationEntityResolver(context)
                .ResolveAsync(new SyncJob { Id = syncJobId, MembershipType = "GroupMembership" });

            var legacy = await new LegacyDestinationResolver(
                    MockGroups(syncJobId, objectId), MockChannels(null, default, null))
                .ResolveAsync(new SyncJob { Id = syncJobId, MembershipType = "GroupMembership" });

            Assert.IsInstanceOfType<ResolvedGroupDestination>(consolidated);
            var resolved = (ResolvedGroupDestination)consolidated;
            Assert.AreEqual(syncJobId, resolved.SyncJobId);
            Assert.AreEqual(objectId, resolved.ObjectId);

            // Equivalence with the legacy implementation.
            Assert.AreEqual(((ResolvedGroupDestination)legacy).ObjectId, resolved.ObjectId);
            Assert.AreEqual(legacy.DestinationType, resolved.DestinationType);
        }

        [TestMethod]
        public async Task ResolveAsync_Channel_ReturnsResolvedTeamsChannelDestination_EquivalentToLegacy()
        {
            var syncJobId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            const string channelId = "19:abc@thread.tacv2";
            using var context = NewReadContext();
            SeedChannel(context, syncJobId, teamId, channelId);

            var consolidated = await new DestinationEntityResolver(context)
                .ResolveAsync(new SyncJob { Id = syncJobId, MembershipType = "TeamsChannelMembership" });

            var legacy = await new LegacyDestinationResolver(
                    MockGroups(null, default), MockChannels(syncJobId, teamId, channelId))
                .ResolveAsync(new SyncJob { Id = syncJobId, MembershipType = "TeamsChannelMembership" });

            Assert.IsInstanceOfType<ResolvedTeamsChannelDestination>(consolidated);
            var resolved = (ResolvedTeamsChannelDestination)consolidated;
            Assert.AreEqual(syncJobId, resolved.SyncJobId);
            Assert.AreEqual(teamId, resolved.TeamObjectId);
            Assert.AreEqual(channelId, resolved.ChannelId);

            var legacyChannel = (ResolvedTeamsChannelDestination)legacy;
            Assert.AreEqual(legacyChannel.TeamObjectId, resolved.TeamObjectId);
            Assert.AreEqual(legacyChannel.ChannelId, resolved.ChannelId);
        }

        [TestMethod]
        public async Task ResolveAsync_NullSyncJob_ReturnsNull()
        {
            using var context = NewReadContext();
            Assert.IsNull(await new DestinationEntityResolver(context).ResolveAsync(null));
        }

        [TestMethod]
        public async Task ResolveAsync_MissingBaseRow_ReturnsNull_NoFallback()
        {
            using var context = NewReadContext();
            var result = await new DestinationEntityResolver(context)
                .ResolveAsync(new SyncJob { Id = Guid.NewGuid(), MembershipType = "GroupMembership" });
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ResolveAsync_BaseWithoutMatchingPerTypeRow_ReturnsNull()
        {
            var syncJobId = Guid.NewGuid();
            using var context = NewReadContext();
            context.Destinations.Add(new Destination { SyncJobId = syncJobId, DestinationType = MembershipTypes.GroupMembership.ToString() });
            context.SaveChanges();

            var result = await new DestinationEntityResolver(context)
                .ResolveAsync(new SyncJob { Id = syncJobId });
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ResolveAsync_WrongTypePerTypeRow_ReturnsNull()
        {
            // Base says Group but only a Teams-channel per-type row exists.
            var syncJobId = Guid.NewGuid();
            using var context = NewReadContext();
            context.Destinations.Add(new Destination { SyncJobId = syncJobId, DestinationType = MembershipTypes.GroupMembership.ToString() });
            context.TeamsChannelDestinations.Add(new TeamsChannelDestination
            {
                SyncJobId = syncJobId,
                TeamId = Guid.NewGuid(),
                ChannelId = "19:x@thread.tacv2"
            });
            context.SaveChanges();

            var result = await new DestinationEntityResolver(context)
                .ResolveAsync(new SyncJob { Id = syncJobId });
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ResolveAsync_BothPerTypeRows_ReturnsNull()
        {
            var syncJobId = Guid.NewGuid();
            using var context = NewReadContext();
            context.Destinations.Add(new Destination { SyncJobId = syncJobId, DestinationType = MembershipTypes.GroupMembership.ToString() });
            context.GroupDestinations.Add(new GroupDestination { SyncJobId = syncJobId, GroupId = Guid.NewGuid() });
            context.TeamsChannelDestinations.Add(new TeamsChannelDestination
            {
                SyncJobId = syncJobId,
                TeamId = Guid.NewGuid(),
                ChannelId = "19:x@thread.tacv2"
            });
            context.SaveChanges();

            var result = await new DestinationEntityResolver(context)
                .ResolveAsync(new SyncJob { Id = syncJobId });
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ResolveAsync_ChannelWithEmptyChannelId_ReturnsNull()
        {
            var syncJobId = Guid.NewGuid();
            using var context = NewReadContext();
            context.Destinations.Add(new Destination { SyncJobId = syncJobId, DestinationType = MembershipTypes.TeamsChannelMembership.ToString() });
            context.TeamsChannelDestinations.Add(new TeamsChannelDestination
            {
                SyncJobId = syncJobId,
                TeamId = Guid.NewGuid(),
                ChannelId = ""
            });
            context.SaveChanges();

            var result = await new DestinationEntityResolver(context)
                .ResolveAsync(new SyncJob { Id = syncJobId });
            Assert.IsNull(result);
        }

        private static IDatabaseGroupsRepository MockGroups(Guid? syncJobId, Guid groupId)
        {
            var mock = new Moq.Mock<IDatabaseGroupsRepository>();
            if (syncJobId.HasValue)
            {
                mock.Setup(m => m.GetGroupUsingSyncJobIdAsync(syncJobId.Value))
                    .ReturnsAsync(new Group { SyncJobId = syncJobId.Value, GroupId = groupId });
            }
            return mock.Object;
        }

        private static IDatabaseChannelsRepository MockChannels(Guid? syncJobId, Guid teamId, string channelId)
        {
            var mock = new Moq.Mock<IDatabaseChannelsRepository>();
            if (syncJobId.HasValue)
            {
                mock.Setup(m => m.GetChannelUsingSyncJobIdAsync(syncJobId.Value))
                    .ReturnsAsync(new Channel { SyncJobId = syncJobId.Value, GroupId = teamId, ChannelId = channelId });
            }
            return mock.Object;
        }
    }
}
