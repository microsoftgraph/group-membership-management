// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using WebApi.Models;
using SyncJobDetailsDTO = WebApi.Models.DTOs.SyncJobDetails;

namespace Services
{
    public class GetChannelHandler : RequestHandlerBase<GetChannelRequest, GetChannelResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GetChannelHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IDatabaseChannelsRepository databaseChannelsRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IHttpContextAccessor httpContextAccessor) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        protected override async Task<GetChannelResponse> ExecuteCoreAsync(GetChannelRequest request)
        {
            var endpoints = new List<string>();
            var response = new GetChannelResponse
            {
                StatusCode = HttpStatusCode.OK
            };

            var (job, statusCode) = await GetSyncJobByGroupIdAsync(request.GroupId, request.ChannelId);

            if (statusCode != HttpStatusCode.OK)
            {
                response.StatusCode = statusCode;
                return response;
            }

            try
            {
                endpoints = await _graphGroupRepository.GetGroupEndpointsAsync(request.GroupId);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Unable to retrieve group endpoints\n{ex.GetBaseException()}"
                });
            }

            var type = job.MembershipType;
            var targetGroupName = await _graphGroupRepository.GetGroupNameAsync(request.GroupId);
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
                TargetGroupId = request.GroupId,
                TargetGroupName = targetGroupName,
                TargetDestinationType = type,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                EstimatedNextRunTime = estimatedNextRunTime,
                Status = job.Status
            };

            response.Model = dto;

            return response;
        }

        private async Task<(SyncJob? syncJob, HttpStatusCode statusCode)> GetSyncJobByGroupIdAsync(Guid groupId, string channelId)
        {
            var userId = _httpContextAccessor.HttpContext.User.Claims.FirstOrDefault(x => x.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return (null, HttpStatusCode.Forbidden);

            var channel = await _databaseChannelsRepository.GetChannelAsync(groupId, channelId);

            if (channel == null) return (null, HttpStatusCode.NotFound);

            if (_httpContextAccessor.HttpContext.User.IsInRole(Roles.JOB_TENANT_READER) ||
                _httpContextAccessor.HttpContext.User.IsInRole(Roles.JOB_TENANT_WRITER))
            {
                return (await _databaseSyncJobsRepository.GetSyncJobAsync(channel.SyncJobId), HttpStatusCode.OK);
            }

            if (!await _databaseSyncJobsRepository.GetSyncJobs(true).AnyAsync(x => x.Id == channel.SyncJobId))
            {
                return (null, HttpStatusCode.NotFound);
            }

            var job = await _databaseSyncJobsRepository
                            .GetSyncJobs(true)
                            .FirstOrDefaultAsync(x => x.Id == channel.SyncJobId && x.DestinationOwners.Any(o => o.ObjectId == Guid.Parse(userId)));

            return job != null ? (job, HttpStatusCode.OK) : (null, HttpStatusCode.Forbidden);
        }
    }
}