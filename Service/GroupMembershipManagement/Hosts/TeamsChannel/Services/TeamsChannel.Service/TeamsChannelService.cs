using Entities;
using Entities.ServiceBus;
using Models;
using Models.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System.Net.Http;
using System.Net.Http.Json;
using TeamsChannel.Service.Contracts;

namespace TeamsChannel.Service
{
    public class TeamsChannelService : ITeamsChannelService
    {
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly ILoggingRepository _logger;

        public TeamsChannelService(ITeamsChannelRepository teamsChannelRepository, IBlobStorageRepository blobStorageRepository, IHttpClientFactory httpClientFactory, ISyncJobRepository syncJobRepository, ILoggingRepository loggingRepository)
        {
            _teamsChannelRepository = teamsChannelRepository;
            _blobStorageRepository = blobStorageRepository;
            _httpClientFactory = httpClientFactory;
            _syncJobRepository = syncJobRepository;
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

        public async Task MakeMembershipAggregatorRequest(ChannelSyncInfo syncInfo, string blobFilePath)
        {
            var aggregatorRequest = new MembershipAggregatorHttpRequest
            {
                FilePath = blobFilePath,
                PartNumber = syncInfo.CurrentPart,
                PartsCount = syncInfo.TotalParts,
                SyncJob = syncInfo.SyncJob,
                IsDestinationPart = syncInfo.IsDestinationPart
            };

            // we could use typed clients here instead, i'd prefer doing this in DI https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-7.0
            // but that feels like overkill when this is probably going to become a durable function eventually.
            // also, it feels like a good idea to me to only create an httpClient if we're actually going to use it
            var httpClient = _httpClientFactory.CreateClient(Constants.MembershipAggregatorHttpClientName);

            // add retry logic with Polly here
            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, making HTTP request to {httpClient.BaseAddress}.", RunId = syncInfo.SyncJob.RunId }, VerbosityLevel.DEBUG);
            var response = await httpClient.PostAsJsonAsync(httpClient.BaseAddress, aggregatorRequest);

            if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
            {
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, successfully made POST request to {httpClient.BaseAddress}.", RunId = syncInfo.SyncJob.RunId }, VerbosityLevel.DEBUG);
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, POST request failed. Got {response.StatusCode} instead. Response body: {responseBody}.", RunId = syncInfo.SyncJob.RunId }, VerbosityLevel.DEBUG);
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncInfo.SyncJob }, SyncStatus.Error);
            }

        }

    }
}