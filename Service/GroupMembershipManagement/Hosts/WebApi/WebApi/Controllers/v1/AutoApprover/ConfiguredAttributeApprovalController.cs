// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace WebApi.Controllers.v1.AutoApprover
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/autoApprover/configuredAttribute")]
    [Authorize(Roles = $"{Models.Roles.AUTO_APPROVER_ADMINISTRATOR},{Models.Roles.AUTO_APPROVER_READER}")]
    public class ConfiguredAttributeApprovalController : ControllerBase
    {
        private readonly IRequestHandler<GetConfiguredAttributeApprovalRequest, GetConfiguredAttributeApprovalResponse> _getConfigurationHandler;
        private readonly IRequestHandler<SaveConfiguredAttributeApprovalRequest, SaveConfiguredAttributeApprovalResponse> _saveConfigurationHandler;
        private readonly ILogger<ConfiguredAttributeApprovalController> _logger;

        public ConfiguredAttributeApprovalController(
            IRequestHandler<GetConfiguredAttributeApprovalRequest, GetConfiguredAttributeApprovalResponse> getConfigurationHandler,
            IRequestHandler<SaveConfiguredAttributeApprovalRequest, SaveConfiguredAttributeApprovalResponse> saveConfigurationHandler,
            ILogger<ConfiguredAttributeApprovalController> logger)
        {
            _getConfigurationHandler = getConfigurationHandler ?? throw new ArgumentNullException(nameof(getConfigurationHandler));
            _saveConfigurationHandler = saveConfigurationHandler ?? throw new ArgumentNullException(nameof(saveConfigurationHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("configuration")]
        public Task<IActionResult> GetConfigurationAsync() =>
            ExecuteAsync(async () => Ok(
                (await _getConfigurationHandler.ExecuteAsync(new GetConfiguredAttributeApprovalRequest())).Configuration));

        [HttpPut("configuration")]
        [Authorize(Roles = Models.Roles.AUTO_APPROVER_ADMINISTRATOR)]
        [RequestSizeLimit(65536)]
        public Task<IActionResult> PutConfigurationAsync([FromBody] ConfiguredAttributeApprovalConfiguration body) =>
            ExecuteAsync(async () => Ok(
                (await _saveConfigurationHandler.ExecuteAsync(new SaveConfiguredAttributeApprovalRequest(body))).Configuration));

        private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> operation)
        {
            try
            {
                return await operation();
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { exception.Message });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unhandled exception in configured attribute approval");
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError,
                               detail: "An error occurred while processing the configured attribute approval request");
            }
        }
    }
}
