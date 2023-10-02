// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;
using AzureADGroup = Models.AzureADGroup;

namespace WebApi.Controllers.v1.Group
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Groups")]
    public class GroupInformationController : ControllerBase
    {
        private readonly IRequestHandler<GetGroupInformationRequest, GetGroupInformationResponse> _getGroupRequestHandler;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly string _gmmAppId = "c3ac1017-f7ec-4f0e-a417-80275bf5ee58"; //todo: reference kv secret

        public GroupInformationController(IRequestHandler<GetGroupInformationRequest, GetGroupInformationResponse> getGroupRequestHandler, 
            IGraphGroupRepository graphGroupRepository)
        {
            _getGroupRequestHandler = getGroupRequestHandler ?? throw new ArgumentNullException(nameof(getGroupRequestHandler));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("search/{query}")]
        public async Task<ActionResult<IEnumerable<AzureADGroup>>> SearchAsync(string query)
        {
            int minQueryLength = 3;
            if (string.IsNullOrEmpty(query) || query.Length < minQueryLength)
            {
                return default;
            }

            var response = await _getGroupRequestHandler.ExecuteAsync(new GetGroupInformationRequest { Query = query });
            return Ok(response.Model);
        }


        [Authorize(Roles = "Admin")]
        [HttpGet("isAppIDOwnerOfGroup")]
        public async Task<ActionResult<bool>> IsAppIDOwnerOfGroupAsync(Guid groupId)
        {
            var isAppIdOwner = await _graphGroupRepository.IsAppIDOwnerOfGroup(_gmmAppId, groupId);
            return isAppIdOwner;
        }
    }
}

