// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using System.Text.Json;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services
{
    public class PostJobHandler : RequestHandlerBase<PostJobRequest, PostJobResponse>
    {
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseDestinationAttributesRepository _destinationAttributesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;

        public PostJobHandler(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseDestinationAttributesRepository destinationAttributesRepository,
            IGraphGroupRepository graphGroupRepository,
            ILoggingRepository loggingRepository,
            ISyncJobChangeRepository syncJobChangeRepository) : base(loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _destinationAttributesRepository = destinationAttributesRepository ?? throw new ArgumentNullException(nameof(destinationAttributesRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
        }

        protected override async Task<PostJobResponse> ExecuteCoreAsync(PostJobRequest request)
        {
            var response = new PostJobResponse();

            try
            {
                var newSyncJobEntity = MapSyncJobDTOtoEntity(request.NewSyncJob);
                var destinationId = newSyncJobEntity.TargetOfficeGroupId;
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
                        Message = $"PostJobHandler created job: {request}."
                    });

                    var destinationName = await _graphGroupRepository.GetGroupNameAsync(destinationId);
                    var ownersDictionary = await _graphGroupRepository.GetDestinationOwnersAsync(new List<Guid> { destinationId });
                    var destinationAttributes = new DestinationAttributes
                    {
                        Id = newSyncJobId,
                        Name = destinationName,
                        Owners = ownersDictionary.GetValueOrDefault(destinationId)
                    };
                    await _destinationAttributesRepository.UpdateAttributes(destinationAttributes);

                    var changedOnBehalfOfDisplayName = request.NewSyncJob.LastModifiedOnBehalfOfDisplayName;
                    var changedOnBehalfOfObjectId = request.NewSyncJob.LastModifiedOnBehalfOfObjectId;

                    await _syncJobChangeRepository.Save(new SyncJobChange
                    {
                        SyncJobId = newSyncJobId,
                        ChangeTime = DateTime.UtcNow,
                        ChangedByObjectId = Guid.Parse(request.UserIdentity),
                        ChangedByDisplayName = request.UserDisplayName,
                        ChangeSource = SyncJobChangeSource.WebApp,
                        ChangeReason = SyncJobChangeReason.Onboarding.ToString(),
                        ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(newSyncJobEntity),
                        BusinessJustification = request.BusinessJustification,
                        ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName != null && changedOnBehalfOfDisplayName != request.UserDisplayName ? changedOnBehalfOfDisplayName : null,
                        ChangedOnBehalfOfObjectId = !string.IsNullOrEmpty(changedOnBehalfOfObjectId) && changedOnBehalfOfObjectId != request.UserIdentity ? new Guid(changedOnBehalfOfObjectId) : (Guid?)null
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


        private static SyncJob MapSyncJobDTOtoEntity(NewSyncJobDTO syncJob)
        {
            var queryObject = JsonDocument.Parse(syncJob.Query);
            var convertedQuery = JsonSerializer.Serialize(queryObject);

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

            return new SyncJob
            {
                Id = new Guid(),
                TargetOfficeGroupId = !string.IsNullOrEmpty(targetOfficeGroupId) ? new Guid(targetOfficeGroupId) : Guid.Empty,
                Destination = syncJob.Destination,
                Requestor = syncJob.Requestor,
                StartDate = DateTime.Parse(syncJob.StartDate),
                Period = syncJob.Period,
                Query = convertedQuery,
                ThresholdPercentageForAdditions = syncJob.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = syncJob.ThresholdPercentageForRemovals,
                Status = SyncStatus.PendingReview.ToString(),
                MembershipType = !string.IsNullOrEmpty(type) ? type : MembershipTypes.GroupMembership.ToString()
            };
        }
    }
}