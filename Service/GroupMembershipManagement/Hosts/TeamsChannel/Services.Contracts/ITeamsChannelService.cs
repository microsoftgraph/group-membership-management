using Models.Entities;

namespace TeamsChannel.Service.Contracts
{
    public interface ITeamsChannelService
    {
        public Task<IEnumerable<AzureADTeamsUser>> GetUsersFromTeam(AzureADTeamsChannel azureADTeamsChannel, Guid runId);

    }
}