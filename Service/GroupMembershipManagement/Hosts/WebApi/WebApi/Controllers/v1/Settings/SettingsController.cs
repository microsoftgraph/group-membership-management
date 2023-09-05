// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;

namespace WebApi.Controllers.v1.Settings
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/settings")]
    public class SettingsController : ControllerBase
    {
        private readonly IRequestHandler<GetSettingsRequest, GetSettingsResponse> _getSettingsRequestHandler;

        public SettingsController(IRequestHandler<GetSettingsRequest, GetSettingsResponse> getSettingsRequestHandler)
        {
            _getSettingsRequestHandler = getSettingsRequestHandler ?? throw new ArgumentNullException(nameof(getSettingsRequestHandler));
        }

        [EnableQuery()]
        [Authorize(Roles = "Admin")]
        [HttpGet()]
        public async Task<ActionResult<string>> GetSettingsAsync(string key)
        {
            var response = await _getSettingsRequestHandler.ExecuteAsync(new GetSettingsRequest(key));
            return Ok(response.Model);
        }
    }
}
