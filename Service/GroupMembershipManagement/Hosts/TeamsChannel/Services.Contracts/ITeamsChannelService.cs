using Models.Entities;

namespace TeamsChannel.Service.Contracts
{
    public interface ITeamsChannelService
    {
        public Task<(AzureADTeamsChannel parsedChannel, bool isGood)> VerifyChannel(ChannelSyncInfo channelSyncInfo);
        public Task<List<AzureADTeamsUser>> GetUsersFromTeam(AzureADTeamsChannel azureADTeamsChannel, Guid runId);
        public Task<string> UploadMembership(List<AzureADTeamsUser> users, ChannelSyncInfo channelSyncInfo, bool dryRun);
        public Task MakeMembershipAggregatorRequest(ChannelSyncInfo syncInfo, string blobFilePath);
    }
}