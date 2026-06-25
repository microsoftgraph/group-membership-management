// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System;
using System.Net;
using System.Threading.Tasks;

namespace WebApi.Controllers.v1.Jobs
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/spotCheck")]
    public class SpotCheckController : ControllerBase
    {
        private readonly IRequestHandler<SpotCheckUserMembershipRequest, SpotCheckUserMembershipResponse> _spotCheckUserMembershipHandler;

        public SpotCheckController(
            IRequestHandler<SpotCheckUserMembershipRequest, SpotCheckUserMembershipResponse> spotCheckUserMembershipHandler)
        {
            _spotCheckUserMembershipHandler = spotCheckUserMembershipHandler ?? throw new ArgumentNullException(nameof(spotCheckUserMembershipHandler));
        }

        /// <summary>
        /// For a reviewer, checks whether a given user is included by each (supported) source part of a sync job.
        /// </summary>
        [Authorize(Roles = Models.Roles.SUBMISSION_REVIEWER)]
        [HttpGet("job/{syncJobId}/user/{userId}")]
        public async Task<IActionResult> SpotCheckUserAsync(Guid syncJobId, string userId)
        {
            var response = await _spotCheckUserMembershipHandler.ExecuteAsync(new SpotCheckUserMembershipRequest(syncJobId, userId));

            return response.StatusCode switch
            {
                HttpStatusCode.OK => Ok(response.Model),
                HttpStatusCode.BadRequest => BadRequest(),
                HttpStatusCode.NotFound => NotFound(),
                HttpStatusCode.Forbidden => Forbid(),
                _ => Problem(statusCode: (int)HttpStatusCode.InternalServerError)
            };
        }
    }
}
