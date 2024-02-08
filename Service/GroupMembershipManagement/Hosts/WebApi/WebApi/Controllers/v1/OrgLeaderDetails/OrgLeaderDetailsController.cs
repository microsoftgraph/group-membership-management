// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace WebApi.Controllers.v1.OrgLeaderDetails
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/orgLeaderDetails")]
    public class OrgLeaderDetailsController : ControllerBase
    {
        private readonly IRequestHandler<GetOrgLeaderDetailsRequest, GetOrgLeaderDetailsResponse> _getOrgLeaderDetailsRequestHandler;
        
        public OrgLeaderDetailsController(IRequestHandler<GetOrgLeaderDetailsRequest, GetOrgLeaderDetailsResponse> getOrgLeaderDetailsRequestHandler)
        {
            _getOrgLeaderDetailsRequestHandler = getOrgLeaderDetailsRequestHandler ?? throw new ArgumentNullException(nameof(getOrgLeaderDetailsRequestHandler));
        }

        [Authorize()]
        [HttpGet("{objectId}")]
        public async Task<ActionResult<int>> GetOrgLeaderDetailsAsync(string objectId)
        {
            var response = await _getOrgLeaderDetailsRequestHandler.ExecuteAsync(new GetOrgLeaderDetailsRequest(objectId));
            return Ok(response);
        }        
    }
}

