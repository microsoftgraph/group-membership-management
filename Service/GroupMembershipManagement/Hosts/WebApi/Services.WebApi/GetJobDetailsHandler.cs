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
    public class GetJobDetailsHandler : RequestHandlerBase<GetJobDetailsRequest, GetJobDetailsResponse>
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GetJobDetailsHandler(ILoggingRepository loggingRepository,
                              IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                              IGraphGroupRepository graphGroupRepository,
                              IHttpContextAccessor httpContextAccessor) : base(loggingRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
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

            try
            {
                endpoints = await _graphGroupRepository.GetGroupEndpointsAsync(job.TargetOfficeGroupId);
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