// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;

namespace WebApi.Controllers.v1.Configuration
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/configuration")]
    public class ConfigurationController : ControllerBase
    {
        private readonly IRequestHandler<GetConfigurationRequest, GetConfigurationResponse> _getConfigurationRequestHandler;

        public ConfigurationController(IRequestHandler<GetConfigurationRequest, GetConfigurationResponse> getConfigurationRequestHandler)
        {
            _getConfigurationRequestHandler = getConfigurationRequestHandler ?? throw new ArgumentNullException(nameof(getConfigurationRequestHandler));
        }

        [EnableQuery()]
        [Authorize(Roles = "Admin")]
        [HttpGet()]
        public async Task<ActionResult<IEnumerable<SyncJob>>> GetConfigurationAsync(Guid id)
        {
            var response = await _getConfigurationRequestHandler.ExecuteAsync(new GetConfigurationRequest(id));
            return Ok(response.Model);
        }
    }
}
