// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using WebApi.BackgroundServices;

namespace WebApi.Controllers.v1.OpenAI
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/OpenAI")]
    public class OpenAIController : ControllerBase
    {
        private readonly OpenAIService _openAIService;

        public OpenAIController(OpenAIService openAIService)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
        }

        [Authorize()]
        [HttpPost("generateTitle")]
        public async Task<IActionResult> GenerateTitle([FromBody] string prompt)
        {
            var result = await _openAIService.GetTitleAsync(prompt);
            return Ok(result);
        }
    }
}

