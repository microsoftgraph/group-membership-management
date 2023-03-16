// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Security.Claims;
using WebApi.Models.Requests;

namespace WebApi.Controllers.v1.Notifications
{
    [ApiController]
    [Authorize]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly IRequestHandler<NotificationCardRequest, NotificationCardResponse> _notificationCardHandler;
        private readonly IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse> _resolveNotificationHandler;

        public NotificationsController(
            IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse> notificationCardHandler,
            IRequestHandler<NotificationCardRequest, NotificationCardResponse> getNotificationCardRequestHandler)
        {
            _resolveNotificationHandler = notificationCardHandler ?? throw new ArgumentNullException(nameof(notificationCardHandler));
            _notificationCardHandler = getNotificationCardRequestHandler ?? throw new ArgumentNullException(nameof(getNotificationCardRequestHandler));
        }

        [EnableQuery()]
        [HttpPost()]
        [Route("{id}/card")]
        public async Task<ActionResult<string>> GetCardAsync(Guid id)
        {
            var userUpn = User.FindFirstValue(ClaimTypes.Upn);
            var response = await _notificationCardHandler.ExecuteAsync(new NotificationCardRequest(id, userUpn));
            Response.Headers["card-update-in-body"] = "true";
            return this.Content(response.CardJson, "application/json");
        }

        [Route("{id}/resolve")]
        [HttpPost()]
        public async Task<ActionResult<string>> ResolveNotificationAsync(Guid id, [FromBody] ResolveNotification model)
        {
            var upn = User.FindFirstValue(ClaimTypes.Upn);
            var response = await _resolveNotificationHandler.ExecuteAsync(new ResolveNotificationRequest(id, upn, model.Resolution));
            Response.Headers["card-update-in-body"] = "true";
            return this.Content(response.CardJson, "application/json");
        }

    }
}
