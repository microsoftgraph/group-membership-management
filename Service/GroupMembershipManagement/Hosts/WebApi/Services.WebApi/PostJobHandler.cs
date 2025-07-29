// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Helpers;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services
{
    public class PostJobHandler : RequestHandlerBase<PostJobRequest, PostJobResponse>
    {
        private const int DEFAULT_PERIOD = 24;
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseDestinationAttributesRepository _destinationAttributesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;

        public PostJobHandler(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseDestinationAttributesRepository destinationAttributesRepository,
            IGraphGroupRepository graphGroupRepository,
            ILoggingRepository loggingRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IServiceBusQueueRepository serviceBusQueueRepository) : base(loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _destinationAttributesRepository = destinationAttributesRepository ?? throw new ArgumentNullException(nameof(destinationAttributesRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
        }

        protected override async Task<PostJobResponse> ExecuteCoreAsync(PostJobRequest request)
        {
            var response = new PostJobResponse();
            
            try
            {
                var newSyncJobEntity = MapSyncJobDTOtoEntity(request.NewSyncJob);
                
                // Check if auto-approval feature is enabled
                var isGroupBasedAutoApprovalEnabled = await IsAutoApprovalForGroupBasedSyncsEnabledAsync();
                var isOrgLeaderAutoApprovalEnabled = await IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabledAsync();
                var shouldAutoApprove = await ShouldAutoApproveJobAsync(request.NewSyncJob.Query, request.UserIdentity, isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled);
                if (shouldAutoApprove)
                {
                    newSyncJobEntity.Status = SyncStatus.Idle.ToString();

                    if (newSyncJobEntity.StartDate < DateTime.UtcNow)
                    {
                        newSyncJobEntity.StartDate = DateTime.UtcNow.AddHours(24);
                    }

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Job auto-approved based on configured auto-approval criteria."
                    });
                }

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

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"PostJobHandler created job: {response.NewSyncJobId}."
                    });

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
                    var changeReason = shouldAutoApprove ? SyncJobChangeReason.OnboardingAutoApproved : SyncJobChangeReason.Onboarding;

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

                    var jobConfigurationQueueMessage = new JobConfigurationQueueMessage { JobId = newSyncJobId };
                    var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jobConfigurationQueueMessage));

                    var message = new ServiceBusMessage
                    {
                        MessageId = newSyncJobId.ToString(),
                        Body = body
                    };

                    await _serviceBusQueueRepository.SendMessageAsync(message);

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Sent message {message.MessageId} to configuration queue",
                    });
                }
                else
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = "JobCreationFailed";
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"PostJobHandler failed to create job request: {request}."
                    });
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"An error occurred during job creation: {ex.Message}"
                });
            }

            return response;
        }

        private async Task<bool> ShouldAutoApproveJobAsync(string query, string userIdentity, bool isGroupBasedAutoApprovalEnabled, bool isOrgLeaderAutoApprovalEnabled)
        {
            try
            {
                // Check for GroupMembership auto-approval scenario (only if enabled)
                if (isGroupBasedAutoApprovalEnabled)
                {
                    var groupMembershipApproval = await ShouldAutoApproveGroupMembershipJobAsync(query);
                    if (groupMembershipApproval)
                        return true;
                }

                // Check for SqlMembership manager auto-approval scenario (only if enabled)
                if (isOrgLeaderAutoApprovalEnabled)
                {
                    var sqlMembershipApproval = await ShouldAutoApproveSqlMembershipJobAsync(query, userIdentity);
                    if (sqlMembershipApproval)
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error during auto-approval check: {ex.Message}"
                });
                return false;
            }
        }

        private async Task<bool> ShouldAutoApproveGroupMembershipJobAsync(string query)
        {
            try
            {
                // Use JsonParser to check if this is a GroupMembership-only query
                if (!JsonParser.IsGroupMembershipOnlyQuery(query))
                    return false;

                // Get the group IDs to check visibility
                var groupIds = JsonParser.GetGroupMembershipSourceIds(query);
                if (groupIds == null || groupIds.Count == 0)
                    return false;

                var groups = await _graphGroupRepository.GetGroupsAsync(groupIds);
                
                var hiddenGroupFound = groups.Any(group => string.Equals(group.Visibility, "HiddenMembership", StringComparison.OrdinalIgnoreCase));
                if (hiddenGroupFound)
                {
                    return false;
                }

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Auto-approval granted: All {groupIds.Count} source groups have acceptable visibility."
                });

                return true;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error during GroupMembership auto-approval check: {ex.Message}"
                });
                return false;
            }
        }

        private async Task<bool> ShouldAutoApproveSqlMembershipJobAsync(string query, string userIdentity)
        {
            try
            {
                var userDetails = await GetUserOnPremisesImmutableIdAsync(userIdentity);
                if (string.IsNullOrEmpty(userDetails))
                    return false;

                // Parse the user's onPremisesImmutableId as manager ID
                if (!int.TryParse(userDetails, out var userImmutableId))
                    return false;

                // Use JsonParser to check if this is a single SqlMembership query with matching manager ID
                if (!JsonParser.IsSingleSqlMembershipQueryWithManagerId(query, userImmutableId))
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Auto-approval granted: Single SqlMembership query with manager ID matching requestor's onPremisesImmutableId."
                });

                return true;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error during SqlMembership auto-approval check: {ex.Message}"
                });
                return false;
            }
        }

        private async Task<string> GetUserOnPremisesImmutableIdAsync(string userIdentity)
        {
            try
            {
                var user = await _graphGroupRepository.GetUserByUpnOrIdAsync(userIdentity, false);
                return user?.OnPremisesImmutableId;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error retrieving user onPremisesImmutableId: {ex.Message}"
                });
                return null;
            }
        }

        private async Task<bool> IsAutoApprovalForGroupBasedSyncsEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled);
                return setting != null && setting.SettingValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error retrieving auto-approval setting: {ex.Message}"
                });
                return false;
            }
        }

        private async Task<bool> IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled);
                return setting != null && setting.SettingValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error retrieving org leader auto-approval setting: {ex.Message}"
                });
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