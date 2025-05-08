// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Models.SyncJobChange;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
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
        private readonly IRequestHandler<GetGroupRequest, GetGroupResponse> _getGroupRequestHandler;
        private readonly IRequestHandler<GetChannelRequest, GetChannelResponse> _getChannelRequestHandler;
        private readonly IRequestHandler<GetJobChangesRequest, GetJobChangesResponse> _getJobChangesRequestHandler;

        public JobDetailsController(IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> getJobsRequestHandler,
                                    IRequestHandler<RemoveGMMRequest, RemoveGMMResponse> removeGMMRequestHandler,
                                    IRequestHandler<PatchJobRequest, PatchJobResponse> patchJobRequestHandler,
                                    IRequestHandler<GetGroupRequest, GetGroupResponse> getGroupRequestHandler,
                                    IRequestHandler<GetChannelRequest, GetChannelResponse> getChannelRequestHandler,
                                    IRequestHandler<GetJobChangesRequest, GetJobChangesResponse> getJobChangesRequestHandler)
        {
            _getJobDetailsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
            _removeGMMRequestHandler = removeGMMRequestHandler ?? throw new ArgumentNullException(nameof(removeGMMRequestHandler));
            _getGroupRequestHandler = getGroupRequestHandler ?? throw new ArgumentNullException(nameof(getGroupRequestHandler));
            _getChannelRequestHandler = getChannelRequestHandler ?? throw new ArgumentNullException(nameof(getChannelRequestHandler));
            _patchJobRequestHandler = patchJobRequestHandler;
            _getJobChangesRequestHandler = getJobChangesRequestHandler ?? throw new ArgumentNullException(nameof(getJobChangesRequestHandler));
        }

        [Authorize(Roles = Models.Roles.JOB_OWNER_READER + "," + Models.Roles.JOB_OWNER_WRITER + "," + Models.Roles.JOB_TENANT_READER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpGet("job/{syncJobId}")]
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

        [Authorize(Roles = $"{Models.Roles.JOB_OWNER_READER}, {Models.Roles.JOB_OWNER_WRITER}, {Models.Roles.JOB_TENANT_READER}, {Models.Roles.JOB_TENANT_WRITER}")]
        [HttpGet("group/{groupId}")]
        public async Task<ActionResult<IEnumerable<SyncJob>>> GetGroupDetailsAsync(Guid groupId)
        {
            var response = await _getGroupRequestHandler.ExecuteAsync(new GetGroupRequest(groupId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => Ok(response.Model),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                System.Net.HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }

        [Authorize(Roles = $"{Models.Roles.JOB_OWNER_READER}, {Models.Roles.JOB_OWNER_WRITER}, {Models.Roles.JOB_TENANT_READER}, {Models.Roles.JOB_TENANT_WRITER}")]
        [HttpGet("groups/{groupId}/channels/{channelId}")]
        public async Task<ActionResult<IEnumerable<SyncJob>>> GetChannelDetailsAsync(Guid groupId, string channelId)
        {
            var response = await _getChannelRequestHandler.ExecuteAsync(new GetChannelRequest(groupId, channelId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => Ok(response.Model),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                System.Net.HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }

        [Authorize(Roles = Models.Roles.SUBMISSION_REVIEWER)]
        [HttpPatch("{syncJobId}/review")]
        [Consumes("application/json-patch+json")]
        public async Task<ActionResult> ReviewJobAsync(Guid syncJobId, [FromBody] JsonPatchDocument<SyncJobPatch> patchDocument)
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

                var changeReason = Request.Headers["X-Change-Reason"].ToString();
                var changeReasonValidation = ValidateChangeReason(changeReason, [SyncJobChangeReason.SubmissionApproved.ToString(), SyncJobChangeReason.SubmissionRejected.ToString()],
                    "Invalid change reason. Only 'SubmissionRejected' and 'SubmissionApproved' are allowed.");
                if (changeReasonValidation != null)
                {
                    return changeReasonValidation;
                }

                var businessJustification = Request.Headers["X-Business-Justification"].ToString();

                var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(true, userId, syncJobId, patchDocument, displayName, changeReason, businessJustification));

                var patchJobResponse = new PatchJobResponse
                {
                    StatusCode = response.StatusCode,
                    ErrorCode = response.ErrorCode,
                    ResponseData = response.ResponseData
                };

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(patchJobResponse),
                    System.Net.HttpStatusCode.NotFound => NotFound(patchJobResponse),
                    System.Net.HttpStatusCode.BadRequest => BadRequest(patchJobResponse),
                    System.Net.HttpStatusCode.Forbidden => Forbid(),
                    System.Net.HttpStatusCode.PreconditionFailed => Problem(statusCode: (int)System.Net.HttpStatusCode.PreconditionFailed, detail: response.ErrorCode),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: response.ErrorCode)
                };
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: {ex.Message}");
            }
        }

        [Authorize(Roles = $"{Models.Roles.JOB_OWNER_ENABLER}, {Models.Roles.JOB_OWNER_WRITER}, {Models.Roles.JOB_TENANT_WRITER}")]
        [HttpPatch("{syncJobId}/enable")]
        [Consumes("application/json-patch+json")]
        public async Task<ActionResult> EnableJobAsync(Guid syncJobId, [FromBody] JsonPatchDocument<SyncJobPatch> patchDocument)
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

                var changeReason = Request.Headers["X-Change-Reason"].ToString();
                var changeReasonValidation = ValidateChangeReason(changeReason, [SyncJobChangeReason.StatusUpdate.ToString()],
                    "Invalid change reason. Only 'StatusUpdate' is allowed.");
                if (changeReasonValidation != null)
                {
                    return changeReasonValidation;
                }

                var businessJustification = Request.Headers["X-Business-Justification"].ToString();

                var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(true, userId, syncJobId, patchDocument, displayName, changeReason, businessJustification));

                var patchJobResponse = new PatchJobResponse
                {
                    StatusCode = response.StatusCode,
                    ErrorCode = response.ErrorCode,
                    ResponseData = response.ResponseData
                };

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(patchJobResponse),
                    System.Net.HttpStatusCode.NotFound => NotFound(patchJobResponse),
                    System.Net.HttpStatusCode.BadRequest => BadRequest(patchJobResponse),
                    System.Net.HttpStatusCode.Forbidden => Forbid(),
                    System.Net.HttpStatusCode.PreconditionFailed => Problem(statusCode: (int)System.Net.HttpStatusCode.PreconditionFailed, detail: response.ErrorCode),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: response.ErrorCode)
                };
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: {ex.Message}");
            }
        }

        [Authorize(Roles = $"{Models.Roles.JOB_OWNER_WRITER}, {Models.Roles.JOB_TENANT_WRITER}")]
        [HttpPatch("{syncJobId}/update")]
        [Consumes("application/json-patch+json")]
        public async Task<ActionResult> UpdateJobAsync(Guid syncJobId, [FromBody] JsonPatchDocument<SyncJobPatch> patchDocument)
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

                var changeReason = Request.Headers["X-Change-Reason"].ToString();
                var changeReasonValidation = ValidateChangeReason(changeReason, [SyncJobChangeReason.Update.ToString()],
                    "Invalid change reason. Only 'Update' is allowed.");
                if (changeReasonValidation != null)
                {
                    return changeReasonValidation;
                }

                var businessJustification = Request.Headers["X-Business-Justification"].ToString();

                // This is a double check right now, keeping this in place for future use when the api call is open up to all users
                var isAllowed = User.IsInRole(Models.Roles.JOB_TENANT_WRITER) || User.IsInRole(Models.Roles.SUBMISSION_REVIEWER);
                var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(isAllowed, userId, syncJobId, patchDocument, displayName, changeReason, businessJustification));

                var patchJobResponse = new PatchJobResponse
                {
                    StatusCode = response.StatusCode,
                    ErrorCode = response.ErrorCode,
                    ResponseData = response.ResponseData
                };

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(patchJobResponse),
                    System.Net.HttpStatusCode.NotFound => NotFound(patchJobResponse),
                    System.Net.HttpStatusCode.BadRequest => BadRequest(patchJobResponse),
                    System.Net.HttpStatusCode.Forbidden => Forbid(),
                    System.Net.HttpStatusCode.PreconditionFailed => Problem(statusCode: (int)System.Net.HttpStatusCode.PreconditionFailed, detail: response.ErrorCode),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: response.ErrorCode)
                };
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: {ex.Message}");
            }
        }

        [Authorize(Roles = $"{Models.Roles.JOB_OWNER_DELETER},{Models.Roles.JOB_OWNER_WRITER},{Models.Roles.JOB_TENANT_WRITER}")]
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

        [Authorize(Roles = Models.Roles.JOB_OWNER_READER + "," + Models.Roles.JOB_OWNER_WRITER + "," + Models.Roles.JOB_TENANT_READER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpGet("history/configuration/{syncJobId}")]
        public async Task<ActionResult<IEnumerable<SyncJobChangeDTO>>> GetJobChangesAsync(Guid syncJobId)
        {
            var response = await _getJobChangesRequestHandler.ExecuteAsync(new GetJobChangesRequest(syncJobId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => Ok(response.Changes),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                System.Net.HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }
        private ActionResult ValidateChangeReason(string changeReason, List<string> expectedReasons, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(changeReason))
            {
                return BadRequest(new PatchJobResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "ChangeReasonIsRequired"
                });
            }

            if (!expectedReasons.Contains(changeReason))
            {
                return BadRequest(new PatchJobResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "InvalidChangeReason",
                    ResponseData = new List<string> { errorMessage }
                });
            }

            return null;
        }
    }
}
