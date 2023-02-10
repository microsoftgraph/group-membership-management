using Models.Entities;

namespace TeamsChannel.Service.Contracts
{
    public interface ITeamsChannelService
    {
        public Task<IEnumerable<AzureADTeamsUser>> GetUsersFromTeam(ChannelSyncInfo info);

    }
}