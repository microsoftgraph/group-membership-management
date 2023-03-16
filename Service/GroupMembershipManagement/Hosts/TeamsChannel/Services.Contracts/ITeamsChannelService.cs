using Models.Entities;

namespace TeamsChannel.Service.Contracts
{
    public interface ITeamsChannelService
    {
        public Task<List<AzureADTeamsUser>> GetUsersFromTeam(ChannelSyncInfo info);
        public Task<string> UploadMembership(List<AzureADTeamsUser> users, ChannelSyncInfo channelSyncInfo, bool dryRun);
        public Task MakeMembershipAggregatorRequest(ChannelSyncInfo syncInfo, string blobFilePath);
    }
}