// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

namespace Services
{
    public class PostJobHandler : RequestHandlerBase<PostJobRequest, PostJobResponse>
    {
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseDestinationAttributesRepository _destinationAttributesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;

        public PostJobHandler(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseDestinationAttributesRepository destinationAttributesRepository,
            IGraphGroupRepository graphGroupRepository,
            ILoggingRepository loggingRepository) : base(loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _destinationAttributesRepository = destinationAttributesRepository ?? throw new ArgumentNullException(nameof(destinationAttributesRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        protected override async Task<PostJobResponse> ExecuteCoreAsync(PostJobRequest request)
        {
            var response = new PostJobResponse();

            try
            {
                var newSyncJobEntity = MapSyncJobDTOtoEntity(request.NewSyncJob);
                var destinationId = newSyncJobEntity.TargetOfficeGroupId;

                var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, destinationId);
                if (!(isGroupOwner || request.IsJobTenantWriter))
                {
                    response.StatusCode = HttpStatusCode.Forbidden;
                    return response;
                }

                var destinationName = await _graphGroupRepository.GetGroupNameAsync(destinationId);
                var ownersDictionary = await _graphGroupRepository.GetDestinationOwnersAsync(new List<Guid> { destinationId });

                var newSyncJobId = await _syncJobRepository.CreateSyncJobAsync(newSyncJobEntity);

                var destinationAttributes = new DestinationAttributes
                {
                    Id = newSyncJobId,
                    Name = destinationName,
                    Owners = ownersDictionary.GetValueOrDefault(destinationId)
                };
                await _destinationAttributesRepository.UpdateAttributes(destinationAttributes);

                if (newSyncJobId != Guid.Empty)
                {
                    response.StatusCode = HttpStatusCode.Created;
                    response.NewSyncJobId = newSyncJobId;

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"PostJobHandler created job: {request}."
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
            var queryObject = JsonConvert.DeserializeObject(syncJob.Query);
            var convertedQuery = JsonConvert.SerializeObject(queryObject);

            var destinationArray = JsonConvert.DeserializeObject<List<dynamic>>(syncJob.Destination);
            string targetOfficeGroupId = destinationArray?.FirstOrDefault()?.value?.objectId;

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
                Status = SyncStatus.PendingReview.ToString()
            };
        }
    }
}