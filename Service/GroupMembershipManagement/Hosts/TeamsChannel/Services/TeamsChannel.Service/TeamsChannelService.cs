// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;
using Microsoft.Graph.Models;
using Models;
using Models.Entities;
using Models.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
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
        private readonly IFeatureManager _featureManager;
        private readonly IConfigurationRefresherProvider _refresherProvider;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;

        public TeamsChannelService(
            ITeamsChannelRepository teamsChannelRepository,
            IBlobStorageRepository blobStorageRepository,
            IHttpClientFactory httpClientFactory,
            ISyncJobRepository syncJobRepository,
            ILoggingRepository loggingRepository,
            IFeatureManager featureManager,
            IConfigurationRefresherProvider refresherProvider,
            IServiceBusQueueRepository serviceBusQueueRepository)
        {
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _logger = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
            _refresherProvider = refresherProvider ?? throw new ArgumentNullException(nameof(refresherProvider));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
        }

        public async Task<(AzureADTeamsChannel parsedChannel, bool isGood)> VerifyChannelAsync(ChannelSyncInfo channelSyncInfo)
        {
            Guid runId = channelSyncInfo.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            var azureADTeamsChannel = GetChannelToRead(channelSyncInfo);

            if (!channelSyncInfo.IsDestinationPart)
            {
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, group {azureADTeamsChannel.ObjectId} and channel {azureADTeamsChannel.ChannelId} is not a destination.", RunId = runId });
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { channelSyncInfo.SyncJob }, SyncStatus.PrivateChannelNotDestination);
                return (azureADTeamsChannel, isGood: false);
            }

            var destType = await _teamsChannelRepository.GetChannelTypeAsync(azureADTeamsChannel, runId);

            if (destType != ChannelMembershipType.Private.ToString())
            {
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, group {azureADTeamsChannel.ObjectId} and channel {azureADTeamsChannel.ChannelId} is not a private channel. It is {destType}.", RunId = runId });
                await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { channelSyncInfo.SyncJob }, SyncStatus.TeamsChannelNotPrivate);
                return (azureADTeamsChannel, isGood: false);
            }

            return (azureADTeamsChannel, isGood: true);
        }

        public Task<List<AzureADTeamsUser>> GetUsersFromTeamAsync(AzureADTeamsChannel azureADTeamsChannel, Guid runId)
        {
            _logger.LogMessageAsync(new LogMessage { Message = $"In Service, reading from group {azureADTeamsChannel.ObjectId} and channel {azureADTeamsChannel.ChannelId}.", RunId = runId });
            return _teamsChannelRepository.ReadUsersFromChannelAsync(azureADTeamsChannel, runId);
        }

        private AzureADTeamsChannel GetChannelToRead(ChannelSyncInfo syncInfo)
        {
            var queryArray = JArray.Parse(syncInfo.SyncJob.Query);
            var thisPart = (queryArray[syncInfo.CurrentPart - 1] as JObject)["source"];
            return new AzureADTeamsChannel
            {
                ObjectId = Guid.Parse(thisPart["group"].Value<string>()),
                ChannelId = thisPart["channel"].Value<string>()
            };
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

            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, uploading {users.Count} users to {fileName}.", RunId = runId });
            await _blobStorageRepository.UploadFileAsync(fileName, JsonConvert.SerializeObject(groupMembership));

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

            if (await CheckFeatureFlagStateAsync("UseServiceBusQueue", refreshAppSettings: true))
                await SendMembershipAggregatorMessageAsync(aggregatorRequest);
            else
                await MakeMembershipAggregatorHTTPRequestAsync(aggregatorRequest);
        }

        public async Task MarkSyncJobAsErroredAsync(SyncJob syncJob)
        {
            await _syncJobRepository.UpdateSyncJobStatusAsync(new[] { syncJob }, SyncStatus.Error);
        }

        private async Task<bool> CheckFeatureFlagStateAsync(string featureFlagName, bool refreshAppSettings = false, Guid? runId = null)
        {
            if (refreshAppSettings)
            {
                var refresher = _refresherProvider.Refreshers.First();
                if (!await refresher.TryRefreshAsync())
                {
                    await _logger.LogMessageAsync(new LogMessage
                    { Message = $"Unable to refresh app configuration values", RunId = runId },
                    VerbosityLevel.DEBUG);
                }
            }

            var isFlagEnabled = await _featureManager.IsEnabledAsync(featureFlagName);
            await _logger.LogMessageAsync(new LogMessage { Message = $"Feature flag {featureFlagName} is {(isFlagEnabled ? "enabled" : "disabled")}", RunId = runId }, VerbosityLevel.INFO);

            return isFlagEnabled;
        }

        private async Task MakeMembershipAggregatorHTTPRequestAsync(MembershipAggregatorHttpRequest request)
        {
            // we could use typed clients here instead, i'd prefer doing this in DI https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-7.0
            // but that feels like overkill when this is probably going to become a durable function eventually.
            // also, it feels like a good idea to me to only create an httpClient if we're actually going to use it
            var httpClient = _httpClientFactory.CreateClient(Constants.MembershipAggregatorHttpClientName);

            await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, making HTTP request to {httpClient.BaseAddress}.", RunId = request.SyncJob.RunId });
            var response = await httpClient.PostAsJsonAsync(httpClient.BaseAddress, request);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, successfully made POST request to {httpClient.BaseAddress}. Status Code: {response.StatusCode}", RunId = request.SyncJob.RunId });
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                await _logger.LogMessageAsync(new LogMessage { Message = $"In Service, POST request failed. Got {response.StatusCode} instead. Response body: {responseBody}.", RunId = request.SyncJob.RunId });
                await MarkSyncJobAsErroredAsync(request.SyncJob);
            }
        }

        private async Task SendMembershipAggregatorMessageAsync(MembershipAggregatorHttpRequest request)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

            var message = new ServiceBusMessage
            {
                MessageId = $"{request.SyncJob.RowKey}_{request.SyncJob.RunId}_{Guid.NewGuid()}",
                Body = body
            };

            await _serviceBusQueueRepository.SendMessageAsync(message);

            await _logger.LogMessageAsync(new LogMessage
            {
                Message = $"Sent message {message.MessageId} to membership aggregator",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.INFO);
        }
    }
}
