// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;
using SyncJobModel = Models.SyncJob;

namespace WebApi.Controllers.v1.Jobs
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Groups")]
    public class GroupController : ControllerBase
    {
        private readonly IRequestHandler<GetGroupRequest, GetGroupResponse> _getGroupRequestHandler;

        public GroupController(IRequestHandler<GetGroupRequest, GetGroupResponse> getGroupRequestHandler)
        {
            _getGroupRequestHandler = getGroupRequestHandler ?? throw new ArgumentNullException(nameof(getGroupRequestHandler));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("search/{query}")]
        public async Task<ActionResult<IEnumerable<SyncJob>>> SearchAsync(string query)
        {
            int minQueryLength = 3;
            if (string.IsNullOrEmpty(query) || query.Length < minQueryLength)
            {
                return default;
            }

            var response = await _getGroupRequestHandler.ExecuteAsync(new GetGroupRequest { Query = query });
            return Ok(response.Model);
        }

    }
}

