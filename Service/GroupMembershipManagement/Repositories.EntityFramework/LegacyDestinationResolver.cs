// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.EntityFramework
{
    public class LegacyDestinationResolver : IDestinationResolver
    {
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;

        public LegacyDestinationResolver(
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository)
        {
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
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
