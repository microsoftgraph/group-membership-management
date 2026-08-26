// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;

namespace WebApi.Controllers.v1.Feedback
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Feedback")]
    public class FeedbackController : ControllerBase
    {
        private readonly IRequestHandler<RefineFeedbackRequest, RefineFeedbackResponse> _refineFeedbackHandler;

        public FeedbackController(
            IRequestHandler<RefineFeedbackRequest, RefineFeedbackResponse> refineFeedbackHandler)
        {
            _refineFeedbackHandler = refineFeedbackHandler ?? throw new ArgumentNullException(nameof(refineFeedbackHandler));
        }

        // Must match the roles on the rejection submission endpoint
        // (JobDetailsController.ReviewJobAsync), since the Refine action is only surfaced
        // from the rejection dialog shown to submission reviewers.
        [Authorize(Roles = Models.Roles.SUBMISSION_REVIEWER)]
        [HttpPost("refine")]
        [ProducesResponseType(typeof(RefineFeedbackResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status408RequestTimeout)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RefineAsync([FromBody] RefineFeedbackRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Feedback))
            {
                return BadRequest(new { error = "Feedback cannot be null or empty.", code = RefineFeedbackErrorCodes.InvalidRequest });
            }

            var response = await _refineFeedbackHandler.ExecuteAsync(new RefineFeedbackRequest(request.Feedback));

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return Ok(new RefineFeedbackResponseDto { RefinedText = response.RefinedText ?? string.Empty });
            }

            return StatusCode((int)response.StatusCode, new { error = response.ResponseMessage, code = response.ErrorCode });
        }
    }

    #region DTOs

    public class RefineFeedbackRequestDto
    {
        public string Feedback { get; set; } = string.Empty;
    }

    public class RefineFeedbackResponseDto
    {
        public string RefinedText { get; set; } = string.Empty;
    }

    #endregion
}
