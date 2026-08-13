// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models;
using Microsoft.Extensions.Logging;
using SyncJobDTO = WebApi.Models.DTOs.SyncJob;

namespace Services
{
    public class GetJobsHandler : RequestHandlerBase<GetJobsRequest, GetJobsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;

        public GetJobsHandler(ILogger<GetJobsHandler> logger,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IHttpContextAccessor httpContextAccessor,
                              ISyncJobChangeRepository syncJobChangeRepository) : base(logger)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
        }

        protected override async Task<GetJobsResponse> ExecuteCoreAsync(GetJobsRequest request)
        {
            var response = new GetJobsResponse
            {
                CurrentPage = 1,
                TotalNumberOfPages = 1,
                TotalItems = 0
            };

            var odataSettings = new ODataQuerySettings
            {
                EnsureStableOrdering = true
            };

            var jobsQuery = GetSyncJobsQuery();

            // Check if we need custom sorting (targetGroupName or lastModifiedTime)
            var needsCustomSorting = request.CustomSortBy == "targetGroupName" || request.CustomSortBy == "lastModifiedTime";

            if (!needsCustomSorting && request.QueryOptions?.OrderBy?.OrderByClause == null)
            {
                jobsQuery = jobsQuery
                            .OrderBy(x => x.StatusDetails.SortPriority)
                            .ThenBy(x => x.Status);

                odataSettings.EnsureStableOrdering = false;
            }

            List<SyncJob> jobs;
            var numberOfJobs = 0;

            var pageSize = request.QueryOptions?.Top?.Value ?? 0;
            var pageOffset = request.QueryOptions?.Skip?.Value ?? -1;
            var hasPaging = pageSize > 0 && pageOffset >= 0;

            // Last modified times for the jobs on the page that is actually returned.
            var lastModifiedTimes = new Dictionary<Guid, DateTime?>();

            if (needsCustomSorting)
            {
                var baseQuery = GetSyncJobsQuery();

                // Apply only filters here. Ordering and paging are applied per sort field below so
                // that the page is narrowed down before any destination details are resolved.
                if (request.QueryOptions?.Filter != null)
                {
                    baseQuery = (IQueryable<SyncJob>)request.QueryOptions.Filter.ApplyTo(baseQuery, new ODataQuerySettings());
                }

                if (request.CustomSortBy == "targetGroupName")
                {
                    // Sort on the destination name cached in SQL (kept fresh by DestinationAttributesUpdater)
                    // instead of on names resolved from Graph. This lets the database do the ordering and the
                    // paging, so only the visible page is ever read from Graph. Sorting on Graph names required
                    // one group read per job in the whole catalog, which exhausted the tenant RU quota.
                    var sortedQuery = request.IsSortedDescending == true
                        ? baseQuery.OrderByDescending(job => job.DestinationName != null ? job.DestinationName.Name : null)
                        : baseQuery.OrderBy(job => job.DestinationName != null ? job.DestinationName.Name : null);

                    numberOfJobs = baseQuery.Count();
                    jobs = hasPaging
                            ? sortedQuery.Skip(pageOffset).Take(pageSize).ToList()
                            : sortedQuery.ToList();
                }
                else
                {
                    // lastModifiedTime lives in SyncJobChanges, so the candidate set still has to be
                    // materialized before it can be ordered. Paging is applied here, before Graph is called.
                    var candidateJobs = baseQuery.ToList();
                    numberOfJobs = candidateJobs.Count;

                    var allLastModifiedTimes = new Dictionary<Guid, DateTime?>();
                    foreach (var job in candidateJobs)
                    {
                        allLastModifiedTimes[job.Id] = await GetLastModifiedTimeAsync(job.Id);
                    }

                    var orderedJobs = request.IsSortedDescending == true
                        ? candidateJobs.OrderByDescending(job => allLastModifiedTimes[job.Id] ?? DateTime.MinValue).ToList()
                        : candidateJobs.OrderBy(job => allLastModifiedTimes[job.Id] ?? DateTime.MinValue).ToList();

                    jobs = hasPaging
                            ? orderedJobs.Skip(pageOffset).Take(pageSize).ToList()
                            : orderedJobs;

                    // Reuse what was already fetched rather than querying again for the page.
                    foreach (var job in jobs)
                    {
                        lastModifiedTimes[job.Id] = allLastModifiedTimes.TryGetValue(job.Id, out var changeTime) ? changeTime : null;
                    }
                }

                if (hasPaging)
                {
                    response.TotalNumberOfPages = (int)Math.Ceiling((double)numberOfJobs / pageSize);
                    response.CurrentPage = pageOffset / pageSize + 1;
                }

                response.TotalItems = numberOfJobs;
            }
            else
            {
                // Original logic for non-custom sorting
                if (request.QueryOptions != null)
                {
                    // First get the total count before applying Skip/Top
                    var countQuery = GetSyncJobsQuery();
                    if (request.QueryOptions.Filter != null)
                    {
                        countQuery = (IQueryable<SyncJob>)request.QueryOptions.Filter.ApplyTo(countQuery, new ODataQuerySettings());
                    }
                    numberOfJobs = countQuery.Count();

                    // Then apply all operations including Skip/Top for the actual data
                    jobsQuery = (IQueryable<SyncJob>)request.QueryOptions.ApplyTo(jobsQuery, odataSettings);

                    if (hasPaging)
                    {
                        response.TotalNumberOfPages = (int)Math.Ceiling((double)numberOfJobs / pageSize);
                        response.CurrentPage = pageOffset / pageSize + 1;
                    }

                    response.TotalItems = numberOfJobs;
                }
                else
                {
                    // No query options, so count all jobs
                    numberOfJobs = GetSyncJobsQuery().Count();
                    response.TotalItems = numberOfJobs;
                    response.TotalNumberOfPages = 1;
                    response.CurrentPage = 1;
                }

                jobs = jobsQuery.ToList();
            }

            // Resolve destination details from Graph only for the page being returned, and only once
            // per distinct destination group. Every branch above has already applied paging.
            var destinationGroupIds = jobs.Select(GetDestinationGroupId)
                                          .Where(groupId => groupId.HasValue)
                                          .Select(groupId => groupId.Value)
                                          .Distinct()
                                          .ToList();

            var targetGroups = new Dictionary<Guid, AzureADGroup>();
            if (destinationGroupIds.Count > 0)
            {
                foreach (var targetGroup in await _graphGroupRepository.GetGroupsAsync(destinationGroupIds))
                {
                    targetGroups[targetGroup.ObjectId] = targetGroup;
                }
            }

            // The lastModifiedTime branch already populated these while sorting.
            if (request.CustomSortBy != "lastModifiedTime")
            {
                foreach (var job in jobs)
                {
                    lastModifiedTimes[job.Id] = await GetLastModifiedTimeAsync(job.Id);
                }
            }

            foreach (var job in jobs)
            {
                var type = job.MembershipType;
                var groupId = GetDestinationGroupId(job) ?? Guid.Empty;
                var currentTime = DateTime.UtcNow;
                var jobStartsInFuture = currentTime < job.StartDate;
                var jobScheduledForFuture = currentTime < job.ScheduledDate;

                DateTime estimatedNextRunTime;
                if (!jobStartsInFuture && !jobScheduledForFuture)
                {
                    // Round current time up to next 5-minute boundary for estimated run time
                    var now = DateTime.UtcNow;
                    var minutesToAdd = 5 - (now.Minute % 5);
                    estimatedNextRunTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc)
                        .AddMinutes(minutesToAdd);
                }
                else if (jobStartsInFuture)
                {
                    estimatedNextRunTime = job.StartDate;
                }
                else
                {
                    estimatedNextRunTime = job.ScheduledDate;
                }

                var dto = new SyncJobDTO
                (
                    job.Id,
                    groupId,
                    job.Status,
                    job.Period,
                    job.LastSuccessfulRunTime,
                    estimatedNextRunTime
                )
                {
                    TargetGroupName = targetGroups.TryGetValue(groupId, out var targetGroup) ? targetGroup.Name : job.DestinationName?.Name,
                    TargetGroupEmail = targetGroups.TryGetValue(groupId, out var targetGroupEmail) ? targetGroupEmail.Email : job.DestinationEmail?.Email,
                    TargetDestinationType = type,
                    LastModifiedTime = lastModifiedTimes.TryGetValue(job.Id, out var lastModifiedTime) ? lastModifiedTime : null
                };

                response.Model.Add(dto);
            }

            return response;
        }

        private IQueryable<SyncJob> GetSyncJobsQuery()
        {
            // DestinationName/DestinationEmail back both the SQL-side sort on target group name and the
            // fallback used when Graph does not return a destination, so they have to be loaded here.
            IQueryable<SyncJob> query = _databaseSyncJobsRepository.GetSyncJobs(true)
                                                                   .Include(j => j.DestinationName)
                                                                   .Include(j => j.DestinationEmail);

            if (_httpContextAccessor.HttpContext.User.IsInRole(Roles.JOB_TENANT_READER) ||
                _httpContextAccessor.HttpContext.User.IsInRole(Roles.JOB_TENANT_WRITER))
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

        /// <summary>
        /// Teams channel jobs store their destination on Channel and have a null Group, so the
        /// destination group id has to be resolved by membership type.
        /// </summary>
        private static Guid? GetDestinationGroupId(SyncJob job)
        {
            return job.MembershipType == MembershipTypes.TeamsChannelMembership.ToString()
                    ? job.Channel?.GroupId
                    : job.Group?.GroupId;
        }

        private async Task<DateTime?> GetLastModifiedTimeAsync(Guid syncJobId)
        {
            try
            {
                var lastChange = await _syncJobChangeRepository.GetLastSyncJobRecordBySyncJobIdAsync(syncJobId);
                return lastChange?.ChangeTime;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Unable to read the last change for sync job {SyncJobId}. Last modified time will be reported as unknown.", syncJobId);
                return null;
            }
        }
    }
}
