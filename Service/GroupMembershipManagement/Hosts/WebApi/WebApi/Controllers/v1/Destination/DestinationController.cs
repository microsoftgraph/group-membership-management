// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using AzureADGroup = Models.AzureADGroup;

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

        public DestinationController
            (IRequestHandler<SearchDestinationsRequest, SearchDestinationsResponse> searchDestinationsRequestHandler,
            IRequestHandler<GetGroupEndpointsRequest, GetGroupEndpointsResponse> getGroupEndpointsRequestHandler,
            IRequestHandler<GetGroupOnboardingStatusRequest, GetGroupOnboardingStatusResponse> getGroupOnboardingStatusHandler)
        {
            _searchDestinationsRequestHandler = searchDestinationsRequestHandler ?? throw new ArgumentNullException(nameof(searchDestinationsRequestHandler));
            _getGroupEndpointsRequestHandler = getGroupEndpointsRequestHandler ?? throw new ArgumentNullException(nameof(getGroupEndpointsRequestHandler));
            _getGroupOnboardingStatusHandler = getGroupOnboardingStatusHandler ?? throw new ArgumentNullException(nameof(getGroupOnboardingStatusHandler));
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
            var response = await _getGroupOnboardingStatusHandler.ExecuteAsync(new GetGroupOnboardingStatusRequest { GroupId = groupId });
            return Ok(response.Status);
        }
    }
}

