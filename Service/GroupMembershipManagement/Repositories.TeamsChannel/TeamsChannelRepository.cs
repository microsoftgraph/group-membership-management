using Microsoft.Graph;
using Models.Entities;
using Repositories.Contracts;

namespace Repositories.TeamsChannel
{
    public class TeamsChannelRepository : ITeamsChannelRepository
    {
        private readonly GraphServiceClient _graphServiceClient;

        public TeamsChannelRepository(GraphServiceClient graphServiceClient)
        {
            _graphServiceClient = graphServiceClient;
        }

        public async Task<IEnumerable<AzureADTeamsUser>> ReadUsersFromChannel(Guid groupId, string channelId)
        {
            return Enumerable.Empty<AzureADTeamsUser>();
        }

    }
}