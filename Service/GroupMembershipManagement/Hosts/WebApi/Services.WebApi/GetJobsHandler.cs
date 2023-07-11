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
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        public GetJobsHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetJobsResponse> ExecuteCoreAsync(GetJobsRequest request)
        {
            var response = new GetJobsResponse();
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsAsync();

            foreach (var job in jobs)
            {
                String targetGroupName = await _graphGroupRepository.GetGroupNameAsync(job.TargetOfficeGroupId);

                var dto = new SyncJobDTO
                (
                    id: job.Id,                    
                    targetGroupId: job.TargetOfficeGroupId,
                    targetGroupName: targetGroupName,
                    period: job.Period,
                    status: job.Status,
                    lastSuccessfulRunTime: job.LastSuccessfulRunTime,
                    estimatedNextRunTime: job.StartDate > job.LastSuccessfulRunTime ? job.StartDate : job.LastSuccessfulRunTime.AddHours(job.Period)
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