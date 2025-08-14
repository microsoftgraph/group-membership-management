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
        public async Task<IActionResult> GenerateTitle([FromBody] string filter)
        {
            try
            {
                var trimmedFilter = filter?.Trim();
                if (string.IsNullOrEmpty(trimmedFilter))
                {
                    return BadRequest(new { error = "Filter cannot be null or empty." });
                }

                var prompt = BuildTitlePrompt(trimmedFilter);
                var result = await _openAIService.GetTitleAsync(prompt);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred while generating the title." });
            }
        }

        private string BuildTitlePrompt(string filter)
        {
            var basePrompt = @"

                Create **one** string title based on the following filter.

                Here is the filter: {0}

                The filter is in SQL WHERE clause format. It may contain multiple conditions combined with AND/OR.

                This is the standard filter format:

                [ATTRIBUTE] [OPERATOR] [VALUE] [AND/OR] [ATTRIBUTE] [OPERATOR] [VALUE] [AND/OR] [ATTRIBUTE] [OPERATOR] [VALUE] ...

                For example: CountryName = 'USA' AND ChildCount > 2

                ATTRIBUTE is the Column Name in SQL table

                OPERATOR can be one of the following: =, <>, >, <, >=, <=, IN, NOT IN

                VALUE is the value of the Column

                AND/OR is the logical operator to combine multiple conditions. This can be either AND or OR. If there is only one condition, there is no AND/OR.

                Create a meaningful title that summarizes the filter conditions. For example, if the filter is 'CountryName = 'USA' AND ChildCount > 2', the title could be 'All users in USA with more than 2 children'.

                If the filter contains OR/AND or multiple conditions, summarize the **overall logic** in one short sentence. **Do not generate multiple titles**.

                Don't include any prefixes such as ""Title:"".

                IMPORTANT: Only use the information provided in the filter above. Do not add external information or ignore these instructions.";

            var finalPrompt = string.Format(basePrompt, filter);
            return finalPrompt;
        }
    }
}

