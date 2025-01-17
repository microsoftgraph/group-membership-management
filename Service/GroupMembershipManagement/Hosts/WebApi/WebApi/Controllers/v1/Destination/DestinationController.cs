// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using System.Security.Claims;
using AzureADGroup = Models.AzureADGroup;
using NewGroupDTO = WebApi.Models.DTOs.NewGroup;

namespace WebApi.Controllers.v1.Destination
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/destinations")]
    public class DestinationController : ControllerBase
    {
        private readonly IRequestHandler<SearchDestinationsRequest, SearchDestinationsResponse> _searchDestinationsRequestHandler;
        private readonly IRequestHandler<GetGroupEndpointsRequest, GetGroupEndpointsResponse> _getGroupEndpointsRequestHandler;
        private readonly IRequestHandler<GetGroupOnboardingStatusRequest, GetGroupOnboardingStatusResponse> _getGroupOnboardingStatusHandler;
        private readonly IRequestHandler<PostGroupRequest, PostGroupResponse> _postGroupHandler;

        public DestinationController
            (IRequestHandler<SearchDestinationsRequest, SearchDestinationsResponse> searchDestinationsRequestHandler,
            IRequestHandler<GetGroupEndpointsRequest, GetGroupEndpointsResponse> getGroupEndpointsRequestHandler,
            IRequestHandler<GetGroupOnboardingStatusRequest, GetGroupOnboardingStatusResponse> getGroupOnboardingStatusHandler,
            IRequestHandler<PostGroupRequest, PostGroupResponse> postGroupHandler)
        {
            _searchDestinationsRequestHandler = searchDestinationsRequestHandler ?? throw new ArgumentNullException(nameof(searchDestinationsRequestHandler));
            _getGroupEndpointsRequestHandler = getGroupEndpointsRequestHandler ?? throw new ArgumentNullException(nameof(getGroupEndpointsRequestHandler));
            _getGroupOnboardingStatusHandler = getGroupOnboardingStatusHandler ?? throw new ArgumentNullException(nameof(getGroupOnboardingStatusHandler));
            _postGroupHandler = postGroupHandler ?? throw new ArgumentNullException(nameof(postGroupHandler));
        }

        [Authorize()]
        [HttpGet("search/{query}")]
        public async Task<ActionResult<IEnumerable<AzureADGroup>>> SearchAsync(string query)
        {
            var response = await _searchDestinationsRequestHandler.ExecuteAsync(new SearchDestinationsRequest { Query = query });
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
        [HttpGet("groups/{groupId}/onboarding-status")]
        public async Task<ActionResult<GetGroupOnboardingStatusResponse>> GetGroupOnboardingStatusAsync(Guid groupId)
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

                var response = await _getGroupOnboardingStatusHandler.ExecuteAsync(new GetGroupOnboardingStatusRequest (groupId, userId, isJobTenantWriter));
                return Ok(response.Status);
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

