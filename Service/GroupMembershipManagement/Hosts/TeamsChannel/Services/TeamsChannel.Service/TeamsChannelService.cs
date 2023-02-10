using Entities;
using Entities.ServiceBus;
using Models.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using TeamsChannel.Service.Contracts;

namespace TeamsChannel.Service
{
    public class TeamsChannelService : ITeamsChannelService
    {
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILoggingRepository _logger;

        public TeamsChannelService(ILoggingRepository loggingRepository, ITeamsChannelRepository teamsChannelRepository, IBlobStorageRepository blobStorageRepository)
        {
            _teamsChannelRepository = teamsChannelRepository;
            _blobStorageRepository = blobStorageRepository;
            _logger = loggingRepository;
        }

        public Task<List<AzureADTeamsUser>> GetUsersFromTeam(ChannelSyncInfo channelSyncInfo)
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

        public async Task<string> UploadMembership(List<AzureADTeamsUser> users, ChannelSyncInfo channelSyncInfo, bool dryRun)
        {
            Guid runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            // for now, convert it to a list of regular AzureADUsers. I think it'll be more useful to get the IDs for removes later on in the chain
            // If need be, I can modify GroupMembership to take either an AzureADUser or AzureADTeamsUser and send that along
            // either with a subclass or something called ChannelMembership or with a generic <T> parameter. The generic parameter would be annoying,
            // since you'd have to change it everywhere someone uses a GroupMembership. 
            var groupMembership = new GroupMembership
            {
                SourceMembers =  new List<AzureADUser>(users) ?? new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = channelSyncInfo.SyncJob.TargetOfficeGroupId },
                RunId = runId,
                Exclusionary = channelSyncInfo.Exclusionary,
                SyncJobRowKey = channelSyncInfo.SyncJob.RowKey,
                SyncJobPartitionKey = channelSyncInfo.SyncJob.PartitionKey,
                MembershipObtainerDryRunEnabled = dryRun,
                Query = channelSyncInfo.SyncJob.Query
            };


            var timeStamp = channelSyncInfo.SyncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            var fileName = $"/{channelSyncInfo.SyncJob.TargetOfficeGroupId}/{timeStamp}_{runId}_TeamsChannel_{channelSyncInfo.CurrentPart}.json";

            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, uploading {users.Count} users to {fileName}.", RunId = runId }, VerbosityLevel.DEBUG);
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, uploaded {users.Count} users to {fileName}.", RunId = runId }, VerbosityLevel.DEBUG);

            return fileName;

        }

    }
}