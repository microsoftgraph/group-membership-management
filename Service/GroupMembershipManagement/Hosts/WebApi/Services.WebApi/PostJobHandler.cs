// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Helpers;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        public PostJobHandler(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseDestinationAttributesRepository destinationAttributesRepository,
            IGraphGroupRepository graphGroupRepository,
            ILoggingRepository loggingRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IDatabaseSettingsRepository databaseSettingsRepository) : base(loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _destinationAttributesRepository = destinationAttributesRepository ?? throw new ArgumentNullException(nameof(destinationAttributesRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
        }

        protected override async Task<PostJobResponse> ExecuteCoreAsync(PostJobRequest request)
        {
            var response = new PostJobResponse();

            
            try
            {
                var newSyncJobEntity = MapSyncJobDTOtoEntity(request.NewSyncJob);
                
                // Check if auto-approval feature is enabled
                var isAutoApprovalEnabled = await IsAutoApprovalForGroupBasedSyncsEnabledAsync();
                var shouldAutoApprove = isAutoApprovalEnabled && await ShouldAutoApproveJobAsync(request.NewSyncJob.Query);
                if (shouldAutoApprove)
                {
                    newSyncJobEntity.Status = SyncStatus.Idle.ToString();

                    if (newSyncJobEntity.StartDate < DateTime.UtcNow)
                    {
                        newSyncJobEntity.StartDate = DateTime.UtcNow.AddHours(24);
                    }

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Job auto-approved: All source parts are GroupMembership with acceptable visibility."
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

        private async Task<bool> ShouldAutoApproveJobAsync(string query)
        {
            try
            {
                var groupIds = JsonParser.GetGroupMembershipSourceIds(query);
                if (groupIds == null)
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
                    Message = $"Error during auto-approval check: {ex.Message}"
                });
                return false;
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