// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;

namespace WebApi.Controllers.v1.Settings
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/settings")]
    public class SettingsController : ControllerBase
    {
        private static readonly IReadOnlySet<SettingKey> AISettingKeys = new HashSet<SettingKey>
        {
            SettingKey.IsAICopilotEnabled,
            SettingKey.CopilotTemperature,
            SettingKey.CopilotTopP,
            SettingKey.CopilotInstructions
        };

        private readonly IRequestHandler<GetSettingRequest, GetSettingResponse> _getSettingRequestHandler;
        private readonly IRequestHandler<GetAllSettingsRequest, GetAllSettingsResponse> _getAllSettingsRequestHandler;
        private readonly IRequestHandler<PatchSettingRequest, NullResponse> _patchSettingRequestHandler;
        private readonly IRequestHandler<GetSupportEmailRequest, GetSupportEmailResponse> _getSupportEmailRequestHandler;

        public SettingsController(
            IRequestHandler<GetSettingRequest, GetSettingResponse> getSettingRequestHandler,
            IRequestHandler<GetAllSettingsRequest, GetAllSettingsResponse> getAllSettingsRequestHandler,
            IRequestHandler<PatchSettingRequest, NullResponse> patchSettingRequestHandler,
            IRequestHandler<GetSupportEmailRequest, GetSupportEmailResponse> getSupportEmailRequestHandler)
        {
            _getSettingRequestHandler = getSettingRequestHandler ?? throw new ArgumentNullException(nameof(getSettingRequestHandler));
            _getAllSettingsRequestHandler = getAllSettingsRequestHandler ?? throw new ArgumentNullException(nameof(getAllSettingsRequestHandler));
            _patchSettingRequestHandler = patchSettingRequestHandler ?? throw new ArgumentNullException(nameof(patchSettingRequestHandler));
            _getSupportEmailRequestHandler = getSupportEmailRequestHandler ?? throw new ArgumentNullException(nameof(getSupportEmailRequestHandler));
        }

        [Authorize()]
        [HttpGet("{settingKey}")]
        public async Task<IActionResult> GetSettingByKeyAsync(SettingKey settingKey)
        {
            if (AISettingKeys.Contains(settingKey) && !User.IsInRole(Models.Roles.AI_SETTINGS_ADMINISTRATOR))
            {
                return Forbid();
            }

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
                var settings = response.Settings;
                if (!User.IsInRole(Models.Roles.AI_SETTINGS_ADMINISTRATOR))
                {
                    settings = settings.Where(setting => !AISettingKeys.Contains(setting.SettingKey)).ToList();
                }

                return Ok(settings);
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }
        }

        [Authorize(Roles = $"{Models.Roles.HYPERLINK_ADMINISTRATOR}, {Models.Roles.GENERAL_SETTINGS_ADMINISTRATOR}, {Models.Roles.AI_SETTINGS_ADMINISTRATOR}")]
        [HttpPatch("{settingKey}")]
        public async Task<IActionResult> PatchSettingAsync(SettingKey settingKey, [FromBody] string settingValue)
        {
            if (AISettingKeys.Contains(settingKey) && !User.IsInRole(Models.Roles.AI_SETTINGS_ADMINISTRATOR))
            {
                return Forbid();
            }

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

        [Authorize()]
        [HttpGet("supportEmail")]
        public async Task<IActionResult> GetSupportEmailAddressAsync()
        {
            var request = new GetSupportEmailRequest();
            var response = await _getSupportEmailRequestHandler.ExecuteAsync(request);
            return Ok(response.SupportEmailAddress);
        }

        [Authorize(Roles = Models.Roles.AI_SETTINGS_ADMINISTRATOR)]
        [HttpGet("aiPrompt/defaults")]
        public IActionResult GetDefaultAIPrompt()
        {
            return Ok(CopilotPrompts.DefaultInstructions);
        }
    }
}
