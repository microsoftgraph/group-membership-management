// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Models;
using Models.Entities;
using Models.ServiceBus;
using Repositories.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TeamsChannelMembershipObtainer.Service.Contracts;


namespace TeamsChannelMembershipObtainer.Service
{
    public class TeamsChannelMembershipObtainerService : ITeamsChannelService
    {
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository;
        private readonly ILoggingRepository _logger;
        private readonly IConfigurationRefresherProvider _refresherProvider;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;

        public TeamsChannelMembershipObtainerService(
            ITeamsChannelRepository teamsChannelRepository,
            IBlobStorageRepository blobStorageRepository,
            IHttpClientFactory httpClientFactory,
            IDatabaseSyncJobsRepository syncJobRepository,
            ILoggingRepository loggingRepository,
            IConfigurationRefresherProvider refresherProvider,
            IServiceBusQueueRepository serviceBusQueueRepository)
        {
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _logger = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _refresherProvider = refresherProvider ?? throw new ArgumentNullException(nameof(refresherProvider));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
        }

        public async Task<ValidateChannelResponse> VerifyChannelAsync(ChannelSyncInfo channelSyncInfo)
        {
            Guid runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            var destinationArray = JsonNode.Parse(channelSyncInfo.SyncJob.Destination).AsArray();
            var currentDestination = destinationArray[0].AsObject()["value"];
            var objectIdNode = currentDestination["objectId"];
            var objectId = Convert.ToString(objectIdNode);
            var channelIdNode = currentDestination.AsObject()["channelId"];
            var channelId = Convert.ToString(channelIdNode);

            var azureADTeamsChannel = new AzureADTeamsChannel
            {
                ObjectId = Guid.Parse(objectId),
                ChannelId = channelId
            };

            if (!channelSyncInfo.IsDestinationPart)
            {
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, group {azureADTeamsChannel.ObjectId} and channel {azureADTeamsChannel.ChannelId} is not a destination.", RunId = runId });
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { channelSyncInfo.SyncJob }, SyncStatus.TeamsChannelNotDestination);
                return new ValidateChannelResponse {
                    ParsedChannel = azureADTeamsChannel,
                    IsValid = false };
            }

            var destType = await _teamsChannelRepository.GetChannelTypeAsync(azureADTeamsChannel, runId);

            if (destType == "standard")
            {
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, channel {azureADTeamsChannel.ChannelId} from group {azureADTeamsChannel.ObjectId} is a standard channel.", RunId = runId });
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { channelSyncInfo.SyncJob }, SyncStatus.StandardTeamsChannel);
                return new ValidateChannelResponse {
                    ParsedChannel = azureADTeamsChannel, 
                    IsValid = false };
            }

            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, Channel {azureADTeamsChannel.ChannelId} of group {azureADTeamsChannel.ObjectId} is of type {destType}.", RunId = runId });

            return new ValidateChannelResponse {
                    ParsedChannel = azureADTeamsChannel, 
                    IsValid = true };
        }

        public Task<List<AzureADTeamsUser>> GetUsersFromTeamAsync(AzureADTeamsChannel azureADTeamsChannel, Guid runId)
        {
            _logger.LogMessageAsync(new LogMessage { Message = $"In Service, reading from group {azureADTeamsChannel.ObjectId} and channel {azureADTeamsChannel.ChannelId}.", RunId = runId });
            return _teamsChannelRepository.ReadUsersFromChannelAsync(azureADTeamsChannel, runId);
        }

        public async Task<string> UploadMembershipAsync(List<AzureADTeamsUser> users, ChannelSyncInfo channelSyncInfo, bool dryRun)
        {
            Guid runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            // for now, convert it to a list of regular AzureADUsers. I think it'll be more useful to get the IDs for removes later on in the chain
            // If need be, I can modify GroupMembership to take either an AzureADUser or AzureADTeamsUser and send that along
            // either with a subclass or something called ChannelMembership or with a generic <T> parameter. The generic parameter would be annoying,
            // since you'd have to change it everywhere someone uses a GroupMembership.
            var groupMembership = new GroupMembership
            {
                SourceMembers = new List<AzureADUser>(users) ?? new List<AzureADUser>(),
                RunId = runId,
                Exclusionary = channelSyncInfo.Exclusionary,
                SyncJobId = channelSyncInfo.SyncJob.Id,
                MembershipObtainerDryRunEnabled = dryRun,
                Query = channelSyncInfo.SyncJob.Query
            };

            var timeStamp = channelSyncInfo.SyncJob.Timestamp.GetValueOrDefault().ToString("MMddyyyy-HHmmss");
            var fileName = $"/{channelSyncInfo.SyncJob.TargetOfficeGroupId}/{timeStamp}_{runId}_TeamsChannelMembership_{channelSyncInfo.CurrentPart}.json";
            var serializerSettings = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            };

            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, uploading {users.Count} users to {fileName}.", RunId = runId });
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(groupMembership, serializerSettings));
            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, uploaded {users.Count} users to {fileName}.", RunId = runId });

            return fileName;
        }

        public async Task MakeMembershipAggregatorRequestAsync(ChannelSyncInfo syncInfo, string blobFilePath)
        {
            var aggregatorRequest = new MembershipAggregatorHttpRequest
            {
                FilePath = blobFilePath,
                PartNumber = syncInfo.CurrentPart,
                PartsCount = syncInfo.TotalParts,
                SyncJob = syncInfo.SyncJob,
                IsDestinationPart = syncInfo.IsDestinationPart
            };

            await SendMembershipAggregatorMessageAsync(aggregatorRequest);
        }

        public async Task SendMessageAsync(SyncJob job)
        {
            await _serviceBusTopicsRepository.AddMessageAsync(job);
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob syncJob, SyncStatus status)
        {
            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, status);
        }

        private async Task SendMembershipAggregatorMessageAsync(MembershipAggregatorHttpRequest request)
        {

            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

            var message = new ServiceBusMessage
            {
                MessageId = $"{request.SyncJob.RowKey}_{request.SyncJob.RunId}_{Guid.NewGuid()}",
                Body = body
            };

            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, sending message {message.MessageId} to membership aggregator.", RunId = request.SyncJob.RunId });

            await _serviceBusQueueRepository.SendMessageAsync(message);

            await _logger.LogMessageAsync(new LogMessage
            {
                Message = $"Sent message {message.MessageId} to membership aggregator.",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.INFO);
        }
    }
}
