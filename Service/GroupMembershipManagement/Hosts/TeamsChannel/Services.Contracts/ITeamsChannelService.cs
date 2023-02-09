using Models.Entities;

namespace TeamsChannel.Service.Contracts
{
    public interface ITeamsChannelService
    {
        public Task<IEnumerable<AzureADTeamsUser>> GetUsersFromTeam(Guid groupId, string channelId);

    }
}