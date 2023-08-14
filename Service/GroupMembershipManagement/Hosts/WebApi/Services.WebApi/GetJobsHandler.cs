// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.OData.Query;
using Models;
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
            var numberOfJobs = 0;
            var response = new GetJobsResponse
            {
                CurrentPage = 1,
                TotalNumberOfPages = 1
            };

            var jobsQuery = _databaseSyncJobsRepository.GetSyncJobs();

            if (request.QueryOptions != null)
            {
                jobsQuery = (IQueryable<SyncJob>)request.QueryOptions.ApplyTo(jobsQuery);

                var countQuery = (IQueryable<SyncJob>)request.QueryOptions.ApplyTo(
                       _databaseSyncJobsRepository.GetSyncJobs(),
                       AllowedQueryOptions.Skip | AllowedQueryOptions.Top);
                numberOfJobs = countQuery.Count();

                if (request.QueryOptions.Top?.Value > 0 && request.QueryOptions.Skip?.Value >= 0)
                {
                    response.TotalNumberOfPages = (int)Math.Ceiling((double)numberOfJobs / request.QueryOptions.Top.Value);
                    response.CurrentPage = request.QueryOptions.Skip.Value / request.QueryOptions.Top.Value + 1;
                }
            }

            var jobs = jobsQuery.ToList();
            var targetGroups = (await _graphGroupRepository.GetGroupsAsync(jobs.Select(x => x.TargetOfficeGroupId).ToList()))
                               .ToDictionary(x => x.ObjectId);

            foreach (var job in jobs)
            {
                var dto = new SyncJobDTO
                (
                    job.Id,
                    job.TargetOfficeGroupId,
                    job.Status,
                    job.Period,
                    job.LastSuccessfulRunTime,
                    job.StartDate > job.LastSuccessfulRunTime ? job.StartDate : job.LastSuccessfulRunTime.AddHours(job.Period)
                )
                {
                    TargetGroupName = targetGroups.ContainsKey(job.TargetOfficeGroupId) ? targetGroups[job.TargetOfficeGroupId].Name : null,
                    TargetGroupType = targetGroups.ContainsKey(job.TargetOfficeGroupId) ? targetGroups[job.TargetOfficeGroupId].Type : null
                };

                response.Model.Add(dto);
            }

            return response;
        }
    }
}