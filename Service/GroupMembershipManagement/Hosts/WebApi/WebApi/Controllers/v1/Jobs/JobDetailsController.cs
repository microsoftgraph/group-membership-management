// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Security.Claims;
using WebApi.Models.DTOs;

namespace WebApi.Controllers.v1.Jobs
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/jobDetails")]
    public class JobDetailsController : ControllerBase
    {
        private readonly IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> _getJobDetailsRequestHandler;
        private readonly IRequestHandler<RemoveGMMRequest, RemoveGMMResponse> _removeGMMRequestHandler;
        private readonly IRequestHandler<PatchJobRequest, PatchJobResponse> _patchJobRequestHandler;

        public JobDetailsController(IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> getJobsRequestHandler,
                                    IRequestHandler<RemoveGMMRequest, RemoveGMMResponse> removeGMMRequestHandler,
                                    IRequestHandler<PatchJobRequest, PatchJobResponse> patchJobRequestHandler)
        {
            _getJobDetailsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
            _removeGMMRequestHandler = removeGMMRequestHandler ?? throw new ArgumentNullException(nameof(removeGMMRequestHandler));
            _patchJobRequestHandler = patchJobRequestHandler;
        }

        [Authorize()]
        [HttpGet()]
        public async Task<ActionResult<IEnumerable<SyncJob>>> GetJobDetailsAsync(Guid syncJobId)
        {
            var response = await _getJobDetailsRequestHandler.ExecuteAsync(new GetJobDetailsRequest(syncJobId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => Ok(response.Model),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                System.Net.HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }

        [Authorize(Roles = Models.Roles.JOB_TENANT_WRITER + "," + Models.Roles.SUBMISSION_REVIEWER)]
        [HttpPatch("{syncJobId}")]
        [Consumes("application/json-patch+json")]
        public async Task<ActionResult> UpdateSyncJobAsync(Guid syncJobId, [FromBody] JsonPatchDocument<SyncJobPatch> patchDocument)
        {
            try
            {
                var user = User;
                var claimsIdentity = User.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return new ForbidResult();
                }

                // This is a double check right now, keeping this in place for future use when the api call is open up to all users
                var isAllowed = User.IsInRole(Models.Roles.JOB_TENANT_WRITER) || User.IsInRole(Models.Roles.SUBMISSION_REVIEWER);
            
                var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(isAllowed, userId, syncJobId, patchDocument));

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(),
                    System.Net.HttpStatusCode.NotFound => NotFound(),
                    System.Net.HttpStatusCode.BadRequest => BadRequest(response.ErrorCode),
                    System.Net.HttpStatusCode.Forbidden => Forbid(),
                    System.Net.HttpStatusCode.PreconditionFailed => Problem(statusCode: (int)System.Net.HttpStatusCode.PreconditionFailed, detail: response.ErrorCode),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
                };
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: ${ex}");
            }
        }

        [Authorize(Roles = Models.Roles.JOB_TENANT_WRITER + "," + Models.Roles.JOB_CREATOR)]
        [HttpPost("{syncJobId}/removeGMM")]
        public async Task<ActionResult> RemoveGMMAsync(Guid syncJobId)
        {
            try
            {
                var user = User;
                var claimsIdentity = User.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return new ForbidResult();
                }

                var isJobTenantWriter = User.IsInRole(Models.Roles.JOB_TENANT_WRITER);
                var response = await _removeGMMRequestHandler.ExecuteAsync(new RemoveGMMRequest(userId, isJobTenantWriter, syncJobId));

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(),
                    System.Net.HttpStatusCode.NotFound => NotFound(),
                    System.Net.HttpStatusCode.BadRequest => BadRequest(response.ErrorCode),
                    System.Net.HttpStatusCode.Forbidden => Forbid(),
                    System.Net.HttpStatusCode.PreconditionFailed => Problem(statusCode: (int)System.Net.HttpStatusCode.PreconditionFailed, detail: response.ErrorCode),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
                };
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: ${ex}");
            }
        }
    }
}
