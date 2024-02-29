// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models;
using SyncJobDTO = WebApi.Models.DTOs.SyncJob;

namespace Services
{
    public class GetJobsHandler : RequestHandlerBase<GetJobsRequest, GetJobsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GetJobsHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IHttpContextAccessor httpContextAccessor) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        protected override async Task<GetJobsResponse> ExecuteCoreAsync(GetJobsRequest request)
        {
            var numberOfJobs = 0;
            var response = new GetJobsResponse
            {
                CurrentPage = 1,
                TotalNumberOfPages = 1
            };

            var odataSettings = new ODataQuerySettings
            {
                EnsureStableOrdering = true
            };

            var jobsQuery = GetSyncJobsQuery();

            if (request.QueryOptions?.OrderBy?.OrderByClause == null)
            {
                jobsQuery = jobsQuery
                            .OrderBy(x => x.StatusDetails.SortPriority)
                            .ThenBy(x => x.Status);

                odataSettings.EnsureStableOrdering = false;
            }

            if (request.QueryOptions != null)
            {
                jobsQuery = (IQueryable<SyncJob>)request.QueryOptions.ApplyTo(jobsQuery, odataSettings);

                var countQuery = (IQueryable<SyncJob>)request.QueryOptions.ApplyTo(
                       GetSyncJobsQuery(),
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
                var type = job.Destination.Contains("GroupMembership") ? "Group" : "Channel";
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
                    TargetGroupType = type
                };

                response.Model.Add(dto);
            }

            return response;
        }

        private IQueryable<SyncJob> GetSyncJobsQuery()
        {
            var query = _databaseSyncJobsRepository.GetSyncJobs(true);

            if (_httpContextAccessor.HttpContext.User.IsInRole(Roles.TENANT_ADMINISTRATOR)
                || _httpContextAccessor.HttpContext.User.IsInRole(Roles.TENANT_READER)
                || _httpContextAccessor.HttpContext.User.IsInRole(Roles.TENANT_SUBMISSION_REVIEWER)
                || _httpContextAccessor.HttpContext.User.IsInRole(Roles.TENANT_JOB_EDITOR))
            {
                return query;
            }
            else
            {
                var userId = _httpContextAccessor.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Enumerable.Empty<SyncJob>().AsQueryable();
                query = query.Where(x => x.DestinationOwners.Any(o => o.ObjectId == Guid.Parse(userId)));
            }

            return query;
        }
    }
}