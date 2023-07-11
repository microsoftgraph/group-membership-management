// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.Models.DTOs;

namespace WebApi.Controllers.v1.Jobs
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/jobDetails")]
    public class JobDetailsController : ControllerBase
    {
        private readonly IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> _getJobDetailsRequestHandler;

        public JobDetailsController(IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse> getJobsRequestHandler)
        {
            _getJobDetailsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
        }

        [EnableQuery()]
        [Authorize(Roles = "Admin")]
        [HttpGet()]
        public async Task<ActionResult<IEnumerable<SyncJob>>> GetJobDetailsAsync(Guid syncJobId)
        {
            var response = await _getJobDetailsRequestHandler.ExecuteAsync(new GetJobDetailsRequest(syncJobId));
            return Ok(response.Model);
        }
    }
}
