// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using System.Text.Json;
using WebApi.Models.DTOs;

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
            SettingKey.IsAIRunExplanationEnabled,
            SettingKey.CopilotTemperature,
            SettingKey.CopilotTopP,
            SettingKey.CopilotInstructions,
            SettingKey.CopilotSuggestedPrompts
        };

        private static readonly IReadOnlySet<SettingKey> ReadOnlySettingKeys = new HashSet<SettingKey>
        {
            SettingKey.RunHistoryOpenViewingAndUnifiedTab
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
            if (ReadOnlySettingKeys.Contains(settingKey))
            {
                return BadRequest("Feature flags must be managed through Azure App Configuration.");
            }

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

        private static readonly JsonSerializerOptions AlertBannerJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Maximum allowed length for an alert banner message.
        /// </summary>
        private const int AlertBannerMaxMessageLength = 250;

        [Authorize()]
        [HttpGet("alertBanner")]
        public async Task<IActionResult> GetAlertBannerAsync()
        {
            try
            {
                var response = await _getSettingRequestHandler.ExecuteAsync(new GetSettingRequest(SettingKey.AlertBannerConfig));

                AlertBannerConfigDto config;
                if (response.Model == null || string.IsNullOrWhiteSpace(response.Model.SettingValue))
                {
                    config = AlertBannerConfigDto.Default;
                }
                else
                {
                    config = JsonSerializer.Deserialize<AlertBannerConfigDto>(response.Model.SettingValue, AlertBannerJsonOptions)
                             ?? AlertBannerConfigDto.Default;
                    config.NormalizeDatesToUtc();
                }

                return Ok(config);
            }
            catch (Exception)
            {
                return StatusCode(500);
            }
        }

        [Authorize(Roles = Models.Roles.GENERAL_SETTINGS_ADMINISTRATOR)]
        [HttpPatch("alertBanner")]
        public async Task<IActionResult> PatchAlertBannerAsync([FromBody] AlertBannerConfigDto config)
        {
            if (!User.IsInRole(Models.Roles.GENERAL_SETTINGS_ADMINISTRATOR))
            {
                return Forbid();
            }

            if (config == null)
            {
                return BadRequest("Alert banner configuration is required.");
            }

            config.NormalizeDatesToUtc();

            var validationError = ValidateAlertBannerConfig(config);
            if (validationError != null)
            {
                return BadRequest(validationError);
            }

            try
            {
                var json = JsonSerializer.Serialize(config, AlertBannerJsonOptions);
                await _patchSettingRequestHandler.ExecuteAsync(new PatchSettingRequest(SettingKey.AlertBannerConfig, json));
                return Ok(config);
            }
            catch (Exception)
            {
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Validates an alert banner configuration. Returns an error message when invalid,
        /// or <c>null</c> when the configuration passes all rules.
        /// </summary>
        private static string? ValidateAlertBannerConfig(AlertBannerConfigDto config)
        {
            if (config.IsEnabled && string.IsNullOrWhiteSpace(config.Message))
            {
                return "Message is required when the alert banner is enabled.";
            }

            if (!string.IsNullOrEmpty(config.Message) && config.Message.Length > AlertBannerMaxMessageLength)
            {
                return $"Message must be {AlertBannerMaxMessageLength} characters or fewer.";
            }

            if (config.StartDate >= config.EndDate)
            {
                return "Start date must be earlier than end date.";
            }

            if (!string.IsNullOrWhiteSpace(config.LinkUrl))
            {
                if (!config.LinkUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return "Link URL must start with https://.";
                }

                if (!Uri.TryCreate(config.LinkUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    return "Link URL must be a valid absolute https URI.";
                }
            }

            return null;
        }
    }
}
