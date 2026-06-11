// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.Requests;

namespace WebApi.Controllers.v1.Notifications
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/notifications")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly IRequestHandler<NotificationCardRequest, NotificationCardResponse> _notificationCardHandler;
        private readonly IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse> _resolveNotificationHandler;

        public NotificationsController(
            IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse> resolveNotificationHandler,
            IRequestHandler<NotificationCardRequest, NotificationCardResponse> notificationCardHandler)
        {
            _resolveNotificationHandler = resolveNotificationHandler ?? throw new ArgumentNullException(nameof(resolveNotificationHandler));
            _notificationCardHandler = notificationCardHandler ?? throw new ArgumentNullException(nameof(notificationCardHandler));
        }

        [HttpPost()]
        [Route("{id}/card")]
        public async Task<ActionResult<string>> GetCardAsync(Guid id)
        {
            var userIdentification = GetUserEmailOrObjectId(); 
            var response = await _notificationCardHandler.ExecuteAsync(new NotificationCardRequest(id, userIdentification));
            Response.Headers["card-update-in-body"] = "true";
            return Content(response.CardJson, "application/json");
        }

        [Route("{id}/resolve")]
        [HttpPost()]
        public async Task<ActionResult<string>> ResolveNotificationAsync(Guid id, [FromBody] ResolveNotification model)
        {
            var userIdentification = GetUserEmailOrObjectId();
            var response = await _resolveNotificationHandler.ExecuteAsync(new ResolveNotificationRequest(id, userIdentification, model.Resolution));
            Response.Headers["card-update-in-body"] = "true";
            return Content(response.CardJson, "application/json");
        }

        private string GetUserEmailOrObjectId()
        {
            // Extract user identification from AAD token claims (already validated by [Authorize] attribute)
            // Priority: upn (user principal name) -> unique_name -> email -> oid (object id)
            var upn = User.Claims.FirstOrDefault(c => c.Type == "upn")?.Value;
            var uniqueName = User.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value;
            var email = User.Claims.FirstOrDefault(c => c.Type == "preferred_username" || c.Type == "email")?.Value;
            var objectId = User.Claims.FirstOrDefault(c => c.Type == "oid" || c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            
            return upn ?? uniqueName ?? email ?? objectId ?? throw new UnauthorizedAccessException("Unable to identify user from token claims");
        }
    }
}
