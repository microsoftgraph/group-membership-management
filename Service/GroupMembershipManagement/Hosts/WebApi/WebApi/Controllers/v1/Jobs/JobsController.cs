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
    [Route("api/v{version:apiVersion}/jobs")]
    public class JobsController : ControllerBase
    {
        private readonly IRequestHandler<GetJobsRequest, GetJobsResponse> _getJobsRequestHandler;

        public JobsController(IRequestHandler<GetJobsRequest, GetJobsResponse> getJobsRequestHandler)
        {
            _getJobsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
        }

        [Authorize()]
        [HttpGet()]
        public async Task<ActionResult<IEnumerable<SyncJob>>> GetJobsAsync(ODataQueryOptions<SyncJobModel> queryOptions)
        {
            var response = await _getJobsRequestHandler.ExecuteAsync(new GetJobsRequest { QueryOptions = queryOptions });
            Response.Headers.Add("x-total-pages", response.TotalNumberOfPages.ToString());
            Response.Headers.Add("x-current-page", response.CurrentPage.ToString());
            return Ok(response.Model);
        }
    }
}

