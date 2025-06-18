// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using System.Security.Claims;
using NewGroupDTO = WebApi.Models.DTOs.NewGroup;

namespace WebApi.Controllers.v1.Destination
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/destinations")]
    public class DestinationController : ControllerBase
    {
        private readonly IRequestHandler<SearchGroupsRequest, SearchGroupsResponse> _searchGroupsRequestHandler;
        private readonly IRequestHandler<SearchChannelsRequest, SearchChannelsResponse> _searchChannelsRequestHandler;
        private readonly IRequestHandler<GetGroupEndpointsRequest, GetGroupEndpointsResponse> _getGroupEndpointsRequestHandler;
        private readonly IRequestHandler<GetGroupOwnersRequest, GetGroupOwnersResponse> _getGroupOwnersRequestHandler;
        private readonly IRequestHandler<GetGroupOnboardingStatusRequest, GetOnboardingStatusResponse> _getGroupOnboardingStatusHandler;
        private readonly IRequestHandler<GetChannelOnboardingStatusRequest, GetOnboardingStatusResponse> _getChannelOnboardingStatusHandler;
        private readonly IRequestHandler<PostGroupRequest, PostGroupResponse> _postGroupHandler;
        public DestinationController
            (IRequestHandler<SearchGroupsRequest, SearchGroupsResponse> searchGroupsRequestHandler,
            IRequestHandler<SearchChannelsRequest, SearchChannelsResponse> searchChannelsRequestHandler,
            IRequestHandler<GetGroupEndpointsRequest, GetGroupEndpointsResponse> getGroupEndpointsRequestHandler,
            IRequestHandler<GetGroupOwnersRequest, GetGroupOwnersResponse> getGroupOwnersRequestHandler,
            IRequestHandler<GetGroupOnboardingStatusRequest, GetOnboardingStatusResponse> getGroupOnboardingStatusHandler,
            IRequestHandler<GetChannelOnboardingStatusRequest, GetOnboardingStatusResponse> getChannelOnboardingStatusHandler,
            IRequestHandler<PostGroupRequest, PostGroupResponse> postGroupHandler)
        {
            _searchGroupsRequestHandler = searchGroupsRequestHandler ?? throw new ArgumentNullException(nameof(searchGroupsRequestHandler));
            _searchChannelsRequestHandler = searchChannelsRequestHandler ?? throw new ArgumentNullException(nameof(searchChannelsRequestHandler));
            _getGroupEndpointsRequestHandler = getGroupEndpointsRequestHandler ?? throw new ArgumentNullException(nameof(getGroupEndpointsRequestHandler));
            _getGroupOwnersRequestHandler = getGroupOwnersRequestHandler ?? throw new ArgumentNullException(nameof(getGroupOwnersRequestHandler));
            _getGroupOnboardingStatusHandler = getGroupOnboardingStatusHandler ?? throw new ArgumentNullException(nameof(getGroupOnboardingStatusHandler));
            _getChannelOnboardingStatusHandler = getChannelOnboardingStatusHandler ?? throw new ArgumentNullException(nameof(getChannelOnboardingStatusHandler));
            _postGroupHandler = postGroupHandler ?? throw new ArgumentNullException(nameof(postGroupHandler));
        }

        [Authorize()]
        [HttpGet("searchGroups/{query}")]
        public async Task<ActionResult<IEnumerable<Models.DTOs.Destination>>> SearchGroupsAsync(string query)
        {
            var response = await _searchGroupsRequestHandler.ExecuteAsync(new SearchGroupsRequest { Query = query });
            return Ok(response.Model);
        }

        [Authorize()]
        [HttpGet("teams/{teamId}/searchChannels/{query}")]
        public async Task<ActionResult<IEnumerable<Models.DTOs.Channel>>> SearchChannelsAsync(Guid teamId, string query)
        {
            var response = await _searchChannelsRequestHandler.ExecuteAsync(new SearchChannelsRequest
            {
                TeamId = teamId,
                Query = query
            });
            return Ok(response.Model);
        }

        [Authorize()]
        [HttpGet("groups/{groupId}/endpoints")]
        public async Task<ActionResult<List<string>>> GetGroupEndpointsAsync(Guid groupId)
        {
            var response = await _getGroupEndpointsRequestHandler.ExecuteAsync(new GetGroupEndpointsRequest { GroupId = groupId });
            return Ok(response.Endpoints);
        }

        [Authorize()]
        [HttpGet("groups/{groupId}/owners")]
        public async Task<ActionResult<List<Models.DTOs.GroupOwner>>> GetGroupOwnersAsync(Guid groupId)
        {
            try
            {
                var response = await _getGroupOwnersRequestHandler.ExecuteAsync(new GetGroupOwnersRequest { GroupId = groupId });
                return Ok(response.Owners);
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: {ex.Message}");
            }
        }

        [Authorize()]
        [HttpGet("groups/{groupId}/onboarding-status")]
        public async Task<ActionResult<GetOnboardingStatusResponse>> GetGroupOnboardingStatusAsync(Guid groupId)
        {
            try
            {
                var user = User;
                var claimsIdentity = User.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

                var isJobTenantWriter = User.IsInRole(Models.Roles.JOB_TENANT_WRITER);

                if (string.IsNullOrEmpty(userId))
                {
                    return new ForbidResult();
                }

                var response = await _getGroupOnboardingStatusHandler.ExecuteAsync(new GetGroupOnboardingStatusRequest(groupId, userId, isJobTenantWriter));
                return Ok(response);
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: ${ex}");
            }
        }

        [Authorize()]
        [HttpGet("teams/{teamId}/channel/{channelId}/onboarding-status")]
        public async Task<ActionResult<GetOnboardingStatusResponse>> GetChannelOnboardingStatusAsync(Guid teamId, string channelId)
        {
            try
            {
                var user = User;
                var claimsIdentity = User.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

                var isJobTenantWriter = User.IsInRole(Models.Roles.JOB_TENANT_WRITER);

                if (string.IsNullOrEmpty(userId))
                {
                    return new ForbidResult();
                }

                var response = await _getChannelOnboardingStatusHandler.ExecuteAsync(new GetChannelOnboardingStatusRequest(teamId, channelId, userId, isJobTenantWriter));
                return Ok(response);
            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: ${ex}");
            }
        }

        [Authorize()]
        [HttpPost("groups")]
        public async Task<ActionResult<PostGroupResponse>> CreateGroupAsync([FromBody] NewGroupDTO newGroupDTO)
        {
            try
            {
                var user = User;
                var claimsIdentity = User.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return new ForbidResult();
                }

                var response = await _postGroupHandler.ExecuteAsync(new PostGroupRequest (new Guid(userId), newGroupDTO.GroupName, newGroupDTO.GroupAlias));
                
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return Ok(response);
                }
                else if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    return BadRequest(response);
                }
                else if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return Forbid();
                }
                else
                {
                    return StatusCode((int)response.StatusCode, response);
                }

            }
            catch (Exception ex)
            {
                return Problem(statusCode: (int)System.Net.HttpStatusCode.InternalServerError, detail: $"An error occurred: {ex.Message}");
            }
        }
    }
}

