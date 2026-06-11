// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.AspNetCore.Mvc;
using Models.SyncJobChange;
using Models.SyncJobHistory;
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
        private readonly IRequestHandler<GetSyncJobHistoryRequest, GetSyncJobHistoryResponse> _getSyncJobHistoryRequestHandler;
        private readonly IRequestHandler<GetMembershipDownloadRequest, GetMembershipDownloadResponse> _getMembershipDownloadRequestHandler;
        private readonly IRequestHandler<GetThresholdNotificationRequest, GetThresholdNotificationResponse> _getThresholdNotificationRequestHandler;

        public JobDetailsController(IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> getJobsRequestHandler,
                                    IRequestHandler<RemoveGMMRequest, RemoveGMMResponse> removeGMMRequestHandler,
                                    IRequestHandler<PatchJobRequest, PatchJobResponse> patchJobRequestHandler,
                                    IRequestHandler<GetGroupRequest, GetGroupResponse> getGroupRequestHandler,
                                    IRequestHandler<GetChannelRequest, GetChannelResponse> getChannelRequestHandler,
                                    IRequestHandler<GetJobChangesRequest, GetJobChangesResponse> getJobChangesRequestHandler,
                                    IRequestHandler<GetSyncJobHistoryRequest, GetSyncJobHistoryResponse> getSyncJobHistoryRequestHandler,
                                    IRequestHandler<GetMembershipDownloadRequest, GetMembershipDownloadResponse> getMembershipDownloadRequestHandler,
                                    IRequestHandler<GetThresholdNotificationRequest, GetThresholdNotificationResponse> getThresholdNotificationRequestHandler)
        {
            _getJobDetailsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
            _removeGMMRequestHandler = removeGMMRequestHandler ?? throw new ArgumentNullException(nameof(removeGMMRequestHandler));
            _getGroupRequestHandler = getGroupRequestHandler ?? throw new ArgumentNullException(nameof(getGroupRequestHandler));
            _getChannelRequestHandler = getChannelRequestHandler ?? throw new ArgumentNullException(nameof(getChannelRequestHandler));
            _patchJobRequestHandler = patchJobRequestHandler;
            _getJobChangesRequestHandler = getJobChangesRequestHandler ?? throw new ArgumentNullException(nameof(getJobChangesRequestHandler));
            _getSyncJobHistoryRequestHandler = getSyncJobHistoryRequestHandler ?? throw new ArgumentNullException(nameof(getSyncJobHistoryRequestHandler));
            _getMembershipDownloadRequestHandler = getMembershipDownloadRequestHandler ?? throw new ArgumentNullException(nameof(getMembershipDownloadRequestHandler));
            _getThresholdNotificationRequestHandler = getThresholdNotificationRequestHandler ?? throw new ArgumentNullException(nameof(getThresholdNotificationRequestHandler));
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

        [Authorize(Roles = $"{Models.Roles.SUBMISSION_REVIEWER}, {Models.Roles.SUBMISSION_REJECTOR}")]
        [HttpPatch("{syncJobId}/review")]
        [Consumes("application/json")]
        public async Task<ActionResult> ReviewJobAsync(Guid syncJobId, [FromBody] PatchJobRequestDTO requestDTO)
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

                var changeReasonValidation = ValidateChangeReason(requestDTO.ChangeReason, [SyncJobChangeReason.SubmissionApproved.ToString(), SyncJobChangeReason.SubmissionRejected.ToString()],
                    "Invalid change reason. Only 'SubmissionRejected' and 'SubmissionApproved' are allowed.");
                if (changeReasonValidation != null)
                {
                    return changeReasonValidation;
                }

                var (titlesValue, hasTitlesOp) = ExtractAndRemoveTitles(requestDTO.PatchOperation);
                var patchDocument = ConvertToPatchDocument(requestDTO.PatchOperation);
                var canApproveJob = User.IsInRole(Models.Roles.SUBMISSION_REVIEWER);

                var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(true, userId, syncJobId, patchDocument, displayName, requestDTO.ChangeReason, requestDTO.BusinessJustification, canApproveJob, titlesValue, hasTitlesOp));

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
        [Consumes("application/json")]
        public async Task<ActionResult> EnableJobAsync(Guid syncJobId, [FromBody] PatchJobRequestDTO requestDTO)
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

                var changeReasonValidation = ValidateChangeReason(requestDTO.ChangeReason, [SyncJobChangeReason.StatusUpdate.ToString()],
                    "Invalid change reason. Only 'StatusUpdate' is allowed.");
                if (changeReasonValidation != null)
                {
                    return changeReasonValidation;
                }

                var (titlesValue, hasTitlesOp) = ExtractAndRemoveTitles(requestDTO.PatchOperation);
                var patchDocument = ConvertToPatchDocument(requestDTO.PatchOperation);

                var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(true, userId, syncJobId, patchDocument, displayName, requestDTO.ChangeReason, requestDTO.BusinessJustification, false, titlesValue, hasTitlesOp));

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
        [Consumes("application/json")]
        public async Task<ActionResult> UpdateJobAsync(Guid syncJobId, [FromBody] PatchJobRequestDTO requestDTO)
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

                var changeReasonValidation = ValidateChangeReason(requestDTO.ChangeReason, [SyncJobChangeReason.Update.ToString()],
                    "Invalid change reason. Only 'Update' is allowed.");
                if (changeReasonValidation != null)
                {
                    return changeReasonValidation;
                }

                var (titlesValue, hasTitlesOp) = ExtractAndRemoveTitles(requestDTO.PatchOperation);
                var patchDocument = ConvertToPatchDocument(requestDTO.PatchOperation);
                
                // This is a double check right now, keeping this in place for future use when the api call is open up to all users
                var isAllowed = User.IsInRole(Models.Roles.JOB_TENANT_WRITER) || User.IsInRole(Models.Roles.SUBMISSION_REVIEWER);

                var response = await _patchJobRequestHandler.ExecuteAsync(new PatchJobRequest(isAllowed, userId, syncJobId, patchDocument, displayName, requestDTO.ChangeReason, requestDTO.BusinessJustification, false, titlesValue, hasTitlesOp));

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

        [Authorize(Roles = Models.Roles.JOB_TENANT_READER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpGet("history/sync/{syncJobId}")]
        public async Task<ActionResult<IEnumerable<SyncJobHistory>>> GetSyncJobHistoryAsync(Guid syncJobId)
        {
            var response = await _getSyncJobHistoryRequestHandler.ExecuteAsync(new GetSyncJobHistoryRequest(syncJobId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => Ok(response.History),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                System.Net.HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }

        [Authorize(Roles = Models.Roles.JOB_OWNER_READER + "," + Models.Roles.JOB_OWNER_WRITER + "," + Models.Roles.JOB_TENANT_READER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpGet("history/sync/{syncJobId}/threshold-notification")]
        public async Task<ActionResult> GetThresholdNotificationAsync(Guid syncJobId)
        {
            var response = await _getThresholdNotificationRequestHandler.ExecuteAsync(new GetThresholdNotificationRequest(syncJobId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => Ok(response),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }

        [Authorize(Roles = Models.Roles.JOB_TENANT_READER + "," + Models.Roles.JOB_TENANT_WRITER)]
        [HttpGet("history/sync/{syncJobId}/search-user/{userObjectId}")]
        public async Task<ActionResult<SearchSyncHistoryByUserResponse>> SearchSyncJobHistoryByUserAsync(Guid syncJobId, Guid userObjectId, [FromQuery] string? requestId = null)
        {
            var handler = HttpContext.RequestServices.GetRequiredService<IRequestHandler<SearchSyncHistoryByUserRequest, SearchSyncHistoryByUserResponse>>();
            var response = await handler.ExecuteAsync(new SearchSyncHistoryByUserRequest(syncJobId, userObjectId, requestId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => Ok(response),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                System.Net.HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }

        [Authorize(Roles = $"{Models.Roles.JOB_TENANT_READER},{Models.Roles.JOB_TENANT_WRITER},{Models.Roles.SUBMISSION_REVIEWER}")]
        [HttpGet("history/sync/{syncJobId}/runs/{runId}/download")]
        public async Task<ActionResult> DownloadMembershipAsync(Guid syncJobId, Guid runId)
        {
            var response = await _getMembershipDownloadRequestHandler.ExecuteAsync(new GetMembershipDownloadRequest(syncJobId, runId));

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => File(response.FileContent!, "application/zip", response.FileName),
                System.Net.HttpStatusCode.NotFound => NotFound(),
                System.Net.HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
            };
        }

        private (string titlesValue, bool hasTitlesOp) ExtractAndRemoveTitles(List<PatchOperation> patchOperations)
        {
            var titlesOp = patchOperations?.FirstOrDefault(op => op.Path == "/Titles");
            var hasTitlesOp = titlesOp != null;
            var titlesValue = titlesOp?.Value?.ToString();

            if (hasTitlesOp && patchOperations != null)
            {
                patchOperations.RemoveAll(op => op.Path == "/Titles");
            }

            return (titlesValue, hasTitlesOp);
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

        private JsonPatchDocument<SyncJobPatch> ConvertToPatchDocument(List<PatchOperation> operations)
        {
            var patchDoc = new JsonPatchDocument<SyncJobPatch>();
            
            if (operations == null || operations.Count == 0)
            {
                return patchDoc;
            }

            foreach (var op in operations)
            {
                // Handle the actual value type
                object actualValue = op.Value;
                
                // If the value is a JsonElement, extract its actual value
                if (op.Value != null && op.Value.GetType().Name == "JsonElement")
                {
                    var jsonElement = (System.Text.Json.JsonElement)op.Value;
                    
                    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        actualValue = jsonElement.GetString();
                    }
                    else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        actualValue = jsonElement.GetRawText();
                    }
                    else
                    {
                        actualValue = jsonElement.GetRawText();
                    }
                }
                
                // Check if this is an empty Titles operation
                bool isTitlesPath = op.Path?.Equals("/Titles", StringComparison.OrdinalIgnoreCase) == true;
                bool isReplace = op.Op?.ToLower() == "replace";
                bool isValueEmpty = actualValue == null || (actualValue is string str && string.IsNullOrEmpty(str));
                
                if (isTitlesPath && isReplace && isValueEmpty)
                {
                    // Skip empty Titles operation - it will be handled separately in the handler
                    continue;
                }
                switch (op.Op?.ToLower())
                {
                    case "replace":
                        patchDoc.Operations.Add(new Operation<SyncJobPatch>
                        {
                            op = "replace",
                            path = op.Path,
                            value = actualValue,
                            from = null
                        });
                        break;
                    case "add":
                        patchDoc.Operations.Add(new Operation<SyncJobPatch>
                        {
                            op = "add",
                            path = op.Path,
                            value = actualValue,
                            from = null
                        });
                        break;
                    case "remove":
                        patchDoc.Operations.Add(new Operation<SyncJobPatch>
                        {
                            op = "remove",
                            path = op.Path,
                            from = null
                        });
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported JSON Patch operation: {op.Op}");
                }
            }

            return patchDoc;
        }
    }
}
