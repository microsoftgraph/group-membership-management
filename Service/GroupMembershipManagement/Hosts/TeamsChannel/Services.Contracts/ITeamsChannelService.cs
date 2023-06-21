// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;

namespace TeamsChannel.Service.Contracts
{
    public interface ITeamsChannelService
    {
        public Task<(AzureADTeamsChannel parsedChannel, bool isGood)> VerifyChannelAsync(ChannelSyncInfo channelSyncInfo);
        public Task<List<AzureADTeamsUser>> GetUsersFromTeamAsync(AzureADTeamsChannel azureADTeamsChannel, Guid runId);
        public Task<string> UploadMembershipAsync(List<AzureADTeamsUser> users, ChannelSyncInfo channelSyncInfo, bool dryRun);
        public Task MakeMembershipAggregatorRequestAsync(ChannelSyncInfo syncInfo, string blobFilePath);
        public Task UpdateSyncJobStatusAsync(SyncJob syncJob, SyncStatus status);
    }
}
