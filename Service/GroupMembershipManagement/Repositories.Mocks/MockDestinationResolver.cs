// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    /// <summary>
    /// Test double for <see cref="IDestinationResolver"/> that mirrors the production
    /// LegacyDestinationResolver semantics (including the loaded-navigation-property /
    /// database-fallback behavior) using the in-memory mock repositories.
    /// </summary>
    public class MockDestinationResolver : IDestinationResolver
    {
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;

        public MockDestinationResolver(
            IDatabaseGroupsRepository databaseGroupsRepository = null,
            IDatabaseChannelsRepository databaseChannelsRepository = null)
        {
            _databaseGroupsRepository = databaseGroupsRepository ?? new MockDatabaseGroupsRepository();
            _databaseChannelsRepository = databaseChannelsRepository ?? new MockDatabaseChannelsRepository();
        }

        public async Task<ResolvedDestination> ResolveAsync(SyncJob syncJob, CancellationToken cancellationToken = default)
        {
            if (syncJob == null || string.IsNullOrWhiteSpace(syncJob.MembershipType))
            {
                return null;
            }

            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                if (channel == null)
                {
                    return null;
                }

                return new ResolvedTeamsChannelDestination
                {
                    SyncJobId = syncJob.Id,
                    TeamObjectId = channel.GroupId,
                    ChannelId = channel.ChannelId
                };
            }

            if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var group = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                if (group == null)
                {
                    return null;
                }

                return new ResolvedGroupDestination
                {
                    SyncJobId = syncJob.Id,
                    ObjectId = group.GroupId
                };
            }

            return null;
        }
    }
}
