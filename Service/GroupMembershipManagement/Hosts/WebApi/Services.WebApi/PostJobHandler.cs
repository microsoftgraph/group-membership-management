// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services
{
    public class PostJobHandler : RequestHandlerBase<PostJobRequest, PostJobResponse>
    {
        private const int DEFAULT_PERIOD = 24;
        private readonly ILogger<PostJobHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseDestinationAttributesRepository _destinationAttributesRepository;
        private readonly IDatabaseTitlesRepository _titlesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly IPendingConfigurationConfig _pendingConfigurationConfig;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;
        private readonly IServiceBusQueueRepository? _autoApproverQueueRepository;

        public PostJobHandler(
            ILogger<PostJobHandler> logger,
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseDestinationAttributesRepository destinationAttributesRepository,
            IDatabaseTitlesRepository titlesRepository,
            IGraphGroupRepository graphGroupRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IPendingConfigurationConfig pendingConfigurationConfig,
            IServiceBusQueueRepository serviceBusQueueRepository,
            [FromKeyedServices("AutoApprover")] IServiceBusQueueRepository? autoApproverQueueRepository = null) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _destinationAttributesRepository = destinationAttributesRepository ?? throw new ArgumentNullException(nameof(destinationAttributesRepository));
            _titlesRepository = titlesRepository ?? throw new ArgumentNullException(nameof(titlesRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _pendingConfigurationConfig = pendingConfigurationConfig ?? throw new ArgumentNullException(nameof(pendingConfigurationConfig));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
            _autoApproverQueueRepository = autoApproverQueueRepository;
        }

        protected override async Task<PostJobResponse> ExecuteCoreAsync(PostJobRequest request)
        {
            var response = new PostJobResponse();
            
            try
            {
                var newSyncJobEntity = MapSyncJobDTOtoEntity(request.NewSyncJob);
                
                var isPendingConfigurationEnabled = _pendingConfigurationConfig.PendingConfigurationIsEnabled;

                // Check if pending configuration feature is enabled, only works for groups
                if (isPendingConfigurationEnabled && newSyncJobEntity.MembershipType == MembershipTypes.GroupMembership.ToString())
                {
                    newSyncJobEntity.Status = SyncStatus.PendingConfiguration.ToString();
                }
                else
                {
                    // Auto-approval is now handled by the AutoApprover function
                }

                var isAITitleEnabled = await IsAITitleEnabledAsync();

                var destinationId = newSyncJobEntity.MembershipType == MembershipTypes.GroupMembership.ToString() ? newSyncJobEntity.Group.GroupId : newSyncJobEntity.Channel.GroupId;
                var userIdentifier = string.IsNullOrEmpty(request.NewSyncJob.LastModifiedOnBehalfOfObjectId) ? request.UserIdentity : request.NewSyncJob.LastModifiedOnBehalfOfObjectId;
                var userResponse = await _graphGroupRepository.GetUserByUpnOrIdAsync(userIdentifier, false);
                newSyncJobEntity.Requestor = userResponse.UserPrincipalName;
                var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, destinationId);
                if (!(isGroupOwner || request.IsJobTenantWriter))
                {
                    response.StatusCode = HttpStatusCode.Forbidden;
                    return response;
                }

                var newSyncJobId = await _syncJobRepository.CreateSyncJobAsync(newSyncJobEntity);

                if (newSyncJobId != Guid.Empty)
                {
                    response.StatusCode = HttpStatusCode.Created;
                    response.NewSyncJobId = newSyncJobId;

                    _logger.JobCreated(newSyncJobId);

                    var destinationName = await _graphGroupRepository.GetGroupNameAsync(destinationId);
                    var destinationEmail = await _graphGroupRepository.GetGroupEmailAsync(destinationId);
                    var ownersDictionary = await _graphGroupRepository.GetDestinationOwnersAsync(new List<Guid> { destinationId });
                    var destinationAttributes = new DestinationAttributes
                    {
                        Id = newSyncJobId,
                        Name = destinationName,
                        Email = destinationEmail,
                        Owners = ownersDictionary.GetValueOrDefault(destinationId)
                    };
                    await _destinationAttributesRepository.UpdateAttributes(destinationAttributes);
                    var changedOnBehalfOfDisplayName = request.NewSyncJob.LastModifiedOnBehalfOfDisplayName;
                    var changedOnBehalfOfObjectId = request.NewSyncJob.LastModifiedOnBehalfOfObjectId;
                    var changeReason = SyncJobChangeReason.Onboarding;

                    await _syncJobChangeRepository.Save(new SyncJobChange
                    {
                        SyncJobId = newSyncJobId,
                        ChangeTime = DateTime.UtcNow,
                        ChangedByObjectId = Guid.Parse(request.UserIdentity),
                        ChangedByDisplayName = request.UserDisplayName,
                        ChangeSource = SyncJobChangeSource.WebApp,
                        ChangeReason = changeReason.ToString(),
                        ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(newSyncJobEntity),
                        BusinessJustification = request.BusinessJustification,
                        ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName != null && changedOnBehalfOfDisplayName != request.UserDisplayName ? changedOnBehalfOfDisplayName : null,
                        ChangedOnBehalfOfObjectId = !string.IsNullOrEmpty(changedOnBehalfOfObjectId) && changedOnBehalfOfObjectId != request.UserIdentity ? new Guid(changedOnBehalfOfObjectId) : (Guid?)null
                    });

                    if (request.NewSyncJob.GroupSettings != null)
                    {
                        await _syncJobChangeRepository.Save(new SyncJobChange
                        {
                            SyncJobId = newSyncJobId,
                            ChangeTime = DateTime.UtcNow,
                            ChangedByObjectId = Guid.Parse(request.UserIdentity),
                            ChangedByDisplayName = request.UserDisplayName,
                            ChangeSource = SyncJobChangeSource.WebApp,
                            ChangeReason = SyncJobChangeReason.GroupSettings.ToString(),
                            ChangeDetails = JsonSerializer.Serialize(request.NewSyncJob.GroupSettings),
                            BusinessJustification = request.BusinessJustification,
                            ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName != null && changedOnBehalfOfDisplayName != request.UserDisplayName ? changedOnBehalfOfDisplayName : null,
                            ChangedOnBehalfOfObjectId = !string.IsNullOrEmpty(changedOnBehalfOfObjectId) && changedOnBehalfOfObjectId != request.UserIdentity ? new Guid(changedOnBehalfOfObjectId) : (Guid?)null
                        });
                    }

                    if (isPendingConfigurationEnabled && newSyncJobEntity.MembershipType == MembershipTypes.GroupMembership.ToString())
                    {
                        var jobConfigurationQueueMessage = new JobConfigurationQueueMessage {
                            JobId = newSyncJobId,
                            GroupId = destinationId,
                            RequestorObjectId = request.UserIdentity,
                            RequestorDisplayName = request.UserDisplayName,
                            ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName,
                            ChangedOnBehalfOfObjectId = changedOnBehalfOfObjectId,
                            BusinessJustification = request.BusinessJustification
                        };
                        var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jobConfigurationQueueMessage));

                        var message = new ServiceBusMessage
                        {
                            MessageId = destinationId.ToString(),
                            Body = body
                        };

                        await _serviceBusQueueRepository.SendMessageAsync(message);

                        _logger.JobConfigurationMessageSent(message.MessageId);
                    }

                    if (!isPendingConfigurationEnabled || newSyncJobEntity.MembershipType != MembershipTypes.GroupMembership.ToString())
                    {
                        if (_autoApproverQueueRepository != null && newSyncJobEntity.Status == SyncStatus.PendingReview.ToString())
                        {
                            var autoApprovalMessage = new AutoApprovalQueueMessage
                            {
                                SyncJobId = newSyncJobId,
                                RequestorObjectId = request.UserIdentity,
                                RequestorDisplayName = request.UserDisplayName,
                                ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName,
                                ChangedOnBehalfOfObjectId = changedOnBehalfOfObjectId,
                                BusinessJustification = request.BusinessJustification
                            };

                            var autoApprovalBody = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(autoApprovalMessage));
                            var autoApprovalServiceBusMessage = new ServiceBusMessage
                            {
                                MessageId = newSyncJobId.ToString(),
                                Body = autoApprovalBody
                            };

                            await _autoApproverQueueRepository.SendMessageAsync(autoApprovalServiceBusMessage);

                            _logger.AutoApproverMessageSent(autoApprovalServiceBusMessage.MessageId);
                        }
                    }

                    if (isAITitleEnabled && request.NewSyncJob.Titles != null)
                    {
                        var titlesDictionary = request.NewSyncJob.Titles.ToDictionary(t => t.PartId, t => t.Name);
                        await _titlesRepository.SaveTitlesAsync(titlesDictionary, newSyncJobId);
                    }
                }
                else
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = "JobCreationFailed";
                    _logger.JobCreationReturnedEmptyId();
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                _logger.JobCreationFailed(ex);
            }

            return response;
        }


        private async Task<bool> IsAITitleEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAITitleEnabled);
                return setting != null && bool.TryParse(setting.SettingValue, out bool result) && result;
            }
            catch (Exception ex)
            {
                _logger.AITitleSettingRetrievalFailed(ex);
                return false;
            }
        }

        private static SyncJob MapSyncJobDTOtoEntity(NewSyncJobDTO syncJob)
        {
            var queryObject = JsonDocument.Parse(syncJob.Query);
            var convertedQuery = JsonSerializer.Serialize(
                JsonDocument.Parse(syncJob.Query),
                new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                });


            var destinationArray = JsonSerializer.Deserialize<List<JsonElement>>(syncJob.Destination);
            string? targetOfficeGroupId = null;
            string? channelId = null;
            string? type = null;

            var destination = destinationArray?.FirstOrDefault();
            if (destination != null && destination.Value.TryGetProperty("value", out JsonElement value))
            {
                if (value.TryGetProperty("objectId", out JsonElement objectId))
                {
                    targetOfficeGroupId = objectId.GetString();
                }

                if (value.TryGetProperty("channelId", out JsonElement channelIdElement))
                {
                    channelId = channelIdElement.GetString();
                }
            }

            type = destination != null ? destination.Value.GetProperty("type").GetString() : null;

            var targetGroupId = !string.IsNullOrEmpty(targetOfficeGroupId) ? new Guid(targetOfficeGroupId) : Guid.Empty;
            string membershipType = !string.IsNullOrEmpty(type) ? type : MembershipTypes.GroupMembership.ToString();

            return new SyncJob
            {
                Id = new Guid(),
                TargetOfficeGroupId = targetGroupId,
                Destination = syncJob.Destination,
                Requestor = syncJob.Requestor,
                StartDate = DateTime.Parse(syncJob.StartDate),
                Period = syncJob.Period == DEFAULT_PERIOD ? syncJob.Period : DEFAULT_PERIOD,
                Query = convertedQuery,
                ThresholdPercentageForAdditions = syncJob.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = syncJob.ThresholdPercentageForRemovals,
                Status = SyncStatus.PendingReview.ToString(),
                MembershipType = membershipType,
                Channel = membershipType == MembershipTypes.TeamsChannelMembership.ToString() ? new Channel { GroupId = targetGroupId, ChannelId = channelId } : null,
                Group = membershipType == MembershipTypes.GroupMembership.ToString() ? new Group { GroupId = targetGroupId } : null
            };
        }
    }
}