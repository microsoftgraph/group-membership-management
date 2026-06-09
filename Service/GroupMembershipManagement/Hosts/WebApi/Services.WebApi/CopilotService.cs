// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Polly;
using Services.WebApi.Contracts;
using Repositories.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;

namespace Services.WebApi
{

    [ExcludeFromCodeCoverage]
    public class CopilotService : ICopilotService
    {
        private readonly ChatClient _chatClient;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<CopilotService> _logger;

        // Stores the Graph objectIds of validated org leaders (per request), keyed by email
        private readonly ConcurrentDictionary<string, string> _validatedOrgLeaders = new(StringComparer.OrdinalIgnoreCase);

        // Stores Graph displayNames from lookup_person (per request), keyed by email
        private readonly ConcurrentDictionary<string, string> _lookupDisplayNames = new(StringComparer.OrdinalIgnoreCase);

        // Stores Graph objectIds from lookup_person (per request), keyed by email
        private readonly ConcurrentDictionary<string, string> _lookupObjectIds = new(StringComparer.OrdinalIgnoreCase);

        // Request-scoped cache for attribute values to avoid duplicate DB queries within the same conversation turn
        private readonly ConcurrentDictionary<string, string> _attributeValueCache = new(StringComparer.OrdinalIgnoreCase);

