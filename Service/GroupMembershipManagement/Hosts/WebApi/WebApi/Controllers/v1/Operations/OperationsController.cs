// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Security.Claims;
using ServiceOperations = Models.Operations;

namespace WebApi.Controllers.v1.Operations
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/operations")]
    public class OperationsController : ControllerBase
    {
        private readonly IRequestHandler<PostOperationRequest, PostOperationResponse> _postResetRequestHandler;
        private readonly IRequestHandler<GetServiceStatusRequest, GetServiceStatusResponse> _getServiceStatusRequestHandler;
        private readonly ILogger<OperationsController> _logger;


        public OperationsController(IRequestHandler<PostOperationRequest, PostOperationResponse> postResetRequestHandler,
                                    IRequestHandler<GetServiceStatusRequest, GetServiceStatusResponse> getServiceStatusRequestHandler,
                                    ILogger<OperationsController> logger)
        {
            _postResetRequestHandler = postResetRequestHandler ?? throw new ArgumentNullException(nameof(postResetRequestHandler));
            _getServiceStatusRequestHandler = getServiceStatusRequestHandler ?? throw new ArgumentNullException(nameof(getServiceStatusRequestHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Authorize(Roles = Models.Roles.RESET_ADMINISTRATOR)]
        [HttpPost("{operation}")]
        public async Task<IActionResult> ProcessOperationAsync(ServiceOperations operation)
        {
            try
            {
                var claimsIdentity = User.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? Guid.Empty.ToString();
                var response = await _postResetRequestHandler.ExecuteAsync(new PostOperationRequest(operation, Guid.Parse(userId)));
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(response),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in {Action}", nameof(ProcessOperationAsync));
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: "An unexpected error occurred.");
            }
        }

        [Authorize]
        [HttpGet("servicestatus")]
        public async Task<IActionResult> GetCurrentStatusAsync()
        {
            try
            {
                var response = await _getServiceStatusRequestHandler.ExecuteAsync(new GetServiceStatusRequest());
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(response),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in {Action}", nameof(GetCurrentStatusAsync));
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: "An unexpected error occurred.");
            }
        }
    }
}
