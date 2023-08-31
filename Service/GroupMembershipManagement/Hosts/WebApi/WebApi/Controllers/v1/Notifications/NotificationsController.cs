// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.Options;
using Microsoft.O365.ActionableMessages.Utilities;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using WebApi.Models;
using WebApi.Models.Requests;

namespace WebApi.Controllers.v1.Notifications
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly IRequestHandler<NotificationCardRequest, NotificationCardResponse> _notificationCardHandler;
        private readonly IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse> _resolveNotificationHandler;
        private readonly IActionableMessageTokenValidator _actionableMessageTokenValidator;
        private readonly IOptions<WebApiSettings> _webApiSettings;

        public NotificationsController(
            IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse> notificationCardHandler,
            IRequestHandler<NotificationCardRequest, NotificationCardResponse> getNotificationCardRequestHandler,
            IActionableMessageTokenValidator actionableMessageTokenValidator,
            IOptions<WebApiSettings> webApiSettings)
        {
            _resolveNotificationHandler = notificationCardHandler ?? throw new ArgumentNullException(nameof(notificationCardHandler));
            _notificationCardHandler = getNotificationCardRequestHandler ?? throw new ArgumentNullException(nameof(getNotificationCardRequestHandler));
            _actionableMessageTokenValidator = actionableMessageTokenValidator ?? throw new ArgumentNullException(nameof(actionableMessageTokenValidator));
            _webApiSettings = webApiSettings ?? throw new ArgumentNullException(nameof(webApiSettings));
        }

        [EnableQuery()]
        [HttpPost()]
        [Route("{id}/card")]
        public async Task<ActionResult<string>> GetCardAsync(Guid id)
        {
            var request = HttpContext.Request;
            var userUpn = GetUserUpn();

            var response = await _notificationCardHandler.ExecuteAsync(new NotificationCardRequest(id, userUpn));
            Response.Headers["card-update-in-body"] = "true";
            return Content(response.CardJson, "application/json");
        }

        [EnableQuery()]
        [Route("{id}/resolve")]
        [HttpPost()]
        public async Task<ActionResult<string>> ResolveNotificationAsync(Guid id, [FromBody] ResolveNotification model)
        {
            var bearerToken = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            var currentSettings = _webApiSettings.Value;
            ActionableMessageTokenValidationResult result = await _actionableMessageTokenValidator.ValidateTokenAsync(bearerToken, currentSettings.ApiHostname);

            var response = await _resolveNotificationHandler.ExecuteAsync(new ResolveNotificationRequest(id, result.ActionPerformer, model.Resolution));
            Response.Headers["card-update-in-body"] = "true";
            return Content(response.CardJson, "application/json");
        }

        private string? GetUserUpn()
        {
            var identity = HttpContext.User.Identity as ClaimsIdentity;
            return identity?.Claims.Where(x => x.Type == ClaimTypes.Upn).FirstOrDefault()?.Value;
        }
    }
}
