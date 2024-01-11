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
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;
using System.Net;

namespace WebApi.Controllers.v1.Jobs
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/jobs")]
    public class JobsController : ControllerBase
    {
        private readonly IRequestHandler<GetJobsRequest, GetJobsResponse> _getJobsRequestHandler;
        private readonly IRequestHandler<PostJobRequest, PostJobResponse> _postJobRequestHandler;

        public JobsController(
            IRequestHandler<GetJobsRequest, GetJobsResponse> getJobsRequestHandler,
            IRequestHandler<PostJobRequest, PostJobResponse> postJobRequestHandler)
        {
            _getJobsRequestHandler = getJobsRequestHandler ?? throw new ArgumentNullException(nameof(getJobsRequestHandler));
            _postJobRequestHandler = postJobRequestHandler ?? throw new ArgumentNullException(nameof(postJobRequestHandler));
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

        [Authorize()]
        [HttpPost()]
        public async Task<ActionResult> PostJobAsync([FromBody] NewSyncJobDTO newSyncJob)
        {
            var user = User;
            var userName = user.Identity?.Name!;
            var response = await _postJobRequestHandler.ExecuteAsync(new PostJobRequest(userName, newSyncJob));

            switch (response.StatusCode)
            {
                case HttpStatusCode.Created:
                    return new CreatedResult($"api/jobs/{response.NewSyncJobId}", response);
                case HttpStatusCode.BadRequest:
                    return new BadRequestObjectResult(response);
                case HttpStatusCode.Forbidden:
                    return new ForbidResult();
                case HttpStatusCode.InternalServerError:
                    return new ObjectResult(response) { StatusCode = (int)HttpStatusCode.InternalServerError };
                default:
                    return new BadRequestResult();
            }
        }
    }
}

