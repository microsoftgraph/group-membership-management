using Entities;
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

        public Task<IEnumerable<AzureADTeamsUser>> GetUsersFromTeam(AzureADTeamsChannel azureADTeamsChannel, Guid runId)
        {
            _logger.LogMessageAsync(new LogMessage { Message = $"In Service, reading from group {azureADTeamsChannel.ObjectId} and channel {azureADTeamsChannel.ChannelId}.", RunId = runId}, VerbosityLevel.DEBUG)
            return _teamsChannelRepository.ReadUsersFromChannel(azureADTeamsChannel, runId);
        }


    }
}