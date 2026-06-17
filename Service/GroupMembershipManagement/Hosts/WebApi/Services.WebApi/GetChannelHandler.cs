// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using WebApi.Models;
using Microsoft.Extensions.Logging;
using SyncJobDetailsDTO = WebApi.Models.DTOs.SyncJobDetails;

namespace Services
{
    public class GetChannelHandler : RequestHandlerBase<GetChannelRequest, GetChannelResponse>
    {
        private readonly ILogger<GetChannelHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly IDatabaseTitlesRepository _titlesRepository;
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GetChannelHandler(ILogger<GetChannelHandler> logger,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IDatabaseChannelsRepository databaseChannelsRepository,
                              IDatabaseTitlesRepository titlesRepository,
                              ITeamsChannelRepository teamsChannelRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IHttpContextAccessor httpContextAccessor) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _titlesRepository = titlesRepository ?? throw new ArgumentNullException(nameof(titlesRepository));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
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
                _logger.ChannelGroupEndpointsRetrievalFailed(ex);
            }

            var type = job.MembershipType;

            string? targetGroupName = null;
            try
            {
                targetGroupName = await _graphGroupRepository.GetGroupNameAsync(request.GroupId);
            }
            catch (Exception ex)
            {
                _logger.GroupNameRetrievalFailed(request.GroupId, ex);
            }

            string? targetChannelName = null;
            try
            {
                targetChannelName = await _teamsChannelRepository.GetTeamsChannelNameAsync(
                    new Models.Entities.AzureADTeamsChannel { ObjectId = request.GroupId, ChannelId = request.ChannelId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to retrieve channel name for ChannelId {ChannelId}", request.ChannelId);
            }

            var titles = await _titlesRepository.GetTitlesAsync(job.Id);
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
                TargetChannelId = request.ChannelId,
                TargetChannelName = targetChannelName,
                TargetDestinationType = type,
                LastSuccessfulRunTime = job.LastSuccessfulRunTime,
                EstimatedNextRunTime = estimatedNextRunTime,
                Status = job.Status,
                Titles = titles
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