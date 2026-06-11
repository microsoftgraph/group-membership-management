// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using System.Text.Json;
using Repositories.Contracts;
using Services.WebApi.Contracts;

namespace WebApi.Controllers.v1.OpenAI
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/OpenAI")]
    public class OpenAIController : ControllerBase
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILoggingRepository _loggingRepository;

        public OpenAIController(
            IOpenAIService openAIService,
            ILoggingRepository loggingRepository)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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

        [Authorize()]
        [HttpPost("generateTitles")]
        public async Task<ActionResult<List<Part>>> GenerateTitles([FromBody] List<Part> parts)
        {
            try
            {
                if (parts == null || !parts.Any())
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = "GenerateTitles request failed: Parts list cannot be null or empty."
                    });
                    return BadRequest(new { error = "Parts list cannot be null or empty." });
                }

                var invalidParts = parts.Where(p => p.PartId == Guid.Empty || !p.HasValidFilter()).ToList();
                if (invalidParts.Any())
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"GenerateTitles request failed: {invalidParts.Count} parts have invalid data."
                    });
                    return BadRequest(new { error = $"Invalid parts detected. All parts must have valid PartId and non-empty Filter." });
                }

                var partsJson = JsonSerializer.Serialize(parts);
                var prompt = BuildTitlesPrompt(partsJson);

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Calling OpenAI service for {parts.Count} parts"
                });

                var apiStartTime = DateTime.UtcNow;
                var result = await _openAIService.GetTitleAsync(prompt);
                var apiDuration = DateTime.UtcNow - apiStartTime;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"OpenAI service returned response. Duration: {apiDuration.TotalMilliseconds} ms"
                });

                if (string.IsNullOrWhiteSpace(result))
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = "OpenAI service returned empty response."
                    });
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
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = "OpenAI response could not be deserialized or was empty."
                        });
                        return StatusCode(500, new { error = "Invalid response format from OpenAI service." });
                    }

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Successfully deserialized {titles.Count} titles from OpenAI response."
                    });

                    if (titles.Count != parts.Count)
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"OpenAI returned {titles.Count} titles but expected {parts.Count}."
                        });
                        return StatusCode(500, new { error = $"Expected {parts.Count} titles but received {titles.Count}." });
                    }

                    var inputPartIds = parts.Select(p => p.PartId).ToHashSet();
                    var returnedPartIds = titles.Select(t => t.PartId).ToHashSet();
                    var missingPartIds = inputPartIds.Except(returnedPartIds).ToList();
                    if (missingPartIds.Any())
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"OpenAI response missing titles for {missingPartIds.Count} parts: {string.Join(", ", missingPartIds)}."
                        });
                        return StatusCode(500, new { error = "OpenAI response is missing titles for some parts." });
                    }

                    var emptyTitles = titles.Where(t => !t.HasTitle()).ToList();
                    if (emptyTitles.Any())
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"OpenAI response contains {emptyTitles.Count} empty titles."
                        });
                        return StatusCode(500, new { error = "OpenAI response contains empty titles." });
                    }

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"GenerateTitles request completed successfully for {titles.Count} parts."
                    });

                    return Ok(titles);
                }
                catch (JsonException ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Failed to parse OpenAI response as JSON: {ex.Message}. Full response: {result}"
                    });
                    return StatusCode(500, new { error = "Failed to parse OpenAI response as valid JSON.", details = ex.Message });
                }
            }
            catch (ArgumentException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"GenerateTitles request failed with ArgumentException: {ex.Message}"
                });
                return BadRequest(new { error = "Invalid request parameters.", details = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"GenerateTitles request failed with InvalidOperationException: {ex.Message}"
                });
                return StatusCode(500, new { error = "OpenAI service is not properly configured.", details = ex.Message });
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 429)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"GenerateTitles request failed due to OpenAI rate limiting (HTTP 429): {ex.Message}. This indicates high API usage - retry logic should handle this automatically."
                });
                return StatusCode(503, new {
                    error = "OpenAI service is temporarily unavailable due to rate limiting.",
                    details = "Please try again in a few moments.",
                    retryAfter = "30 seconds"
                });
            }
            catch (Azure.RequestFailedException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"GenerateTitles request failed with Azure RequestFailedException (Status: {ex.Status}): {ex.Message}"
                });
                return StatusCode(500, new { error = "OpenAI service request failed.", details = ex.Message });
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"GenerateTitles request failed with unexpected error: {ex.Message}. Stack trace: {ex.StackTrace}"
                });
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

