// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Models;
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
        private readonly IRequestHandler<GetAllSettingsRequest, GetAllSettingsResponse> _getAllSettingsRequestHandler;
        private readonly IRequestHandler<PatchSettingRequest, NullResponse> _patchSettingRequestHandler;

        public SettingsController(
            IRequestHandler<GetSettingRequest, GetSettingResponse> getSettingRequestHandler,
            IRequestHandler<GetAllSettingsRequest, GetAllSettingsResponse> getAllSettingsRequestHandler,
            IRequestHandler<PatchSettingRequest, NullResponse> patchSettingRequestHandler)
        {
            _getSettingRequestHandler = getSettingRequestHandler ?? throw new ArgumentNullException(nameof(getSettingRequestHandler));
            _getAllSettingsRequestHandler = getAllSettingsRequestHandler ?? throw new ArgumentNullException(nameof(getAllSettingsRequestHandler));
            _patchSettingRequestHandler = patchSettingRequestHandler ?? throw new ArgumentNullException(nameof(patchSettingRequestHandler));
        }

        [Authorize()]
        [HttpGet("{settingKey}")]
        public async Task<IActionResult> GetSettingByKeyAsync(SettingKey settingKey)
        {
            try
            {
                var response = await _getSettingRequestHandler.ExecuteAsync(new GetSettingRequest(settingKey));
                if (response.Model != null)
                {
                    return Ok(response.Model);
                }
                return NotFound();
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }
        }

        [Authorize()]
        [HttpGet]
        public async Task<IActionResult> GetAllSettingsAsync()
        {
            try
            {
                var response = await _getAllSettingsRequestHandler.ExecuteAsync(new GetAllSettingsRequest());
                return Ok(response.Settings);
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }
        }

        [Authorize()]
        [HttpPatch("{settingKey}")]
        public async Task<IActionResult> PatchSettingAsync(SettingKey settingKey, [FromBody] string settingValue)
        {
            var isAdmin = User.IsInRole(Models.Roles.TENANT_ADMINISTRATOR);
            if (!isAdmin)
                return Unauthorized();

            try
            {
                await _patchSettingRequestHandler.ExecuteAsync(new PatchSettingRequest(settingKey, settingValue));
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception)
            {
                return StatusCode(500);
            }
        }
    }
}
