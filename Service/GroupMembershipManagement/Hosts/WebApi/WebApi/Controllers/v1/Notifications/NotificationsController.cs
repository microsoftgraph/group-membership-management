// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        private readonly IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse> _resolveNotificationHandler;

        public NotificationsController(
            IRequestHandler<ResolveNotificationRequest, 
            ResolveNotificationResponse> resolveNotificationHandler)
        {
            _resolveNotificationHandler = resolveNotificationHandler ?? throw new ArgumentNullException(nameof(resolveNotificationHandler));
        }

        [Route("{id}/resolve")]
        [HttpPost()]
        public async Task<ActionResult<string>> ResolveNotificationAsync(Guid id, [FromBody]ResolveNotification model)
        {
            var upn = User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn");
            var response = await _resolveNotificationHandler.ExecuteAsync(new ResolveNotificationRequest(id, upn, model.Resolution));
            Response.Headers["card-update-in-body"] = "true";
            return this.Content(response.CardJson, "application/json");
        }

    }
}
