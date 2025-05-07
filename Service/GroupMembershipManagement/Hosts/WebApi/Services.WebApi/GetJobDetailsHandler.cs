// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using WebApi.Models;
using SyncJobDetailsDTO = WebApi.Models.DTOs.SyncJobDetails;

namespace Services
{
    public class GetJobDetailsHandler : RequestHandlerBase<GetJobDetailsRequest, GetJobDetailsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobChangeRepository _syncJobChangesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GetJobDetailsHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              ISyncJobChangeRepository syncJobChangesRepository,
                              IGraphGroupRepository graphGroupRepository,
                              ITeamsChannelRepository teamsChannelRepository,
                              IHttpContextAccessor httpContextAccessor) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobChangesRepository = syncJobChangesRepository ?? throw new ArgumentNullException(nameof(syncJobChangesRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        protected override async Task<GetJobDetailsResponse> ExecuteCoreAsync(GetJobDetailsRequest request)
        {
            var endpoints = new List<string>();
            var response = new GetJobDetailsResponse
            {
                StatusCode = HttpStatusCode.OK
            };

            var (job, statusCode) = await GetSyncJobAsync(request.SyncJobId);

            if (statusCode != HttpStatusCode.OK)
            {
                response.StatusCode = statusCode;
                return response;
            }

            var type = job.MembershipType;
            var groupId = job.MembershipType == MembershipTypes.GroupMembership.ToString() ? job.Group.GroupId : job.Channel.GroupId;

            try
            {
                endpoints = await _graphGroupRepository.GetGroupEndpointsAsync(groupId);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Unable to retrieve group endpoints\n{ex.GetBaseException()}"
                });
            }

            var targetGroupName = await _graphGroupRepository.GetGroupNameAsync(groupId);

            var targetChannelId = job.Channel?.ChannelId;
            var targetChannelName = job.MembershipType == MembershipTypes.TeamsChannelMembership.ToString() ?
                    await _teamsChannelRepository.GetTeamsChannelNameAsync(new Models.Entities.AzureADTeamsChannel { ChannelId = job.Channel!.ChannelId }) : null;
               
            var currentTime = DateTime.UtcNow;
            var jobStartsInFuture = currentTime < job.StartDate;
            var jobScheduledForFuture = currentTime < job.ScheduledDate;

            var lastModifiedByDisplayName = string.Empty;
            var lastModifiedByObjectId = string.Empty;
            var lastModifiedOnBehalfOfDisplayName = string.Empty;
            var lastModifiedOnBehalfOfObjectId = string.Empty;
            var groupSettings = string.Empty;

            var res = await _syncJobChangesRepository.GetLastSyncJobChangeBySyncJobIdAsync(request.SyncJobId);
            if (res != null)
            {
                lastModifiedByDisplayName = res.ChangedByDisplayName;
                lastModifiedByObjectId = res.ChangedByObjectId.ToString();
                lastModifiedOnBehalfOfDisplayName = res.ChangedOnBehalfOfDisplayName;
                lastModifiedOnBehalfOfObjectId = res.ChangedOnBehalfOfObjectId.ToString();
                if (!string.IsNullOrEmpty(lastModifiedOnBehalfOfDisplayName) && string.IsNullOrEmpty(lastModifiedOnBehalfOfObjectId))
                {
                    lastModifiedOnBehalfOfObjectId = await UpdateChangedOnBehalfOfObjectIdAsync(res, lastModifiedOnBehalfOfDisplayName);
                }
            }

            var gs = await _syncJobChangesRepository.GetRecentGroupSettingsBySyncJobIdAsync(request.SyncJobId);
            if (gs != null) groupSettings = gs?.ChangeDetails?.ToString();

            DateTime estimatedNextRunTime;
            if (!jobStartsInFuture && !jobScheduledForFuture)
            {
                estimatedNextRunTime = job.LastRunTime.AddHours(job.Period);
            }
            else if (jobStartsInFuture)
            {
                estimatedNextRunTime = job.StartDate;
            }
            else
            {
                estimatedNextRunTime = job.ScheduledDate;
            }

            var dto = new SyncJobDetailsDTO
            (
                startDate: job.StartDate,
                lastSuccessfulStartTime: job.LastSuccessfulStartTime,
                query: job.Query,
                requestor: job.Requestor,
                thresholdViolations: job.ThresholdViolations,
                thresholdPercentageForAdditions: job.ThresholdPercentageForAdditions,
                thresholdPercentageForRemovals: job.ThresholdPercentageForRemovals,
                endpoints: endpoints,
                period: job.Period
            )
            {
                SyncJobId = job.Id,
                TargetGroupId = groupId,
                TargetGroupName = targetGroupName,
                TargetChannelId = targetChannelId,
                TargetChannelName = targetChannelName,
                TargetDestinationType = type,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                EstimatedNextRunTime = estimatedNextRunTime,
                Status = job.Status,
                LastModifiedByDisplayName = lastModifiedByDisplayName,
                LastModifiedByObjectId = lastModifiedByObjectId,
                LastModifiedOnBehalfOfDisplayName = lastModifiedOnBehalfOfDisplayName,
                LastModifiedOnBehalfOfObjectId = lastModifiedOnBehalfOfObjectId,
                GroupSettings = groupSettings
            };

            response.Model = dto;

            return response;
        }

        private async Task<string> UpdateChangedOnBehalfOfObjectIdAsync(SyncJobChange res, string userIdentifier)
        {
            var userResponse = await _graphGroupRepository.GetUserByUpnOrIdAsync(userIdentifier, false);
            if (userResponse == null) return string.Empty;
            res.ChangedOnBehalfOfObjectId = userResponse.ObjectId;
            await _syncJobChangesRepository.UpdateSyncJobChangeAsync(res);
            return userResponse.ObjectId.ToString();
        }

        private async Task<(SyncJob? syncJob, HttpStatusCode statusCode)> GetSyncJobAsync(Guid syncJobId)
        {
            var userId = _httpContextAccessor.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return (null, HttpStatusCode.Forbidden);

            if (_httpContextAccessor.HttpContext.User.IsInRole(Roles.JOB_TENANT_READER) || 
                _httpContextAccessor.HttpContext.User.IsInRole(Roles.JOB_TENANT_WRITER))
            {
                return (await _databaseSyncJobsRepository.GetSyncJobAsync(syncJobId), HttpStatusCode.OK);
            }

            if (!await _databaseSyncJobsRepository.GetSyncJobs(true).AnyAsync(x => x.Id == syncJobId))
            {
                return (null, HttpStatusCode.NotFound);
            }

            var job = await _databaseSyncJobsRepository
                            .GetSyncJobs(true)
                            .FirstOrDefaultAsync(x => x.Id == syncJobId && x.DestinationOwners.Any(o => o.ObjectId == Guid.Parse(userId)));

            return job != null ? (job, HttpStatusCode.OK) : (null, HttpStatusCode.Forbidden);
        }
    }
}
