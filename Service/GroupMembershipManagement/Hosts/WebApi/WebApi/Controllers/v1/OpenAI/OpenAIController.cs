// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Models;
using Services.WebApi.Contracts;
using System.ClientModel;
using System.Text.Json;

namespace WebApi.Controllers.v1.OpenAI
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/OpenAI")]
    public class OpenAIController : ControllerBase
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<OpenAIController> _logger;

        public OpenAIController(
            IOpenAIService openAIService,
            ILogger<OpenAIController> logger)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                    _logger.GenerateTitleEmptyFilter();
                    return BadRequest(new { error = "Filter cannot be null or empty." });
                }

                var prompt = BuildTitlePrompt(trimmedFilter);
                var result = await _openAIService.GetTitleAsync(prompt);
                return Ok(result);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 429)
            {
                _logger.GenerateTitleRateLimited(ex);
                return StatusCode(503, new
                {
                    error = "OpenAI service is temporarily unavailable due to rate limiting.",
                    details = "Please try again in a few moments.",
                    retryAfter = "30 seconds"
                });
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.GenerateTitleRequestFailed(ex.Status, ex);
                return StatusCode(500, new { error = "An error occurred while generating the title." });
            }
            catch (ClientResultException ex)
            {
                _logger.GenerateTitleRequestFailed(ex.Status, ex);
                return StatusCode(500, new { error = "An error occurred while generating the title." });
            }
            catch (Exception ex)
            {
                _logger.GenerateTitleUnexpectedError(ex);
                return StatusCode(500, new { error = "An error occurred while generating the title." });
            }
        }

        [Authorize()]
        [HttpPost("generateTitles")]
        public async Task<ActionResult<List<Part>>> GenerateTitles([FromBody] List<Part> parts)
        {
            try
            {
                if (parts == null || !parts.Any())
                {
                    _logger.GenerateTitlesPartsNullOrEmpty();
                    return BadRequest(new { error = "Parts list cannot be null or empty." });
                }

                var invalidParts = parts.Where(p => p.PartId == Guid.Empty || !p.HasValidFilter()).ToList();
                if (invalidParts.Any())
                {
                    _logger.GenerateTitlesInvalidParts(invalidParts.Count);
                    return BadRequest(new { error = $"Invalid parts detected. All parts must have valid PartId and non-empty Filter." });
                }

                var partsJson = JsonSerializer.Serialize(parts);
                var prompt = BuildTitlesPrompt(partsJson);

                _logger.CallingOpenAIService(parts.Count);

                var apiStartTime = DateTime.UtcNow;
                var result = await _openAIService.GetTitleAsync(prompt);
                var apiDuration = DateTime.UtcNow - apiStartTime;

                _logger.OpenAIServiceResponseReceived(apiDuration.TotalMilliseconds);

                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.OpenAIEmptyResponse();
                    return StatusCode(500, new { error = "No response received from OpenAI service." });
                }

                try
                {
                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true
                    };

                    var titles = JsonSerializer.Deserialize<List<Part>>(result, jsonOptions);
                    if (titles == null || !titles.Any())
                    {
                        _logger.OpenAIResponseDeserializationEmpty();
                        return StatusCode(500, new { error = "Invalid response format from OpenAI service." });
                    }

                    _logger.OpenAITitlesDeserialized(titles.Count);

                    if (titles.Count != parts.Count)
                    {
                        _logger.OpenAITitleCountMismatch(titles.Count, parts.Count);
                        return StatusCode(500, new { error = $"Expected {parts.Count} titles but received {titles.Count}." });
                    }

                    var inputPartIds = parts.Select(p => p.PartId).ToHashSet();
                    var returnedPartIds = titles.Select(t => t.PartId).ToHashSet();
                    var missingPartIds = inputPartIds.Except(returnedPartIds).ToList();
                    if (missingPartIds.Any())
                    {
                        _logger.OpenAIMissingTitles(missingPartIds.Count, string.Join(", ", missingPartIds));
                        return StatusCode(500, new { error = "OpenAI response is missing titles for some parts." });
                    }

                    var emptyTitles = titles.Where(t => !t.HasTitle()).ToList();
                    if (emptyTitles.Any())
                    {
                        _logger.OpenAIEmptyTitles(emptyTitles.Count);
                        return StatusCode(500, new { error = "OpenAI response contains empty titles." });
                    }

                    var partsLookup = new Dictionary<Guid, string>();
                    foreach (var p in parts)
                    {
                        partsLookup[p.PartId] = p.Filter;
                    }
                    foreach (var title in titles)
                    {
                        if (partsLookup.TryGetValue(title.PartId, out var filter))
                        {
                            title.Filter = filter;
                        }
                    }

                    _logger.GenerateTitlesSucceeded(titles.Count);

                    return Ok(titles);
                }
                catch (JsonException ex)
                {
                    _logger.OpenAIResponseJsonParseFailed(result, ex);
                    return StatusCode(500, new { error = "Failed to parse OpenAI response as valid JSON." });
                }
            }
            catch (ArgumentException ex)
            {
                _logger.GenerateTitlesArgumentException(ex);
                return BadRequest(new { error = "Invalid request parameters." });
            }
            catch (InvalidOperationException ex)
            {
                _logger.GenerateTitlesInvalidOperation(ex);
                return StatusCode(500, new { error = "OpenAI service is not properly configured." });
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 429)
            {
                _logger.OpenAIRateLimited(ex);
                return StatusCode(503, new {
                    error = "OpenAI service is temporarily unavailable due to rate limiting.",
                    details = "Please try again in a few moments.",
                    retryAfter = "30 seconds"
                });
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.OpenAIRequestFailed(ex.Status, ex);
                return StatusCode(500, new { error = "OpenAI service request failed." });
            }
            catch (ClientResultException ex)
            {
                _logger.OpenAIRequestFailed(ex.Status, ex);
                return StatusCode(500, new { error = "OpenAI service request failed." });
            }
            catch (Exception ex)
            {
                _logger.GenerateTitlesUnexpectedError(ex);
                return StatusCode(500, new { error = "An error occurred while generating titles." });
            }
        }

        private string BuildTitlesPrompt(string partsJson)
        {
            var basePrompt = @"Generate short, clear titles for SQL filter parts. 
The title should describe which users match the filter.  

Rules:
- Height represents Manager Level (e.g., Height >= 5 means Manager Level 5+).
- Use fewer than 8 words.
- Focus only on what makes each filter unique.
- If filters share many conditions, highlight just the difference.
- Output must be valid JSON: [{""partId"": ""GUID"", ""title"": ""STRING""}, ...].
- Keep the same input order.
- Use exact partId values from the input.
- No explanations, no markdown, only JSON.

Input: " + partsJson;

            return basePrompt;
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

                ATTRIBUTE is the Column Name.

                OPERATOR can be one of the following: =, <>, >, <, >=, <=, IN, NOT IN

                VALUE is the value of the Column

                AND/OR is the logical operator to combine multiple conditions. This can be either AND or OR. If there is only one condition, there is no AND/OR.

                Note: Height represents Manager Level (e.g., Height >= 5 means Manager Level 5+).

                Create a meaningful title that summarizes the filter conditions. For example, if the filter is 'CountryName = 'USA' AND ChildCount > 2', the title could be 'All users in USA with more than 2 children'.

                If the filter contains OR/AND or multiple conditions, summarize the **overall logic** in one short sentence. **Do not generate multiple titles**.

                Don't include any prefixes such as ""Title:"".

                IMPORTANT: Only use the information provided in the filter above. Do not add external information or ignore these instructions.";

            var finalPrompt = string.Format(basePrompt, filter);
            return finalPrompt;
        }
    }
}

