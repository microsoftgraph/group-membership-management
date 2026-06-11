// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Models;
using Models.SyncJobChange;
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
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;

        public GetJobsHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IHttpContextAccessor httpContextAccessor,
                              ISyncJobChangeRepository syncJobChangeRepository) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
        }

        protected override async Task<GetJobsResponse> ExecuteCoreAsync(GetJobsRequest request)
        {
            var numberOfJobs = 0;
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
            
            if (needsCustomSorting)
            {
                // For custom sorting, get ALL jobs first without applying OData pagination
                var baseQuery = GetSyncJobsQuery();
                
                // Apply only filters, NOT Skip/Top/OrderBy
                if (request.QueryOptions?.Filter != null)
                {
                    var filterOnlyQuery = (IQueryable<SyncJob>)request.QueryOptions.Filter.ApplyTo(baseQuery, new ODataQuerySettings());
                    jobs = filterOnlyQuery.ToList();
                }
                else
                {
                    jobs = baseQuery.ToList();
                }

                // Update numberOfJobs to reflect the filtered count
                numberOfJobs = jobs.Count;
                
                // Calculate pagination info based on filtered results
                if (request.QueryOptions?.Top?.Value > 0 && request.QueryOptions?.Skip?.Value >= 0)
                {
                    response.TotalNumberOfPages = (int)Math.Ceiling((double)numberOfJobs / request.QueryOptions.Top.Value);
                    response.CurrentPage = request.QueryOptions.Skip.Value / request.QueryOptions.Top.Value + 1;
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

                    if (request.QueryOptions.Top?.Value > 0 && request.QueryOptions.Skip?.Value >= 0)
                    {
                        response.TotalNumberOfPages = (int)Math.Ceiling((double)numberOfJobs / request.QueryOptions.Top.Value);
                        response.CurrentPage = request.QueryOptions.Skip.Value / request.QueryOptions.Top.Value + 1;
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

            var targetGroups = (await _graphGroupRepository.GetGroupsAsync(jobs.Select(x => x.MembershipType == MembershipTypes.TeamsChannelMembership.ToString() ? x.Channel.GroupId : x.Group.GroupId).ToList()))
                               .ToDictionary(x => x.ObjectId);

            // Handle custom sorting
            var allLastModifiedTimes = new Dictionary<Guid, DateTime?>(); // Store for reuse
            
            if (request.CustomSortBy == "targetGroupName")
            {
                var jobsWithNames = jobs.Select(job => new { Job = job, TargetGroupName = targetGroups.ContainsKey(job.Group.GroupId) ? targetGroups[job.Group.GroupId].Name : null }).ToList();
                jobs = request.IsSortedDescending == true 
                    ? jobsWithNames.OrderByDescending(job => job.TargetGroupName).Select(job => job.Job).ToList()
                    : jobsWithNames.OrderBy(job => job.TargetGroupName).Select(job => job.Job).ToList();
            }
            else if (request.CustomSortBy == "lastModifiedTime")
            {
                // For lastModifiedTime sorting, we need to get all jobs, fetch their last modified times,
                // sort them, and then apply pagination. This is expensive but necessary for accurate sorting.
                foreach (var job in jobs)
                {
                    try
                    {
                        var lastChange = await _syncJobChangeRepository.GetLastSyncJobRecordBySyncJobIdAsync(job.Id);
                        allLastModifiedTimes[job.Id] = lastChange?.ChangeTime;
                    }
                    catch
                    {
                        allLastModifiedTimes[job.Id] = null;
                    }
                }

                var jobsWithLastModified = jobs.Select(job => new { Job = job, LastModifiedTime = allLastModifiedTimes[job.Id] }).ToList();
                jobs = request.IsSortedDescending == true
                    ? jobsWithLastModified.OrderByDescending(job => job.LastModifiedTime ?? DateTime.MinValue).Select(job => job.Job).ToList()
                    : jobsWithLastModified.OrderBy(job => job.LastModifiedTime ?? DateTime.MinValue).Select(job => job.Job).ToList();
            }

            // Apply pagination for custom sorted results
            if (needsCustomSorting && request.QueryOptions?.Top?.Value > 0 && request.QueryOptions?.Skip?.Value >= 0)
            {
                jobs = jobs.Skip(request.QueryOptions.Skip.Value).Take(request.QueryOptions.Top.Value).ToList();
            }

            // Get last modified times for the final jobs that will be displayed
            var lastModifiedTimes = new Dictionary<Guid, DateTime?>();
            
            if (request.CustomSortBy == "lastModifiedTime")
            {
                // If we're sorting by lastModifiedTime, reuse the data we already fetched
                // We just need to filter it to the jobs that are being displayed after pagination
                foreach (var job in jobs)
                {
                    lastModifiedTimes[job.Id] = allLastModifiedTimes.ContainsKey(job.Id) 
                        ? allLastModifiedTimes[job.Id] 
                        : null;
                }
            }
            else
            {
                // For non-lastModifiedTime sorting, fetch last modified times only for displayed jobs
                // This is much more efficient as we only fetch for the current page
                foreach (var job in jobs)
                {
                    try
                    {
                        var lastChange = await _syncJobChangeRepository.GetLastSyncJobRecordBySyncJobIdAsync(job.Id);
                        lastModifiedTimes[job.Id] = lastChange?.ChangeTime;
                    }
                    catch
                    {
                        lastModifiedTimes[job.Id] = null;
                    }
                }
            }

            foreach (var job in jobs)
            {
                var type = job.MembershipType;
                var groupId = job.MembershipType.Contains("GroupMembership") ? job.Group.GroupId : job.Channel.GroupId;
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
                    TargetGroupName = targetGroups.ContainsKey(groupId) ? targetGroups[groupId].Name : null,
                    TargetGroupEmail = targetGroups.ContainsKey(groupId)? targetGroups[groupId].Email : null,
                    TargetDestinationType = type,
                    LastModifiedTime = lastModifiedTimes.ContainsKey(job.Id) ? lastModifiedTimes[job.Id] : null
                };

                response.Model.Add(dto);
            }

            return response;
        }

        private IQueryable<SyncJob> GetSyncJobsQuery()
        {
            var query = _databaseSyncJobsRepository.GetSyncJobs(true);

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
    }
}