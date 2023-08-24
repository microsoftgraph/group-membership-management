// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using SyncJobDetailsDTO = WebApi.Models.DTOs.SyncJobDetails;

namespace Services{
    public class GetJobDetailsHandler : RequestHandlerBase<GetJobDetailsRequest, GetJobDetailsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;

        public GetJobDetailsHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        protected override async Task<GetJobDetailsResponse> ExecuteCoreAsync(GetJobDetailsRequest request)
        {
            var response = new GetJobDetailsResponse();
            SyncJob job = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
            List<string> endpoints = new List<string>();

            try
            {
                endpoints = await GetGroupEndpointsAsync(request.SyncJobId);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Unable to retrieve group endpoints\n{ex.GetBaseException()}"
                });
            }

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