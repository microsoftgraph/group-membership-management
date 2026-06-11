// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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


        public OperationsController(IRequestHandler<PostOperationRequest, PostOperationResponse> postResetRequestHandler,
                                    IRequestHandler<GetServiceStatusRequest, GetServiceStatusResponse> getServiceStatusRequestHandler)
        {
            _postResetRequestHandler = postResetRequestHandler ?? throw new ArgumentNullException(nameof(postResetRequestHandler));
            _getServiceStatusRequestHandler = getServiceStatusRequestHandler ?? throw new ArgumentNullException(nameof(getServiceStatusRequestHandler));
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
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: ${ex}");
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
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: ${ex}");
            }
        }
    }
}
