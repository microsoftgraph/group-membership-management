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
        private readonly IRequestHandler<GetSettingRequest, GetSettingResponse> _getSettingRequestHandler;
        private readonly IRequestHandler<UpdateSettingRequest, UpdateSettingResponse> _updateSettingRequestHandler;

        public SettingsController(
            IRequestHandler<GetSettingRequest, GetSettingResponse> getSettingRequestHandler,
            IRequestHandler<UpdateSettingRequest, UpdateSettingResponse> updateSettingRequestHandler)
        {
            _getSettingRequestHandler = getSettingRequestHandler ?? throw new ArgumentNullException(nameof(getSettingRequestHandler));
            _updateSettingRequestHandler = updateSettingRequestHandler ?? throw new ArgumentNullException(nameof(updateSettingRequestHandler));
        }

        [EnableQuery()]
        [Authorize(Roles = "Admin")]
        [HttpGet()]
        public async Task<ActionResult<string>> GetSettingByKeyAsync(string key)
        {
            var response = await _getSettingRequestHandler.ExecuteAsync(new GetSettingRequest(key));
            return Ok(response.Model);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{key}")]
        public async Task<ActionResult<Setting>> UpdateSettingAsync(string key, [FromBody] string value)
        {
            var response = await _updateSettingRequestHandler.ExecuteAsync(new UpdateSettingRequest(key, value));
            return Ok(response.Model); 
        }

    }
}
