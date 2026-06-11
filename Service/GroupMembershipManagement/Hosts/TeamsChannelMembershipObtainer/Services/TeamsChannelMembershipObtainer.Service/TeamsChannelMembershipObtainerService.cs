// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Models;
using Models.Entities;
using Models.ServiceBus;
using Repositories.Contracts;
using Services.Contracts;
using Models.SyncJobHistory;
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
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly ILoggingRepository _logger;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly ISyncJobStatusService _syncJobStatusService;

        public TeamsChannelMembershipObtainerService(
            ITeamsChannelRepository teamsChannelRepository,
            IBlobStorageRepository blobStorageRepository,
            IHttpClientFactory httpClientFactory,
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseChannelsRepository channelsRepository,
            ILoggingRepository loggingRepository,
            IConfigurationRefresherProvider refresherProvider,
            IServiceBusQueueRepository serviceBusQueueRepository,
            ISyncJobStatusService syncJobStatusService)
        {
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseChannelsRepository = channelsRepository ?? throw new ArgumentException(nameof(channelsRepository));
            _logger = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
            _syncJobStatusService = syncJobStatusService ?? throw new ArgumentNullException(nameof(syncJobStatusService));
        }

        public async Task<Channel> GetDestinationAsync(SyncJob syncJob)
        {
            var channel = new Channel();
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                return syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
            }
            return channel;
        }

        public async Task<ValidateChannelResponse> VerifyChannelAsync(ChannelSyncInfo channelSyncInfo)
        {
            Guid runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            var channel = await GetDestinationAsync(channelSyncInfo.SyncJob);

            if (channel.GroupId == Guid.Empty || channel.ChannelId == null)
            {
                await _logger.LogMessageAsync(new LogMessage { Message = $"Unable to get destination details from TeamsChannels table", RunId = runId });
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { channelSyncInfo.SyncJob }, SyncStatus.Error);
                return new ValidateChannelResponse { ParsedChannel = null, IsValid = false };
            }

            var azureADTeamsChannel = new AzureADTeamsChannel
            {
                ObjectId = channel.GroupId,
                ChannelId = channel.ChannelId
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

        public async Task<string> UploadMembershipAsync(List<AzureADTeamsUser> users, ChannelSyncInfo channelSyncInfo, bool dryRun, Guid targetOfficeGroupId)
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
            var fileName = $"/{targetOfficeGroupId}/{timeStamp}_{runId}_TeamsChannelMembership_{channelSyncInfo.CurrentPart}.json";
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

        public async Task UpdateSyncJobStatusAsync(SyncJob syncJob, SyncStatus status)
        {
            var history = new SyncJobHistory
            {
                SyncJobId = syncJob.Id,
                RunId = syncJob.RunId ?? Guid.Empty,
                Status = status.ToString(),
                UpdatedByFunction = "TeamsChannelMembershipObtainer",
                EndTime = status != SyncStatus.InProgress ? DateTime.UtcNow : null,
                UpdatedAt = DateTime.UtcNow
            };

            await _syncJobStatusService.UpdateJobStatusAsync(syncJob, status, history, "TeamsChannelMembershipObtainer");
        }

        private async Task SendMembershipAggregatorMessageAsync(MembershipAggregatorHttpRequest request)
        {

            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

            var message = new ServiceBusMessage
            {
                MessageId = $"{request.SyncJob.Id}_{request.SyncJob.RunId}_{Guid.NewGuid()}",
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
