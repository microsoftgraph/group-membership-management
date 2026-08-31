// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;

namespace Repositories.EntityFramework.Tests
{
    [TestClass]
    public class LegacyDestinationResolverTests
    {
        private readonly Mock<IDatabaseGroupsRepository> groupsRepository = new();
        private readonly Mock<IDatabaseChannelsRepository> channelsRepository = new();
        private LegacyDestinationResolver resolver = null!;

        [TestInitialize]
        public void Initialize()
        {
            resolver = new LegacyDestinationResolver(groupsRepository.Object, channelsRepository.Object);
        }

        [TestMethod]
        public async Task ResolveAsync_WithValidGroupMembership_ReturnsResolvedGroupDestination()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                MembershipType = "GroupMembership",
                Group = new Group { GroupId = groupId }
            };

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsInstanceOfType<ResolvedGroupDestination>(destination);
            var groupDestination = (ResolvedGroupDestination)destination;
            Assert.AreEqual(DestinationType.Group, groupDestination.DestinationType);
            Assert.AreEqual(syncJobId, groupDestination.SyncJobId);
            Assert.AreEqual(groupId, groupDestination.ObjectId);
        }

        [TestMethod]
        public async Task ResolveAsync_WithValidTeamsChannelMembership_ReturnsResolvedTeamsChannelDestination()
        {
            var syncJobId = Guid.NewGuid();
            var teamObjectId = Guid.NewGuid();
            const string channelId = "19:channel@thread.tacv2";
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                MembershipType = "TeamsChannelMembership",
                Channel = new Channel { GroupId = teamObjectId, ChannelId = channelId }
            };

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsInstanceOfType<ResolvedTeamsChannelDestination>(destination);
            var teamsChannelDestination = (ResolvedTeamsChannelDestination)destination;
            Assert.AreEqual(DestinationType.TeamsChannel, teamsChannelDestination.DestinationType);
            Assert.AreEqual(syncJobId, teamsChannelDestination.SyncJobId);
            Assert.AreEqual(teamObjectId, teamsChannelDestination.TeamObjectId);
            Assert.AreEqual(channelId, teamsChannelDestination.ChannelId);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task ResolveAsync_WithNullOrWhitespaceMembershipType_ReturnsNull(string membershipType)
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = membershipType,
                Group = new Group { GroupId = Guid.NewGuid() },
                Channel = new Channel { GroupId = Guid.NewGuid(), ChannelId = "channel" }
            };

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsNull(destination);
        }

        [TestMethod]
        public async Task ResolveAsync_WithTeamsChannelMembershipAndNullChannel_ReturnsNull()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = "TeamsChannelMembership",
                Channel = null
            };

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsNull(destination);
        }

        [TestMethod]
        public async Task ResolveAsync_WithGroupMembershipAndNullGroup_ReturnsNull()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Group = null
            };

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsNull(destination);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        public async Task ResolveAsync_WithTeamsChannelMembershipAndNullOrEmptyChannelId_ResolvesChannelIdAsIs(string channelId)
        {
            var syncJobId = Guid.NewGuid();
            var teamObjectId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                MembershipType = "TeamsChannelMembership",
                Channel = new Channel { GroupId = teamObjectId, ChannelId = channelId }
            };

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsInstanceOfType<ResolvedTeamsChannelDestination>(destination);
            var teamsChannelDestination = (ResolvedTeamsChannelDestination)destination;
            Assert.AreEqual(syncJobId, teamsChannelDestination.SyncJobId);
            Assert.AreEqual(teamObjectId, teamsChannelDestination.TeamObjectId);
            Assert.AreEqual(channelId, teamsChannelDestination.ChannelId);
        }

        [TestMethod]
        public async Task ResolveAsync_WithUnsupportedMembershipType_ReturnsNull()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = "SomethingElse",
                Group = new Group { GroupId = Guid.NewGuid() },
                Channel = new Channel { GroupId = Guid.NewGuid(), ChannelId = "channel" }
            };

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsNull(destination);
        }

        [TestMethod]
        public async Task ResolveAsync_WithGroupMembershipAndUnloadedGroup_HydratesFromDatabase()
        {
            var syncJobId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                MembershipType = "GroupMembership",
                Group = null
            };
            groupsRepository
                .Setup(x => x.GetGroupUsingSyncJobIdAsync(syncJobId))
                .ReturnsAsync(new Group { GroupId = groupId, SyncJobId = syncJobId });

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsInstanceOfType<ResolvedGroupDestination>(destination);
            var groupDestination = (ResolvedGroupDestination)destination;
            Assert.AreEqual(syncJobId, groupDestination.SyncJobId);
            Assert.AreEqual(groupId, groupDestination.ObjectId);
            groupsRepository.Verify(x => x.GetGroupUsingSyncJobIdAsync(syncJobId), Times.Once);
        }

        [TestMethod]
        public async Task ResolveAsync_WithTeamsChannelMembershipAndUnloadedChannel_HydratesFromDatabase()
        {
            var syncJobId = Guid.NewGuid();
            var teamObjectId = Guid.NewGuid();
            const string channelId = "19:channel@thread.tacv2";
            var syncJob = new SyncJob
            {
                Id = syncJobId,
                MembershipType = "TeamsChannelMembership",
                Channel = null
            };
            channelsRepository
                .Setup(x => x.GetChannelUsingSyncJobIdAsync(syncJobId))
                .ReturnsAsync(new Channel { GroupId = teamObjectId, ChannelId = channelId, SyncJobId = syncJobId });

            var destination = await resolver.ResolveAsync(syncJob);

            Assert.IsInstanceOfType<ResolvedTeamsChannelDestination>(destination);
            var teamsChannelDestination = (ResolvedTeamsChannelDestination)destination;
            Assert.AreEqual(syncJobId, teamsChannelDestination.SyncJobId);
            Assert.AreEqual(teamObjectId, teamsChannelDestination.TeamObjectId);
            Assert.AreEqual(channelId, teamsChannelDestination.ChannelId);
            channelsRepository.Verify(x => x.GetChannelUsingSyncJobIdAsync(syncJobId), Times.Once);
        }

        [TestMethod]
        public async Task ResolveAsync_WithLoadedGroup_DoesNotQueryDatabase()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = "GroupMembership",
                Group = new Group { GroupId = Guid.NewGuid() }
            };

            await resolver.ResolveAsync(syncJob);

            groupsRepository.Verify(x => x.GetGroupUsingSyncJobIdAsync(It.IsAny<Guid>()), Times.Never);
        }
    }
}
