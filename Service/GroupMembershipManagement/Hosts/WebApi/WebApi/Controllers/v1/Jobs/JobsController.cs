// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;
using SyncJobModel = Models.SyncJob;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;
using System.Net;
using System.Security.Claims;

namespace WebApi.Controllers.v1.Jobs
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/jobs")]
    public class JobsController : ControllerBase
    {
        private readonly IRequestHandler<GetJobsRequest, GetJobsResponse> _getJobsRequestHandler;
        private readonly IRequestHandler<PostJobRequest, PostJobResponse> _postJobRequestHandler;
        private readonly IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> _getJobDetailsRequestHandler;

        public JobsController(
            IRequestHandler<GetJobsRequest, GetJobsResponse> getJobsRequestHandler,
            IRequestHandler<PostJobRequest, PostJobResponse> postJobRequestHandler,
            IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> getJobDetailsRequestHandler)
        {
            _getJobsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
            _postJobRequestHandler = postJobRequestHandler ?? throw new ArgumentNullException(nameof(postJobRequestHandler));
            _getJobDetailsRequestHandler = getJobDetailsRequestHandler ?? throw new ArgumentNullException(nameof(getJobDetailsRequestHandler));
        }

        [Authorize(Roles = Models.Roles.JOB_OWNER_READER + "," + Models.Roles.JOB_OWNER_WRITER + "," + Models.Roles.JOB_TENANT_READER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpGet()]
        public async Task<ActionResult<PagedResponse<SyncJob>>> GetJobsAsync(ODataQueryOptions<SyncJobModel> queryOptions, [FromQuery] string? customSortBy = null)
        {
            var response = await _getJobsRequestHandler.ExecuteAsync(new GetJobsRequest { QueryOptions = queryOptions, CustomSortBy = customSortBy });
            
            var pageSize = queryOptions?.Top?.Value ?? 10;
            var totalItems = response.TotalNumberOfPages * pageSize;
            
            var pagedResponse = new PagedResponse<SyncJob>
            {
                Items = response.Model ?? new List<SyncJob>(),
                TotalNumberOfPages = response.TotalNumberOfPages,
                CurrentPage = response.CurrentPage,
                PageSize = pageSize,
                TotalItems = totalItems
            };

            return Ok(pagedResponse);
        }

        [Authorize(Roles = Models.Roles.JOB_OWNER_WRITER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpPost()]
        public async Task<ActionResult> PostJobAsync([FromBody] NewSyncJobDTO newSyncJob)
        {
            try
            {
                var user = User;
                var claimsIdentity = User.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
                var displayName = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "name")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return new ForbidResult();
                }

                var isJobTenantWriter = User.IsInRole(Models.Roles.JOB_TENANT_WRITER);
                var businessJustification = newSyncJob.BusinessJustification;
                var response = await _postJobRequestHandler.ExecuteAsync(new PostJobRequest(userId, newSyncJob, isJobTenantWriter, displayName, businessJustification));

                switch (response.StatusCode)
                {
                    case HttpStatusCode.Created:
                        return new CreatedResult($"api/jobs/{response.NewSyncJobId}", response);
                    case HttpStatusCode.BadRequest:
                        return new BadRequestObjectResult(response);
                    case HttpStatusCode.Forbidden:
                        return new ForbidResult();
                    case HttpStatusCode.InternalServerError:
                        return new ObjectResult(response) { StatusCode = (int)HttpStatusCode.InternalServerError };
                    default:
                        return new BadRequestResult();
                }
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: ${ex}");
            }
        }

        [Authorize(Roles = Models.Roles.JOB_OWNER_READER + "," + Models.Roles.JOB_OWNER_WRITER + "," + Models.Roles.JOB_TENANT_READER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpPost("bulkDownload")]
        public async Task<ActionResult<IEnumerable<SyncJob>>> BulkGetJobsDetailsAsync([FromBody] string[] syncJobIds)
        {
            var jobDetailsResponses = new List<GetJobDetailsResponse>();

            for (int i = 0; i < syncJobIds.Length; i++)
            {
                if (!Guid.TryParse(syncJobIds[i], out Guid guid))
                {
                    return BadRequest($"Invalid GUID format: {syncJobIds[i]}");
                }

                syncJobIds[i] = guid.ToString();
                var response = await _getJobDetailsRequestHandler.ExecuteAsync(new GetJobDetailsRequest(guid));
                jobDetailsResponses.Add(response);
            }

            var successfulJobs = jobDetailsResponses
                .Where(r => r.StatusCode == System.Net.HttpStatusCode.OK)
                .Select(r => r.Model)
                .OfType<SyncJobDetails>()
                .ToList();

            return successfulJobs.Count == 0
                ? NotFound()
                : Ok(successfulJobs);
        }
    }
}