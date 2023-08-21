// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using SyncJobDetailsDTO = WebApi.Models.DTOs.SyncJobDetails;

namespace Services{
    public class GetJobDetailsHandler : RequestHandlerBase<GetJobDetailsRequest, GetJobDetailsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public GetJobDetailsHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetJobDetailsResponse> ExecuteCoreAsync(GetJobDetailsRequest request)
        {
            var response = new GetJobDetailsResponse();
            SyncJob job = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
            var endpoints = await GetGroupEndpointsAsync(request.SyncJobId);

            bool isRequestorOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(job.Requestor, job.TargetOfficeGroupId);
            string requestor = isRequestorOwner ? job.Requestor : job.Requestor + " (Not an Owner)";

            var dto = new SyncJobDetailsDTO
                (
                    startDate: job.StartDate,
                    lastSuccessfulStartTime: job.LastSuccessfulStartTime,
                    source: job.Query,
                    requestor: requestor,
                    thresholdViolations: job.ThresholdViolations,
                    thresholdPercentageForAdditions: job.ThresholdPercentageForAdditions,
                    thresholdPercentageForRemovals: job.ThresholdPercentageForRemovals,
                    endpoints: endpoints
                );

            response.Model = dto;

            return response;
        }

        public async Task<List<string>> GetGroupEndpointsAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupEndpointsAsync(groupId);
        }
    }
}