// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace WebApi.Controllers.v1.Dashboard
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/dashboard")]
    public class DashboardController : ControllerBase
    {
        private const int DefaultWindowDays = 7;

        private readonly IRequestHandler<GetDashboardSummaryRequest, GetDashboardSummaryResponse> _getDashboardSummaryRequestHandler;
        private readonly IRequestHandler<GetApprovalActivityRequest, GetApprovalActivityResponse> _getApprovalActivityRequestHandler;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(IRequestHandler<GetDashboardSummaryRequest, GetDashboardSummaryResponse> getDashboardSummaryRequestHandler,
                                   IRequestHandler<GetApprovalActivityRequest, GetApprovalActivityResponse> getApprovalActivityRequestHandler,
                                   ILogger<DashboardController> logger)
        {
            _getDashboardSummaryRequestHandler = getDashboardSummaryRequestHandler ?? throw new ArgumentNullException(nameof(getDashboardSummaryRequestHandler));
            _getApprovalActivityRequestHandler = getApprovalActivityRequestHandler ?? throw new ArgumentNullException(nameof(getApprovalActivityRequestHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Authorize(Roles = Models.Roles.DASHBOARD_READER)]
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummaryAsync()
        {
            try
            {
                var response = await _getDashboardSummaryRequestHandler.ExecuteAsync(new GetDashboardSummaryRequest());
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(response),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in {Action}", nameof(GetSummaryAsync));
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: "An unexpected error occurred.");
            }
        }

        [Authorize(Roles = Models.Roles.DASHBOARD_READER)]
        [HttpGet("approvals")]
        public async Task<IActionResult> GetApprovalActivityAsync(int windowDays = DefaultWindowDays)
        {
            try
            {
                var response = await _getApprovalActivityRequestHandler.ExecuteAsync(new GetApprovalActivityRequest(windowDays));
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.OK => Ok(response),
                    _ => Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in {Action}", nameof(GetApprovalActivityAsync));
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: "An unexpected error occurred.");
            }
        }
    }
}
