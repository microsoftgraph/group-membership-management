// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;

namespace WebApi.Controllers.v1.Jobs
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/jobDetails")]
    public class JobDetailsController : ControllerBase
    {
        private readonly IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> _getJobDetailsRequestHandler;
        private readonly IRequestHandler<PatchJobRequest, PatchJobResponse> _patchJobRequestHandler;

        public JobDetailsController(IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> getJobsRequestHandler,
                                    IRequestHandler<PatchJobRequest, PatchJobResponse> patchJobRequestHandler)
        {
            _getJobDetailsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
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

        [Authorize()]
        [HttpPatch("{syncJobId}")]
        [Consumes("application/json-patch+json")]
        public async Task<ActionResult> UpdateSyncJobAsync(Guid syncJobId, [FromBody] JsonPatchDocument<SyncJobPatch> patchDocument)
        {
            var user = User;
            var userName = user.Identity?.Name!;
            var isAllowed = User.IsInRole(Models.Roles.TENANT_ADMINISTRATOR) || User.IsInRole(Models.Roles.TENANT_SUBMISSION_REVIEWER);
            var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(isAllowed, userName, syncJobId, patchDocument));

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
    }
}
