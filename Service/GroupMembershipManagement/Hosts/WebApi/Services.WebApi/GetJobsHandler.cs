// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using SyncJobDTO = WebApi.Models.DTOs.SyncJob;

namespace Services
{
    public class GetJobsHandler : RequestHandlerBase<GetJobsRequest, GetJobsResponse>
    {
        private readonly ISyncJobRepository _syncJobRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        public GetJobsHandler(ILoggingRepository loggingRepository,
                              ISyncJobRepository syncJobRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetJobsResponse> ExecuteCoreAsync(GetJobsRequest request)
        {
            var response = new GetJobsResponse();
            var jobs = _syncJobRepository.GetSyncJobsAsync(true, Models.SyncStatus.All);

            await foreach (var job in jobs)
            {
                var dto = new SyncJobDTO
                (
                    partitionKey: job.PartitionKey,
                    rowKey: job.RowKey,
                    targetGroupId: job.TargetOfficeGroupId,
                    status: job.Status,
                    startDate: job.StartDate,
                    lastSuccessfulStartTime: job.LastSuccessfulStartTime,
                    lastSuccessfulRunTime: job.LastSuccessfulRunTime,
                    estimatedNextRunTime: job.StartDate > job.LastSuccessfulRunTime ? job.StartDate : job.LastSuccessfulRunTime.AddHours(job.Period),
                    thresholdPercentageForAdditions: job.ThresholdPercentageForAdditions,
                    thresholdPercentageForRemovals: job.ThresholdPercentageForRemovals
                );

                response.Model.Add(dto);
            }

            var targetGroups = (await _graphGroupRepository.GetGroupsAsync(response.Model.Select(x => x.TargetGroupId).ToList()))
                               .ToDictionary(x => x.ObjectId);

            foreach (var job in response.Model)
            {
                if (targetGroups.ContainsKey(job.TargetGroupId))
                    job.TargetGroupType = targetGroups[job.TargetGroupId].Type;
            }

            return response;
        }
    }
}