        #region Tool Definition
        private static readonly ChatTool GetAttributeValuesTool = ChatTool.CreateFunctionTool(
            functionName: "get_attribute_values",
            functionDescription: "Get valid values for HR attributes from the database. ALWAYS call this before creating any filter to ensure you use correct attribute values. Never guess values.",
            functionParameters: BinaryData.FromString(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""attributes"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""List of attribute names to get values for. Use exact attribute names from the available list.""
                    }
                },
                ""required"": [""attributes""]
            }")
        );

        private static readonly ChatTool LookupPersonTool = ChatTool.CreateFunctionTool(
            functionName: "lookup_person",
            functionDescription: "Search for a person by name or email in Microsoft Graph. ALWAYS call this when the user mentions a specific person's name as org leader (not 'my org' or 'my manager'). Returns matching people with their email so you can confirm the right person.",
            functionParameters: BinaryData.FromString(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""searchQuery"": {
                        ""type"": ""string"",
                        ""description"": ""The person's name or email to search for (e.g., 'John Smith' or 'john.smith@company.com')""
                    }
                },
                ""required"": [""searchQuery""]
            }")
        );

        private static readonly ChatTool ValidateOrgLeaderTool = ChatTool.CreateFunctionTool(
            functionName: "validate_org_leader",
            functionDescription: "Validate that a person exists in the HR database and can be used as an org leader. Call this ONLY after the user has confirmed the specific email of the person they want to use. Takes an email address, looks up the user in the HR database, and returns whether they are valid.",
            functionParameters: BinaryData.FromString(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""email"": {
                        ""type"": ""string"",
                        ""description"": ""The confirmed email address of the person to validate as org leader (e.g., 'john.smith@company.com')""
                    }
                },
                ""required"": [""email""]
            }")
        );

        #endregion

        #region Cache

        // Cache for HR attribute metadata (name, customLabel, description)
        private static List<HrAttributeInfo>? _hrAttributesCache = null;
        private static DateTime _hrAttributesCacheExpiry = DateTime.MinValue;
        private static readonly object _hrAttributesLock = new();
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Represents an HR attribute with its metadata for LLM context.
        /// </summary>
        private class HrAttributeInfo
        {
            public string Name { get; set; } = string.Empty;
            public string FilterName { get; set; } = string.Empty; // Name with _Code suffix if applicable
            public string CustomLabel { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool HasMapping { get; set; }
            public bool Enabled { get; set; } = true;
        }

        #endregion

        public CopilotService(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory, ILogger<CopilotService> logger)
        {
            var endpoint = configuration["Settings:OpenAIEndpoint"];
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new ArgumentNullException(nameof(endpoint), "OpenAI endpoint is not configured.");
            }

            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
            var openAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);
            _chatClient = openAIClient.GetChatClient("gpt-4o");

            _retryPolicy = Policy
                .Handle<Azure.RequestFailedException>(ex =>
                    ex.Status == 429 || ex.Status >= 500)
                .Or<TaskCanceledException>()
                .Or<HttpRequestException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 10)),
                    onRetry: (exception, timespan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception, "Retry {RetryCount} after {Delay}s", retryCount, timespan.TotalSeconds);
                    });
        }

        #region Public Methods

        public async Task<CopilotChatResult> GetChatResponseAsync(
            string userMessage,
            List<CopilotChatMessage> conversationHistory,
            CopilotUserContext? userContext = null,
            string? currentFilter = null)
        {
            var requestOptions = new ChatCompletionOptions()
            {
                Temperature = 0.7f,
                TopP = 0.9f,
                FrequencyPenalty = 0.3f,
                PresencePenalty = 0.0f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            // Add the tool for fetching attribute values
            requestOptions.Tools.Add(GetAttributeValuesTool);
            // Add the tool for looking up people by name/email
            requestOptions.Tools.Add(LookupPersonTool);
            // Add the tool for validating an org leader against HR database
            requestOptions.Tools.Add(ValidateOrgLeaderTool);

            // Fetch HR attributes from database (cached)
            var hrAttributes = await GetHrAttributesAsync();

            // Build system prompt with rich attribute info (name, label, description)
            var attributesText = BuildAttributesText(hrAttributes);
            var systemPrompt = CopilotPrompts.ChatPrompt.Replace("{0}", attributesText);

            // Add user context if available
            if (userContext != null)
            {
                var userContextText = BuildUserContextSection(userContext);
                if (!string.IsNullOrEmpty(userContextText))
                {
                    systemPrompt += userContextText;
                }
            }

            // Add current filter context if available
            if (!string.IsNullOrEmpty(currentFilter))
            {
                systemPrompt += CopilotPrompts.CurrentFilterContextTemplate.Replace("{0}", currentFilter);
            }

            // Build messages
            List<ChatMessage> messages = new List<ChatMessage> { new SystemChatMessage(systemPrompt) };

            // Add conversation history
            if (conversationHistory != null)
            {
                foreach (var msg in conversationHistory)
                {
                    if (msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add(new UserChatMessage(msg.Content));
                    }
                    else if (msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        messages.Add(new AssistantChatMessage(msg.Content));
                    }
                }
            }

            messages.Add(new UserChatMessage(userMessage));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

            try
            {
                // Tool calling loop - keep going until we get a final response
                const int maxToolCalls = 5; // Prevent infinite loops
                int toolCallCount = 0;

                while (toolCallCount < maxToolCalls)
                {
                    var response = await _retryPolicy.ExecuteAsync(async () =>
                    {
                        return await _chatClient.CompleteChatAsync(messages, requestOptions, timeoutCts.Token);
                    });

                    var chatCompletion = response.Value;

                    // Check if LLM wants to call a tool
                    if (chatCompletion.FinishReason == ChatFinishReason.ToolCalls)
                    {
                        toolCallCount++;
                        
                        var toolCallNames = string.Join(", ", chatCompletion.ToolCalls.Select(t => t.FunctionName));
                        _logger.LogDebug("Tool call iteration {Iteration}/{Max}: {Tools}", toolCallCount, maxToolCalls, toolCallNames);

                        // Add assistant message with tool calls to conversation
                        messages.Add(new AssistantChatMessage(chatCompletion));

                        // Process tool calls in parallel for independent tools
                        var toolTasks = chatCompletion.ToolCalls.Select(async toolCall =>
                        {
                            string result = toolCall.FunctionName switch
                            {
                                "get_attribute_values" => await ExecuteGetAttributeValuesToolAsync(toolCall.FunctionArguments.ToString()),
                                "lookup_person" => await ExecuteLookupPersonToolAsync(toolCall.FunctionArguments.ToString()),
                                "validate_org_leader" => await ExecuteValidateOrgLeaderToolAsync(toolCall.FunctionArguments.ToString()),
                                _ => LogAndReturnUnknownTool(toolCall.FunctionName)
                            };
                            return (toolCall.Id, result);
                        });

                        var toolResults = await Task.WhenAll(toolTasks);
                        foreach (var (toolId, toolResult) in toolResults)
                        {
                            messages.Add(new ToolChatMessage(toolId, toolResult));
                        }
                    }
                    else
                    {
                        // Final response - no more tool calls
                        var responseText = chatCompletion.Content[0].Text;

                        // Parse structured JSON response with sourceParts
                        var (parsedResponse, sourceParts) = ParseStructuredResponseWithSourcePart(responseText);

                        // Fix orgLeaderName if it was incorrectly extracted as "Organization Structure"
                        foreach (var sp in sourceParts.Where(p => p.UseOrgStructure))
                        {
                            if (string.IsNullOrEmpty(sp.OrgLeaderName) || sp.OrgLeaderName.Equals("Organization Structure", StringComparison.OrdinalIgnoreCase))
                            {
                                var nameInText = Regex.Match(responseText, @"\*\*([^*]+)\*\*\s*\([^)]+@[^)]+\)", RegexOptions.IgnoreCase);
                                if (nameInText.Success && !nameInText.Groups[1].Value.Equals("Organization Structure", StringComparison.OrdinalIgnoreCase))
                                {
                                    sp.OrgLeaderName = nameInText.Groups[1].Value;
                                }
                                else if (!string.IsNullOrEmpty(userContext?.ManagerName))
                                {
                                    sp.OrgLeaderName = userContext.ManagerName;
                                }
                            }

                            if (string.IsNullOrEmpty(sp.OrgLeaderEmail))
                            {
                                var emailInText = Regex.Match(responseText, @"\*\*[^*]+\*\*\s*\([^,]+,\s*([^\s)]+@[^\s)]+)\)", RegexOptions.IgnoreCase);
                                if (emailInText.Success)
                                {
                                    sp.OrgLeaderEmail = emailInText.Groups[1].Value;
                                }
                                else if (!string.IsNullOrEmpty(userContext?.ManagerEmail))
                                {
                                    sp.OrgLeaderEmail = userContext.ManagerEmail;
                                }
                            }
                        }

                        _logger.LogDebug("Chat completed with {ToolCalls} tool calls, {SourceParts} source parts", toolCallCount, sourceParts.Count);

                        return new CopilotChatResult
                        {
                            ResponseMessage = parsedResponse,
                            SourceParts = sourceParts
                        };
                    }
                }

                // Too many tool calls - return error
                throw new InvalidOperationException($"Exceeded maximum tool calls ({maxToolCalls})");
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("OpenAI API call timed out after 120 seconds");
            }
        }

        #endregion

        #region Tool Execution

        private string LogAndReturnUnknownTool(string functionName)
        {
            _logger.LogWarning("Unknown tool requested: {FunctionName}", functionName);
            return $"Error: Unknown tool '{functionName}'";
        }

        private async Task<string> ExecuteGetAttributeValuesToolAsync(string argumentsJson)
        {
            try
            {
                var args = JsonSerializer.Deserialize<GetAttributeValuesArgs>(argumentsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (args?.Attributes == null || args.Attributes.Count == 0)
                {
                    return JsonSerializer.Serialize(new { error = "No attributes specified" });
                }

                var result = await FetchAttributeValuesAsync(args.Attributes);
                return result;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to fetch attribute values: {ex.Message}" });
            }
        }

        private class GetAttributeValuesArgs
        {
            public List<string>? Attributes { get; set; }
        }

        private async Task<string> ExecuteLookupPersonToolAsync(string argumentsJson)
        {
            try
            {
                var args = JsonSerializer.Deserialize<LookupPersonArgs>(argumentsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (string.IsNullOrWhiteSpace(args?.SearchQuery))
                {
                    return JsonSerializer.Serialize(new { error = "No search query specified" });
                }

                var searchQuery = args.SearchQuery.Trim();
                var searchSafe = searchQuery.Replace("\"", "").Replace("'", "''");

                using var scope = _serviceScopeFactory.CreateScope();
                var graphClient = scope.ServiceProvider.GetRequiredService<GraphServiceClient>();

                var selectFields = new[] { "displayName", "mail", "id", "userPrincipalName", "userType", "accountEnabled" };

                // Exact match by displayName
                List<Microsoft.Graph.Models.User> allUsers = new();
                try
                {
                    var exactResponse = await graphClient.Users.GetAsync(config =>
                    {
                        config.Headers.Add("ConsistencyLevel", "eventual");
                        config.QueryParameters.Filter = $"displayName eq '{searchSafe}'";
                        config.QueryParameters.Select = selectFields;
                        config.QueryParameters.Count = true;
                        config.QueryParameters.Top = 10;
                    });
                    allUsers = exactResponse?.Value ?? new();
                }
                catch
                {
                    // $filter eq can fail for some special characters
                }

                // Filter in code: only real enabled member accounts with reasonable display names
                var realUsers = allUsers
                    .Where(u => (u.UserType ?? "").Equals("Member", StringComparison.OrdinalIgnoreCase)
                                && u.AccountEnabled == true
                                && (u.DisplayName?.Length ?? 0) < 60
                                && !(u.DisplayName?.Contains("Stamp", StringComparison.OrdinalIgnoreCase) == true)
                                && !(u.DisplayName?.Contains("Rebalancing", StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();

                if (realUsers.Count == 0)
                {
                    return JsonSerializer.Serialize(new { 
                        message = $"No users found matching '{searchQuery}'. Could you provide their email address instead?",
                        users = Array.Empty<object>()
                    });
                }

                // Cache objectIds and displayNames from lookup results to avoid redundant Graph calls in validate_org_leader
                foreach (var u in realUsers)
                {
                    var userEmail = u.UserPrincipalName ?? u.Mail;
                    if (!string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(u.Id))
                    {
                        _lookupObjectIds.TryAdd(userEmail, u.Id);
                    }
                    if (!string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(u.DisplayName))
                    {
                        _lookupDisplayNames.TryAdd(userEmail, u.DisplayName);
                    }
                }

                var users = realUsers.Select(u => new
                {
                    displayName = u.DisplayName ?? "Unknown",
                    email = u.UserPrincipalName ?? u.Mail ?? "No email"
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    message = users.Count == 1 
                        ? $"Found 1 person matching '{searchQuery}'" 
                        : $"Found {users.Count} people matching '{searchQuery}'",
                    users
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Failed to search users: {ex.Message}", detail = ex.InnerException?.Message ?? "" });
            }
        }

        private class LookupPersonArgs
        {
            public string? SearchQuery { get; set; }
        }

        #endregion

        #region Validate Org Leader Tool

        private async Task<string> ExecuteValidateOrgLeaderToolAsync(string argumentsJson)
        {
            try
            {
                var args = JsonSerializer.Deserialize<ValidateOrgLeaderArgs>(argumentsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var email = args?.Email?.Trim();

                if (string.IsNullOrEmpty(email))
                    return JsonSerializer.Serialize(new { valid = false, error = "No email provided" });

                // Step 1: Look up Graph objectId by email (use cached value from lookup_person if available)
                string? objectId = _lookupObjectIds.TryGetValue(email, out var cachedId) ? cachedId : null;
                objectId ??= await GetGraphObjectIdByEmailAsync(email);
                if (objectId == null)
                {
                    return JsonSerializer.Serialize(new { valid = false, error = $"Could not find user '{email}' in Microsoft Entra ID" });
                }

                // Step 2: Validate in HR database (also retrieves maxDepth)
                var (exists, employeeId, maxDepth) = await ValidateUserInHrDbAsync(objectId);

                if (exists)
                {
                    // Cache the objectId so we can include it in the final response
                    _validatedOrgLeaders[email] = objectId;
                    // Use displayName from lookup_person cache; only fetch from Graph if not cached
                    string? displayName = _lookupDisplayNames.TryGetValue(email, out var cachedName) ? cachedName : null;
                    if (displayName == null)
                    {
                        try
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var graphClient = scope.ServiceProvider.GetRequiredService<GraphServiceClient>();
                            var user = await graphClient.Users[objectId].GetAsync(config =>
                            {
                                config.QueryParameters.Select = new[] { "displayName" };
                            });
                            displayName = user?.DisplayName;
                        }
                        catch { /* non-critical */ }
                    }

                    return JsonSerializer.Serialize(new
                    {
                        valid = true,
                        displayName = displayName ?? email,
                        email,
                        objectId,
                        employeeId,
                        maxDepth
                    });
                }
                else
                {
                    return JsonSerializer.Serialize(new
                    {
                        valid = false,
                        error = $"User '{email}' was found in Entra ID but does NOT exist in the HR database. They cannot be used as an org leader."
                    });
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { valid = false, error = $"Validation failed: {ex.Message}" });
            }
        }

        private class ValidateOrgLeaderArgs
        {
            public string? Email { get; set; }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Validates whether a user exists in the HR database by their Graph objectId.
        /// Returns (exists, employeeId) — exists is true if employeeId > 0.
        /// Uses a lightweight query (no recursive depth calculation).
        /// </summary>
        private async Task<(bool exists, int employeeId, int maxDepth)> ValidateUserInHrDbAsync(string azureObjectId)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var sqlRepo = scope.ServiceProvider.GetService<ISqlMembershipRepository>();
                var adfRepo = scope.ServiceProvider.GetService<IDataFactoryRepository>();

                if (sqlRepo == null || adfRepo == null)
                {
                    _logger.LogWarning("HR DB services (ISqlMembershipRepository/IDataFactoryRepository) unavailable");
                    return (false, 0, 0);
                }

                var adfRunId = await adfRepo.GetMostRecentSucceededRunIdAsync();
                var tableName = adfRunId?.Replace("-", "") ?? "";
                
                if (string.IsNullOrEmpty(tableName))
                    return (false, 0, 0);

                // Use GetOrgLeaderDetailsAsync to get both employeeId and maxDepth in one call
                var (maxDepth, employeeId) = await sqlRepo.GetOrgLeaderDetailsAsync(azureObjectId, tableName);
                return (employeeId > 0, employeeId, maxDepth);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HR validation failed");
                return (false, 0, 0);
            }
        }

        /// <summary>
        /// Looks up a user's Graph objectId by their email or UPN using the Graph SDK.
        /// Uses $search by userPrincipalName first, then mail as fallback.
        /// Returns null if not found.
        /// </summary>
        private async Task<string?> GetGraphObjectIdByEmailAsync(string email)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var graphClient = scope.ServiceProvider.GetRequiredService<GraphServiceClient>();
            var sanitizedEmail = email.Replace("\"", "");

            // Search by userPrincipalName first (always populated, most reliable)
            try
            {
                var searchResult = await graphClient.Users.GetAsync(config =>
                {
                    config.Headers.Add("ConsistencyLevel", "eventual");
                    config.QueryParameters.Search = $"\"userPrincipalName:{sanitizedEmail}\"";
                    config.QueryParameters.Select = new[] { "id", "userPrincipalName" };
                    config.QueryParameters.Count = true;
                    config.QueryParameters.Top = 5;
                });

                var matchedUser = searchResult?.Value?.FirstOrDefault(u =>
                    string.Equals(u.UserPrincipalName, sanitizedEmail, StringComparison.OrdinalIgnoreCase));
                if (matchedUser?.Id != null)
                    return matchedUser.Id;

                var firstUser = searchResult?.Value?.FirstOrDefault();
                if (firstUser?.Id != null)
                    return firstUser.Id;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Graph UPN search failed for {Email}", email);
            }

            // Fallback: search by mail property (for cases where input is a mail address, not UPN)
            try
            {
                var searchResult = await graphClient.Users.GetAsync(config =>
                {
                    config.Headers.Add("ConsistencyLevel", "eventual");
                    config.QueryParameters.Search = $"\"mail:{sanitizedEmail}\"";
                    config.QueryParameters.Select = new[] { "id" };
                    config.QueryParameters.Count = true;
                    config.QueryParameters.Top = 1;
                });

                var matchedUser = searchResult?.Value?.FirstOrDefault();
                if (matchedUser?.Id != null)
                    return matchedUser.Id;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Graph mail search failed for {Email}", email);
            }

            return null;
        }

        private (string response, List<CopilotSourcePartResult> sourceParts) ParseStructuredResponseWithSourcePart(string responseText)
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var trimmed = responseText.Trim();

            // Try to extract JSON from markdown code blocks
            var jsonBlockMatch = Regex.Match(trimmed, @"```json\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (jsonBlockMatch.Success)
            {
                trimmed = jsonBlockMatch.Groups[1].Value.Trim();
            }

            if (trimmed.StartsWith("{"))
            {
                try
                {
                    var structured = JsonSerializer.Deserialize<StructuredChatResponseWithSourceParts>(trimmed, jsonOptions);
                    if (structured != null && !string.IsNullOrEmpty(structured.Response))
                    {
                        var sourceParts = new List<CopilotSourcePartResult>();

                        if (structured.SourceParts != null && structured.SourceParts.Count > 0)
                        {
                            // New format: sourceParts array with per-part org leader info
                            foreach (var sp in structured.SourceParts)
                            {
                                if (!string.IsNullOrEmpty(sp.Filter) || sp.UseOrgStructure)
                                {
                                    var objectId = !string.IsNullOrEmpty(sp.OrgLeaderEmail) && _validatedOrgLeaders.TryGetValue(sp.OrgLeaderEmail, out var oid) ? oid : null;
                                    sourceParts.Add(new CopilotSourcePartResult
                                    {
                                        PartId = Guid.NewGuid().ToString(),
                                        Filter = sp.Filter ?? "",
                                        Title = sp.Title ?? "HR Filter",
                                        IsExclusion = sp.IsExclusion,
                                        UseOrgStructure = sp.UseOrgStructure,
                                        OrgLeaderName = sp.OrgLeaderName,
                                        OrgLeaderEmail = sp.OrgLeaderEmail,
                                        OrgLeaderObjectId = objectId,
                                        OrgLeaderDepth = sp.OrgLeaderDepth
                                    });
                                }
                            }
                        }
                        else if (structured.SourcePart != null)
                        {
                            // Legacy format: single sourcePart with top-level org leader fields
                            if (!string.IsNullOrEmpty(structured.SourcePart.Filter) || structured.UseOrgStructure)
                            {
                                var objectId = !string.IsNullOrEmpty(structured.OrgLeaderEmail) && _validatedOrgLeaders.TryGetValue(structured.OrgLeaderEmail, out var oid) ? oid : _validatedOrgLeaders.Values.LastOrDefault();
                                sourceParts.Add(new CopilotSourcePartResult
                                {
                                    PartId = Guid.NewGuid().ToString(),
                                    Filter = structured.SourcePart.Filter ?? "",
                                    Title = structured.SourcePart.Title ?? "HR Filter",
                                    IsExclusion = structured.SourcePart.IsExclusion,
                                    UseOrgStructure = structured.UseOrgStructure,
                                    OrgLeaderName = structured.OrgLeaderName,
                                    OrgLeaderEmail = structured.OrgLeaderEmail,
                                    OrgLeaderObjectId = objectId,
                                    OrgLeaderDepth = structured.OrgLeaderDepth
                                });
                            }
                        }
                        else if (structured.UseOrgStructure)
                        {
                            // Fallback: AI set useOrgStructure:true but forgot sourceParts, create default
                            string? extractedFilter = null;
                            var filterMatch = Regex.Match(structured.Response, @"`([^`]+_Code[^`]*)`");
                            if (filterMatch.Success)
                            {
                                extractedFilter = filterMatch.Groups[1].Value;
                            }
                            var objectId = !string.IsNullOrEmpty(structured.OrgLeaderEmail) && _validatedOrgLeaders.TryGetValue(structured.OrgLeaderEmail, out var oid) ? oid : _validatedOrgLeaders.Values.LastOrDefault();
                            sourceParts.Add(new CopilotSourcePartResult
                            {
                                PartId = Guid.NewGuid().ToString(),
                                Filter = extractedFilter ?? "",
                                Title = structured.OrgLeaderName != null ? $"{structured.OrgLeaderName}'s Org" : "Org Filter",
                                IsExclusion = false,
                                UseOrgStructure = true,
                                OrgLeaderName = structured.OrgLeaderName,
                                OrgLeaderEmail = structured.OrgLeaderEmail,
                                OrgLeaderObjectId = objectId,
                                OrgLeaderDepth = structured.OrgLeaderDepth
                            });
                        }
                        return (structured.Response, sourceParts);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "JSON parse error in LLM response");
                }
            }

            // Fallback: AI returned plain text instead of JSON
            var hasOrgStructure = responseText.Contains("Organization Structure", StringComparison.OrdinalIgnoreCase);
            var hasAcceptApply = responseText.Contains("Accept & Apply", StringComparison.OrdinalIgnoreCase) || responseText.Contains("Accept &amp; Apply", StringComparison.OrdinalIgnoreCase);
            
            if (hasOrgStructure && hasAcceptApply)
            {
                string? extractedFilter = null;
                var filterMatch = Regex.Match(responseText, @"`([^`]*_Code[^`]*)`");
                if (filterMatch.Success)
                {
                    extractedFilter = filterMatch.Groups[1].Value;
                }

                string? extractedLeaderName = null;
                var leaderMatch = Regex.Match(responseText, @"\*\*([^*]+)\*\*\s*\([^)]*\)\s*as the org leader", RegexOptions.IgnoreCase);
                if (!leaderMatch.Success)
                {
                    leaderMatch = Regex.Match(responseText, @"with\s+\*\*([^*]+)\*\*.*?as the org leader", RegexOptions.IgnoreCase);
                }
                if (leaderMatch.Success)
                {
                    extractedLeaderName = leaderMatch.Groups[1].Value;
                }

                _logger.LogInformation("Plain-text fallback: extracted filter={Filter}, leader={Leader}", extractedFilter, extractedLeaderName);

                var fallbackPart = new CopilotSourcePartResult
                {
                    PartId = Guid.NewGuid().ToString(),
                    Filter = extractedFilter ?? "",
                    Title = extractedLeaderName != null ? $"{extractedLeaderName}'s Org" : "Org Filter",
                    IsExclusion = false,
                    UseOrgStructure = true,
                    OrgLeaderName = extractedLeaderName
                };
                return (responseText, new List<CopilotSourcePartResult> { fallbackPart });
            }

            return (responseText, new List<CopilotSourcePartResult>());
        }

        // New format: sourceParts array with per-part org leader info
        private class StructuredChatResponseWithSourceParts
        {
            public string Response { get; set; } = string.Empty;
            public List<SourcePartWithLeaderJson>? SourceParts { get; set; }
            // Legacy support: single sourcePart with top-level org leader fields
            public SourcePartJson? SourcePart { get; set; }
            public bool UseOrgStructure { get; set; }
            public string? OrgLeaderName { get; set; }
            public string? OrgLeaderEmail { get; set; }
            public int? OrgLeaderDepth { get; set; }
        }

        private class SourcePartWithLeaderJson
        {
            public string? Filter { get; set; }
            public string? Title { get; set; }
            public bool IsExclusion { get; set; }
            public bool UseOrgStructure { get; set; }
            public string? OrgLeaderName { get; set; }
            public string? OrgLeaderEmail { get; set; }
            public int? OrgLeaderDepth { get; set; }
        }

        private class SourcePartJson
        {
            public string? Filter { get; set; }
            public string? Title { get; set; }
            public bool IsExclusion { get; set; }
        }

        private string BuildAttributesText(List<HrAttributeInfo>? hrAttributes)
        {
            if (hrAttributes == null || hrAttributes.Count == 0)
                return "No HR attributes available";

            // Build attribute list: FilterName (Label) - Description
            var lines = new List<string>();
            foreach (var attr in hrAttributes.Where(a => a.Enabled))
            {
                var line = $"- {attr.FilterName}";
                
                if (!string.IsNullOrWhiteSpace(attr.CustomLabel) && attr.CustomLabel.Trim() != attr.Name)
                    line += $" ({attr.CustomLabel.Trim()})";
                
                if (!string.IsNullOrWhiteSpace(attr.Description))
                    line += $" - {attr.Description.Trim()}";
                
                lines.Add(line);
            }
            
            return string.Join("\n", lines);
        }

        private async Task<List<HrAttributeInfo>> GetHrAttributesAsync()
        {
            // Check cache
            lock (_hrAttributesLock)
            {
                if (_hrAttributesCache != null && DateTime.UtcNow < _hrAttributesCacheExpiry)
                {
                    return _hrAttributesCache;
                }
            }

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IDatabaseSqlMembershipSourcesRepository>();
                var storedAttributes = await repository.GetDefaultSourceAttributesAsync();

                var attributes = new List<HrAttributeInfo>();
                if (storedAttributes != null)
                {
                    foreach (var attr in storedAttributes.Where(a => a.Enabled))
                    {
                        attributes.Add(new HrAttributeInfo
                        {
                            Name = attr.Name,
                            FilterName = attr.HasMapping ? $"{attr.Name}_Code" : attr.Name,
                            CustomLabel = attr.CustomLabel ?? string.Empty,
                            Description = attr.Description ?? string.Empty,
                            HasMapping = attr.HasMapping,
                            Enabled = attr.Enabled
                        });
                    }
                }

                // Update cache
                lock (_hrAttributesLock)
                {
                    _hrAttributesCache = attributes;
                    _hrAttributesCacheExpiry = DateTime.UtcNow.Add(_cacheDuration);
                }

                return attributes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch HR attributes");
                // Return empty list on error, don't crash
                return new List<HrAttributeInfo>();
            }
        }

        private string BuildUserContextSection(CopilotUserContext userContext)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(userContext.ManagerName))
            {
                var managerInfo = userContext.ManagerName;
                if (!string.IsNullOrEmpty(userContext.ManagerEmail))
                    managerInfo += $" ({userContext.ManagerEmail})";

                parts.Add($"- User's Manager: {managerInfo}");
            }

            if (parts.Count == 0) return string.Empty;

            return CopilotPrompts.UserContextTemplate.Replace("{0}", string.Join("\n", parts));
        }

        private async Task<string> FetchAttributeValuesAsync(List<string> attributeNames)
        {
            // Check request-scoped cache: build a cache key from sorted attribute names
            var cacheKey = string.Join(",", attributeNames.OrderBy(a => a, StringComparer.OrdinalIgnoreCase));
            if (_attributeValueCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var sqlMembershipRepository = scope.ServiceProvider.GetService<ISqlMembershipRepository>();
            var dataFactoryRepository = scope.ServiceProvider.GetService<IDataFactoryRepository>();

            if (sqlMembershipRepository == null || dataFactoryRepository == null)
            {
                return JsonSerializer.Serialize(new { error = "Database services unavailable", attributes = attributeNames });
            }

            string tableName;
            try
            {
                var adfRunId = await dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                tableName = adfRunId?.Replace("-", "") ?? "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get ADF run ID");
                return JsonSerializer.Serialize(new { 
                    note = "Unable to fetch exact attribute values from database (permission issue). Ask the user what values they'd like to use and note that the values should be verified after applying.",
                    attributes = attributeNames 
                });
            }

            var fetchTasks = attributeNames.Select(async attrName =>
            {
                var baseAttrName = attrName.EndsWith("_Code") ? attrName.Substring(0, attrName.Length - 5) : attrName;

                try
                {
                    var mappings = await sqlMembershipRepository.GetAttributeMappingsAsync(baseAttrName, tableName);
                    if (mappings != null && mappings.Count > 0)
                    {
                        return (attrName, mappings, (string?)null);
                    }
                    return (attrName, new List<(string Code, string Description)>(), "No predefined values");
                }
                catch (Exception ex)
                {
                    return (attrName, new List<(string Code, string Description)>(), ex.Message);
                }
            });

            var results = await Task.WhenAll(fetchTasks);

            // Build structured JSON response for the tool
            var attributeValues = new Dictionary<string, object>();
            foreach (var (attrName, mappings, error) in results)
            {
                if (!string.IsNullOrEmpty(error) && error != "No predefined values")
                {
                    attributeValues[attrName] = new { error };
                }
                else if (mappings.Count == 0)
                {
                    attributeValues[attrName] = new { values = Array.Empty<object>(), note = "No predefined values - use appropriate numeric or string values" };
                }
                else if (mappings.Count > 50)
                {
                    // Truncate detailed list to avoid token overflow, but include ALL codes (they're short)
                    // so the LLM can find the user's requested value without asking to "search further"
                    var truncated = mappings.Take(50).Select(m => new { code = m.Code, description = m.Description }).ToList();
                    var allCodes = mappings.Select(m => m.Code).ToList();
                    attributeValues[attrName] = new { 
                        values = truncated, 
                        totalCount = mappings.Count,
                        note = $"Detailed descriptions shown for first 50 of {mappings.Count} values. ALL valid codes are listed in 'allCodes' — use them directly in filters. Do NOT ask the user to 'search further' or say a value is missing.",
                        allCodes
                    };
                }
                else
                {
                    attributeValues[attrName] = new { values = mappings.Select(m => new { code = m.Code, description = m.Description }).ToList() };
                }
            }

            var result = JsonSerializer.Serialize(attributeValues, new JsonSerializerOptions { WriteIndented = false });

            // Store in request-scoped cache for reuse within this conversation
            _attributeValueCache.TryAdd(cacheKey, result);

            return result;
        }

        #endregion
    }
}