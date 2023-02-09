using Models.Entities;
using Repositories.Contracts;
using TeamsChannel.Service.Contracts;

namespace TeamsChannel.Service
{
    public class TeamsChannelService : ITeamsChannelService
    {
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly ILoggingRepository _logger;

        public TeamsChannelService(ILoggingRepository loggingRepository, ITeamsChannelRepository teamsChannelRepository)
        {
            _teamsChannelRepository = teamsChannelRepository;
            _logger = loggingRepository;
        }

        public Task<IEnumerable<AzureADTeamsUser>> GetUsersFromTeam(Guid groupId, string channelId)
        {
            return _teamsChannelRepository.ReadUsersFromChannel(groupId, channelId);
        }


    }
}