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
        private readonly IRequestHandler<GetOrgLeaderRequest, GetOrgLeaderResponse> _getOrgLeaderRequestHandler;

        public OrgLeaderDetailsController(
            IRequestHandler<GetOrgLeaderDetailsRequest, GetOrgLeaderDetailsResponse> getOrgLeaderDetailsRequestHandler,
             IRequestHandler<GetOrgLeaderRequest, GetOrgLeaderResponse> getOrgLeaderRequestHandler)
        {
            _getOrgLeaderDetailsRequestHandler = getOrgLeaderDetailsRequestHandler ?? throw new ArgumentNullException(nameof(getOrgLeaderDetailsRequestHandler));
            _getOrgLeaderRequestHandler = getOrgLeaderRequestHandler ?? throw new ArgumentNullException(nameof(getOrgLeaderRequestHandler));
        }

        [Authorize()]
        [HttpGet("ObjectId/{objectId}")]
        public async Task<ActionResult<int>> GetOrgLeaderDetailsAsync(string objectId)
        {
            var response = await _getOrgLeaderDetailsRequestHandler.ExecuteAsync(new GetOrgLeaderDetailsRequest(objectId));
            return Ok(response);
        }

        [Authorize()]
        [HttpGet("EmployeeId/{employeeId}")]
        public async Task<ActionResult<int>> GetOrgLeaderAsync(int employeeId)
        {
            var response = await _getOrgLeaderRequestHandler.ExecuteAsync(new GetOrgLeaderRequest(employeeId));
            return Ok(response);
        }
    }
}

