using Entities;
using Models.Entities;
using Newtonsoft.Json.Linq;
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

        public Task<IEnumerable<AzureADTeamsUser>> GetUsersFromTeam(ChannelSyncInfo channelSyncInfo)
        {
            Guid runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            var azureADTeamsChannel = GetChannelToRead(channelSyncInfo);
            _logger.LogMessageAsync(new LogMessage { Message = $"In Service, reading from group {azureADTeamsChannel.ObjectId} and channel {azureADTeamsChannel.ChannelId}.", RunId = runId }, VerbosityLevel.DEBUG);
            return _teamsChannelRepository.ReadUsersFromChannel(azureADTeamsChannel, runId);
        }

        private AzureADTeamsChannel GetChannelToRead(ChannelSyncInfo syncInfo)
        {
            var queryArray = JArray.Parse(syncInfo.SyncJob.Query);
            var thisPart = queryArray[syncInfo.CurrentPart - 1] as JObject;
            return new AzureADTeamsChannel
            {
                ObjectId = thisPart["group"].Value<Guid>(),
                ChannelId = thisPart["channel"].Value<string>()
            };

        }

    }
}