// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.ApplicationInsights;
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
using System.Text.Json.Nodes;
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
        private readonly TelemetryClient _telemetryClient;
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
            TelemetryClient telemetryClient,
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
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _autoApproverQueueRepository = autoApproverQueueRepository;
        }

        protected override async Task<PostJobResponse> ExecuteCoreAsync(PostJobRequest request)
        {
            var response = new PostJobResponse();
            
            try
            {
                var newSyncJobEntity = MapSyncJobDTOtoEntity(request.NewSyncJob);

                // Validate TeamsChannel jobs for a channel id and exact duplicate destination.
                if (newSyncJobEntity.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
                {
                    var channelId = newSyncJobEntity.Channel?.ChannelId;
                    if (string.IsNullOrEmpty(channelId))
                    {
                        response.StatusCode = HttpStatusCode.BadRequest;
                        response.ErrorCode = "MissingChannelId";
                        response.Message = "TeamsChannel sync requires a specific channel. To sync the Team itself, use the GroupMembership path.";
                        return response;
                    }

                    var existingChannelJob = await _syncJobRepository.GetSyncJobByTeamIdAndChannelIdAsync(newSyncJobEntity.Channel.GroupId, channelId);
                    if (existingChannelJob != null)
                    {
                        // Authorize ownership before returning duplicate-destination details.
                        var isChannelGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, newSyncJobEntity.Channel.GroupId);
                        if (!(isChannelGroupOwner || request.IsJobTenantWriter))
                        {
                            response.StatusCode = HttpStatusCode.Forbidden;
                            return response;
                        }

                        response.StatusCode = HttpStatusCode.Conflict;
                        response.ErrorCode = "DuplicateDestination";
                        response.Message = "A sync job for this channel already exists.";
                        return response;
                    }
                }

                // Validate any TeamsChannel source part: it is permitted only when it references the
                // same channel as this job's TeamsChannel destination, the job has at least one other
                // source part, and the part is inclusionary. (US3: source == destination.)
                var teamsChannelSourceValidation = ValidateTeamsChannelSourceParts(newSyncJobEntity);
                if (teamsChannelSourceValidation != null)
                {
                    return teamsChannelSourceValidation;
                }

                var isPendingConfigurationEnabled = _pendingConfigurationConfig.PendingConfigurationIsEnabled;

                // Check if pending configuration feature is enabled, only works for groups
                if (isPendingConfigurationEnabled && newSyncJobEntity.MembershipType == MembershipTypes.GroupMembership.ToString())
                {
                    newSyncJobEntity.Status = SyncStatus.PendingConfiguration.ToString();
                }
                else if (_autoApproverQueueRepository != null)
                {
                    // Route through the AutoApprover: the job waits in PendingAutoApproval until the
                    // AutoApprover evaluates it (approves -> Idle, declines -> PendingReview). The queue
                    // repository is only registered when AutoApprover:IsEnabled is true, so when the feature
                    // is disabled (or not configured) it resolves to null and the job keeps the default
                    // PendingReview status and is never enqueued.
                    newSyncJobEntity.Status = SyncStatus.PendingAutoApproval.ToString();
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

                    _telemetryClient.TrackEvent("JobOnboarded", new Dictionary<string, string>
                    {
                        { "SyncJobId", newSyncJobId.ToString() },
                        { "TargetOfficeGroupId", newSyncJobEntity.TargetOfficeGroupId.ToString() },
                        { "MembershipType", newSyncJobEntity.MembershipType },
                        { "OnboardedUsingAIQB", request.NewSyncJob.OnboardedUsingAIQB.ToString() }
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
                        if (_autoApproverQueueRepository != null && newSyncJobEntity.Status == SyncStatus.PendingAutoApproval.ToString())
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

                            try
                            {
                                await _autoApproverQueueRepository.SendMessageAsync(autoApprovalServiceBusMessage);
                                _logger.AutoApproverMessageSent(autoApprovalServiceBusMessage.MessageId);
                            }
                            catch (Exception ex)
                            {
                                // The job was already persisted with PendingAutoApproval status. The AutoApprover
                                // only acts on queue messages (it never scans the database for orphaned jobs), so
                                // if enqueueing fails nothing would ever move the job out of PendingAutoApproval and
                                // it would be invisible to reviewers. Revert it to PendingReview for human review.
                                _logger.AutoApproverMessageSendFailed(newSyncJobId, ex);

                                try
                                {
                                    newSyncJobEntity.Id = newSyncJobId;
                                    newSyncJobEntity.Status = SyncStatus.PendingReview.ToString();
                                    await _syncJobRepository.UpdateSyncJobsAsync(new[] { newSyncJobEntity });
                                }
                                catch (Exception revertEx)
                                {
                                    _logger.AutoApproverRevertToPendingReviewFailed(newSyncJobId, revertEx);
                                }
                            }
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

        private PostJobResponse? ValidateTeamsChannelSourceParts(SyncJob newSyncJobEntity)
        {
            var sourceParts = ParseQuerySourceParts(newSyncJobEntity.Query);
            var teamsChannelSourceParts = sourceParts
                .Where(IsTeamsChannelSourcePart)
                .ToList();

            if (teamsChannelSourceParts.Count == 0)
            {
                return null;
            }

            // A TeamsChannel source part requires a TeamsChannel destination on the same job.
            if (newSyncJobEntity.MembershipType != MembershipTypes.TeamsChannelMembership.ToString()
                || string.IsNullOrEmpty(newSyncJobEntity.Channel?.ChannelId))
            {
                return new PostJobResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "TeamsChannelSourceRequiresChannelDestination",
                    Message = "A TeamsChannel source is only allowed on a job whose destination is a Teams channel."
                };
            }

            var destinationGroupId = newSyncJobEntity.Channel.GroupId.ToString();
            var destinationChannelId = newSyncJobEntity.Channel.ChannelId;

            // Each TeamsChannel source part must reference the same channel as the destination.
            if (teamsChannelSourceParts.Any(p =>
                    !string.Equals(p.SourceObjectId, destinationGroupId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(p.SourceChannelId, destinationChannelId, StringComparison.OrdinalIgnoreCase)))
            {
                return new PostJobResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "TeamsChannelSourceMustMatchDestination",
                    Message = "A TeamsChannel source must reference the same channel as this job's destination."
                };
            }

            // At least one other source part with a recognized (non-TeamsChannel) type must be present.
            // Typeless/garbage parts do not count as an additional source.
            if (!sourceParts.Any(p => !string.IsNullOrWhiteSpace(p.Type) && !IsTeamsChannelSourcePart(p)))
            {
                return new PostJobResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "TeamsChannelSourceRequiresAdditionalSource",
                    Message = "A TeamsChannel source must be combined with at least one other source part."
                };
            }

            // A TeamsChannel source part is inclusionary only.
            if (teamsChannelSourceParts.Any(p => p.Exclusionary))
            {
                return new PostJobResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "TeamsChannelSourceCannotBeExclusionary",
                    Message = "A TeamsChannel source cannot be exclusionary."
                };
            }

            return null;
        }

        private static bool IsTeamsChannelSourcePart(SourcePartInfo part) =>
            string.Equals(part.Type, MembershipTypes.TeamsChannelMembership.ToString(), StringComparison.OrdinalIgnoreCase);

        private static List<SourcePartInfo> ParseQuerySourceParts(string? query)
        {
            var parts = new List<SourcePartInfo>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return parts;
            }

            if (JsonNode.Parse(query) is not JsonArray queryArray)
            {
                return parts;
            }

            foreach (var item in queryArray)
            {
                if (item is not JsonObject sourcePart)
                {
                    continue;
                }

                var info = new SourcePartInfo
                {
                    Type = ReadStringNode(sourcePart["type"])
                };

                if (sourcePart["exclusionary"] is JsonValue exclusionaryValue)
                {
                    if (exclusionaryValue.TryGetValue<bool>(out var exclusionary))
                    {
                        info.Exclusionary = exclusionary;
                    }
                    else if (exclusionaryValue.TryGetValue<string>(out var exclusionaryText)
                        && bool.TryParse(exclusionaryText, out var parsedExclusionary))
                    {
                        // Align with the trigger's (bool) cast: a stringified boolean must be honored here
                        // so an exclusionary TeamsChannel source is rejected at submit rather than crashing later.
                        info.Exclusionary = parsedExclusionary;
                    }
                }

                if (sourcePart["source"] is JsonObject source)
                {
                    info.SourceObjectId = ReadStringNode(source["objectId"]);
                    info.SourceChannelId = ReadStringNode(source["channelId"]);
                }

                parts.Add(info);
            }

            return parts;
        }

        // Returns the node's string value, or null when it is absent or not a JSON string. This keeps
        // the parser tolerant of malformed parts so validation rules (not an exception) reject them.
        private static string? ReadStringNode(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        private sealed class SourcePartInfo
        {
            public string? Type { get; set; }
            public bool Exclusionary { get; set; }
            public string? SourceObjectId { get; set; }
            public string? SourceChannelId { get; set; }
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