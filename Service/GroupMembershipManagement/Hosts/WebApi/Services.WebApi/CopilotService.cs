// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using OpenAI.Chat;
using Hosts.WebApi;
using Services.WebApi.AI;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Models;
using Polly;
using Services.WebApi.Contracts;
using Repositories.Contracts;
using Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using Models.AdfRunHistory;

namespace Services.WebApi
{

    [ExcludeFromCodeCoverage]
    public class CopilotService : ICopilotService
    {
        private readonly ChatClient _chatClient;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<CopilotService> _logger;
        private readonly IDatabaseSettingsRepository _settingsRepository;

        // Request-scoped cache for attribute values to avoid duplicate DB queries within the same conversation turn
        private readonly ConcurrentDictionary<string, string> _attributeValueCache = new(StringComparer.OrdinalIgnoreCase);

        private const int CopilotAttributeValueLimit = 25;

        #region Tool Definition
        private static readonly ChatTool GetAttributeValuesTool = ChatTool.CreateFunctionTool(
            functionName: "get_attribute_values",
            functionDescription: "Get valid values for HR attributes from the database. ALWAYS call this before creating any filter to ensure you use correct attribute values. Never guess values. If the user names a specific value, pass it as searchTerm so the database performs the match.",
            functionParameters: BinaryData.FromString(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""attributes"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" },
                        ""description"": ""List of attribute names to get values for. Use exact attribute names from the available list.""
                    },
                    ""searchTerm"": {
                        ""type"": ""string"",
                        ""description"": ""Optional case-insensitive substring to match against Code or Description.""
                    }
                },
                ""required"": [""attributes""]
            }")
        );

        private static readonly ChatTool LookupPersonTool = ChatTool.CreateFunctionTool(
            functionName: "lookup_person",
            functionDescription: "Search for a person by name, alias, or email in Microsoft Graph. Set isOrgLeader true ONLY when resolving the leader of a requested organization, team, reports, reporting chain, hierarchy, or rollup; set it false for an individual inclusion or exclusion. Always resolve a named org leader (not 'my org' or 'my manager'). For individuals, follow additional context on using explicit aliases/emails directly; use this tool for display names that need resolution.",
            functionParameters: BinaryData.FromString(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""searchQuery"": {
                        ""type"": ""string"",
                        ""description"": ""The person's name, alias, or email to search for (e.g., 'John Smith', 'jsmith', or 'john.smith@company.com')""
                    },
                    ""isOrgLeader"": {
                        ""type"": ""boolean"",
                        ""description"": ""True for a collective hierarchy request; false when resolving only an individual, including in a mixed org-and-individual request. Defaults to true if omitted.""
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

        private static readonly ChatTool SearchGroupTool = ChatTool.CreateFunctionTool(
            functionName: "search_group",
            functionDescription: "Search for Entra ID groups by name or email. Use this when the user wants to include members of a specific Entra ID group as a source. Returns matching groups with their name, email, and objectId.",
            functionParameters: BinaryData.FromString(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""searchQuery"": {
                        ""type"": ""string"",
                        ""description"": ""The group name or email prefix to search for (e.g., 'Engineering Team' or 'eng-team')""
                    }
                },
                ""required"": [""searchQuery""]
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

        #region AI Settings Cache

        // Cache for AI settings from DB (IsAICopilotEnabled, Temperature, TopP)
        private static Dictionary<SettingKey, string>? _aiSettingsCache = null;
        private static DateTime _aiSettingsCacheExpiry = DateTime.MinValue;
        private static readonly object _aiSettingsLock = new();
        private static readonly TimeSpan _aiSettingsCacheDuration = TimeSpan.FromMinutes(5);

        // Cache for AI prompt from settings DB
        private static string? _aiPromptCache = null;
        private static DateTime _aiPromptCacheExpiry = DateTime.MinValue;
        private static readonly object _aiPromptLock = new();
        private static readonly TimeSpan _aiPromptCacheDuration = TimeSpan.FromMinutes(5);

        #endregion

        public CopilotService(
            IChatClientFactory chatClientFactory,
            IServiceScopeFactory serviceScopeFactory,
            IDatabaseSettingsRepository settingsRepository,
            ILogger<CopilotService> logger)
        {
            ArgumentNullException.ThrowIfNull(chatClientFactory);
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _chatClient = chatClientFactory.CreateChatClient();

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
                        _logger.CopilotChatRetryAttempt(retryCount, timespan.TotalSeconds, exception);
                    });
        }

        #region Public Methods

        public async Task<CopilotChatResult> GetChatResponseAsync(
            string userMessage,
            List<CopilotChatMessage> conversationHistory,
            CopilotUserContext? userContext = null,
            List<CopilotSourcePartResult>? workingQuery = null,
            string? conversationId = null)
        {
            // Check kill switch
            var aiSettings = await GetCachedAISettingsAsync();
            if (aiSettings.TryGetValue(SettingKey.IsAICopilotEnabled, out var copilotEnabledStr)
                && copilotEnabledStr.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return new CopilotChatResult
                {
                    ResponseMessage = "The AI Copilot is currently disabled by your administrator.",
                    SourceParts = new List<CopilotSourcePartResult>()
                };
            }

            var confirmationGate = new OrgLeaderConfirmationGate(conversationId, userContext?.ObjectId, userMessage);

            // Load dynamic temperature and topP
            // Defaults tuned for deterministic structured-JSON output (exact attribute names/casing,
            // no invented values). Low temperature minimizes hallucination; top_p left at 1.0 so
            // temperature is the single sampling lever. Overridable via admin AI settings.
            var temperature = 0.2f;
            var topP = 1.0f;
            if (aiSettings.TryGetValue(SettingKey.CopilotTemperature, out var tempStr) && float.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedTemp))
                temperature = Math.Clamp(parsedTemp, 0.0f, 1.0f);
            if (aiSettings.TryGetValue(SettingKey.CopilotTopP, out var topPStr) && float.TryParse(topPStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedTopP))
                topP = Math.Clamp(parsedTopP, 0.0f, 1.0f);

            var requestOptions = new ChatCompletionOptions()
            {
                Temperature = temperature,
                TopP = topP,
                FrequencyPenalty = 0.3f,
                PresencePenalty = 0.0f,
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "copilot_operations",
                    jsonSchema: BinaryData.FromString(CopilotPrompts.OperationResponseJsonSchema),
                    jsonSchemaFormatDescription: "A natural-language message plus an ordered set of membership-query edit operations (set/add/remove/replace).",
                    jsonSchemaIsStrict: true)
            };

            // Add the tool for fetching attribute values
            requestOptions.Tools.Add(GetAttributeValuesTool);
            // Add the tool for looking up people by name/email
            requestOptions.Tools.Add(LookupPersonTool);
            // Add the tool for validating an org leader against HR database
            requestOptions.Tools.Add(ValidateOrgLeaderTool);
            // Add the tool for searching Entra ID groups
            requestOptions.Tools.Add(SearchGroupTool);

            // Fetch HR attributes from database (cached)
            var hrAttributes = await GetHrAttributesAsync();
            var attributesText = BuildAttributesText(hrAttributes);

            // Build system prompt: non-editable prefix (with attributes injected) + editable behavior
            var editableInstructions = await GetCachedPromptAsync();
            var prefixWithAttributes = CopilotPrompts.NonEditablePrefix.Replace("{0}", attributesText);
            var systemPrompt = prefixWithAttributes + "\n\n" + editableInstructions;

            // Add user context if available
            if (userContext != null)
            {
                await EnrichUserContextAsync(userContext);
                if (userContext.IsInHrDatabase && !string.IsNullOrEmpty(userContext.Email) &&
                    !string.IsNullOrEmpty(userContext.ObjectId))
                {
                    confirmationGate.Know(new OrgLeaderConfirmationGate.Candidate(
                        userContext.DisplayName ?? userContext.Email, userContext.Email, userContext.ObjectId));
                }
                if (userContext.ManagerIsInHrDatabase && !string.IsNullOrEmpty(userContext.ManagerEmail) &&
                    !string.IsNullOrEmpty(userContext.ManagerObjectId))
                {
                    confirmationGate.Know(new OrgLeaderConfirmationGate.Candidate(
                        userContext.ManagerName ?? userContext.ManagerEmail,
                        userContext.ManagerEmail, userContext.ManagerObjectId));
                }
                var userContextText = BuildUserContextSection(userContext);
                if (!string.IsNullOrEmpty(userContextText))
                {
                    systemPrompt += userContextText;
                }
            }

            if (workingQuery != null)
            {
                foreach (var part in workingQuery.Where(part => part.UseOrgStructure &&
                             !string.IsNullOrEmpty(part.OrgLeaderEmail) &&
                             !string.IsNullOrEmpty(part.OrgLeaderObjectId)))
                {
                    confirmationGate.TrustExistingRoot(new OrgLeaderConfirmationGate.Candidate(
                        part.OrgLeaderName ?? part.OrgLeaderEmail!,
                        part.OrgLeaderEmail!,
                        part.OrgLeaderObjectId!));
                }
            }

            // Add the full working query context (v2) or legacy current-filter context (v1).
            var workingQueryContext = BuildWorkingQueryContext(workingQuery);
            if (!string.IsNullOrEmpty(workingQueryContext))
            {
                systemPrompt += workingQueryContext;
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

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

            int toolCallCount = 0;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;
            int totalTokens = 0;
            var usageObserved = false;
            var invokedTools = new List<string>();

            try
            {
                // Tool calling loop - keep going until we get a final response
                const int maxToolCalls = 8; // Allow complex multi-part queries (org leader + groups + attributes)

                // Allow up to 'maxToolCalls' tool-call iterations, plus one final LLM call to produce the response
                while (toolCallCount <= maxToolCalls)
                {
                    var response = await _retryPolicy.ExecuteAsync(async () =>
                    {
                        return await _chatClient.CompleteChatAsync(messages, requestOptions, timeoutCts.Token);
                    });

                    var chatCompletion = response.Value;
                    if (chatCompletion.Usage is { } usage)
                    {
                        usageObserved = true;
                        totalInputTokens += usage.InputTokenCount;
                        totalOutputTokens += usage.OutputTokenCount;
                        totalTokens += usage.TotalTokenCount;
                    }

                    // Check if LLM wants to call a tool
                    if (chatCompletion.FinishReason == ChatFinishReason.ToolCalls)
                    {
                        toolCallCount++;
                        
                        var toolCallNames = string.Join(", ", chatCompletion.ToolCalls.Select(t => t.FunctionName));
                        _logger.CopilotToolCallIteration(toolCallCount, maxToolCalls, toolCallNames);

                        // Add assistant message with tool calls to conversation
                        messages.Add(new AssistantChatMessage(chatCompletion));

                        // Process tool calls in parallel for independent tools
                        var toolTasks = chatCompletion.ToolCalls.Select(async toolCall =>
                        {
                            var executed = true;
                            string result;
                            if (toolCall.FunctionName == "validate_org_leader" &&
                                !TryAuthorizeValidation(toolCall.FunctionArguments.ToString(), confirmationGate))
                            {
                                executed = false;
                                result = JsonSerializer.Serialize(new
                                {
                                    valid = false,
                                    error = "User confirmation is required in a separate turn before validating this org leader."
                                });
                            }
                            else if (toolCall.FunctionName == "lookup_person" &&
                                     !TryAuthorizeLookup(toolCall.FunctionArguments.ToString(), confirmationGate))
                            {
                                executed = false;
                                result = JsonSerializer.Serialize(new
                                {
                                    error = "Only search for a person whose name, alias, or email appears in the current user message or was previously authorized by the user in this conversation."
                                });
                            }
                            else
                            {
                                result = toolCall.FunctionName switch
                                {
                                    "get_attribute_values" => await ExecuteGetAttributeValuesToolAsync(toolCall.FunctionArguments.ToString()),
                                    "lookup_person" => await ExecuteLookupPersonToolAsync(toolCall.FunctionArguments.ToString(), confirmationGate),
                                    "validate_org_leader" => await ExecuteValidateOrgLeaderToolAsync(toolCall.FunctionArguments.ToString(), confirmationGate),
                                    "search_group" => await ExecuteSearchGroupToolAsync(toolCall.FunctionArguments.ToString()),
                                    _ => LogAndReturnUnknownTool(toolCall.FunctionName)
                                };
                            }
                            return (toolCall.Id, result, toolCall.FunctionName, executed);
                        });

                        var toolResults = await Task.WhenAll(toolTasks);
                        foreach (var (toolId, toolResult, toolName, executed) in toolResults)
                        {
                            messages.Add(new ToolChatMessage(toolId, toolResult));
                            if (executed)
                                invokedTools.Add(toolName);
                        }
                    }
                    else
                    {
                        // Final response - no more tool calls
                        var responseText = chatCompletion.Content[0].Text;

                        // Parse the strict-schema operation set { message, operations[] }.
                        var (message, operations) = ParseOperationResponse(responseText);

                        var (applyResult, confirmationMessage) = ApplyOperationsWithConfirmation(
                            workingQuery, operations, confirmationGate, message);
                        if (confirmationMessage != null)
                        {
                            _logger.CopilotOrgLeaderConfirmationRequired(conversationId ?? "unknown");
                        }

                        _logger.CopilotOperationsApplied(
                            applyResult.AddCount,
                            applyResult.RemoveCount,
                            applyResult.ReplaceCount,
                            applyResult.RejectedTargetCount,
                            applyResult.PreservedUnsupportedCount,
                            conversationId ?? "unknown");

                        _logger.CopilotChatLoopCompleted(toolCallCount, applyResult.ResultingQuery.Count);
                        _logger.CopilotChatTokenUsage(totalInputTokens, totalOutputTokens, toolCallCount + 1, conversationId ?? "unknown");

                        return new CopilotChatResult
                        {
                            ResponseMessage = confirmationMessage ?? message,
                            // v1 backward-compat: SourceParts carries the resulting query (for an empty
                            // working query this equals the newly generated parts — the old append result).
                            SourceParts = applyResult.ResultingQuery,
                            ResultingQuery = applyResult.ResultingQuery,
                            AppliedOperations = applyResult.AppliedOperations,
                            Warning = applyResult.Warning,
                            ErrorCode = applyResult.ErrorCode,
                            InvokedTools = invokedTools,
                            EffectiveTemperature = requestOptions.Temperature,
                            EffectiveTopP = requestOptions.TopP,
                            Usage = usageObserved
                                ? new CopilotChatUsage
                                {
                                    InputTokens = totalInputTokens,
                                    OutputTokens = totalOutputTokens,
                                    TotalTokens = totalTokens,
                                    ModelCallCount = toolCallCount + 1
                                }
                                : null
                        };
                    }
                }

                // Too many tool calls - log consumed tokens before failing
                _logger.CopilotChatTokenUsage(totalInputTokens, totalOutputTokens, toolCallCount, conversationId ?? "unknown");
                throw new InvalidOperationException($"Exceeded maximum tool call iterations ({maxToolCalls})");
            }
            catch (OperationCanceledException)
            {
                _logger.CopilotChatTokenUsage(totalInputTokens, totalOutputTokens, toolCallCount, conversationId ?? "unknown");
                throw new TimeoutException("OpenAI API call timed out after 180 seconds");
            }
        }

        #endregion

        #region Tool Execution

        private string LogAndReturnUnknownTool(string functionName)
        {
            _logger.CopilotUnknownToolRequested(functionName);
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

                var result = await FetchAttributeValuesAsync(args.Attributes, args.SearchTerm);
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
            public string? SearchTerm { get; set; }
        }

        private async Task<string> ExecuteLookupPersonToolAsync(
            string argumentsJson,
            OrgLeaderConfirmationGate confirmationGate)
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

                // Single call: exact match by displayName, alias (UPN local-part), mail, or UPN.
                // mailNickname is not reliably the user's real alias (it can drift from the UPN
                // local-part), so alias-only input (e.g. "jsmith") is matched via userPrincipalName
                // startswith "jsmith@" instead of mailNickname eq "jsmith".
                List<Microsoft.Graph.Models.User> allUsers = new();

                try
                {
                    var exactResponse = await graphClient.Users.GetAsync(config =>
                    {
                        config.Headers.Add("ConsistencyLevel", "eventual");
                        config.QueryParameters.Filter = $"displayName eq '{searchSafe}' or userPrincipalName eq '{searchSafe}' or startswith(userPrincipalName, '{searchSafe}@') or mail eq '{searchSafe}'";
                        config.QueryParameters.Select = selectFields;
                        config.QueryParameters.Count = true;
                        config.QueryParameters.Top = 10;
                    });
                    allUsers = exactResponse?.Value ?? new();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Graph user search failed for '{SearchQuery}'", searchQuery);
                }

                // Fallback: broad $search across multiple fields if exact match found nothing (mirrors Entra ID portal user lookup)
                if (allUsers.Count == 0)
                {
                    try
                    {
                        var searchResponse = await graphClient.Users.GetAsync(config =>
                        {
                            config.Headers.Add("ConsistencyLevel", "eventual");
                            config.QueryParameters.Search = $"\"displayName:{searchSafe}\" OR \"mail:{searchSafe}\" OR \"userPrincipalName:{searchSafe}\" OR \"givenName:{searchSafe}\" OR \"surName:{searchSafe}\"";
                            config.QueryParameters.Select = selectFields;
                            config.QueryParameters.Count = true;
                            config.QueryParameters.Top = 10;
                        });
                        allUsers = searchResponse?.Value ?? new();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Graph $search failed for '{SearchQuery}'", searchQuery);
                    }
                }

                // Filter in code: only real enabled member accounts with reasonable display names
                var realUsers = allUsers
                    .Where(u => (u.UserType ?? "").Equals("Member", StringComparison.OrdinalIgnoreCase)
                                && u.AccountEnabled == true
                                && (u.DisplayName?.Length ?? 0) < 60
                                && !(u.DisplayName?.Contains("Stamp", StringComparison.OrdinalIgnoreCase) == true)
                                && !(u.DisplayName?.Contains("Rebalancing", StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();

                confirmationGate.Found(realUsers
                    .Where(user => !string.IsNullOrWhiteSpace(user.Id))
                    .Select(user => new OrgLeaderConfirmationGate.Candidate(
                        user.DisplayName ?? "Unknown",
                        user.UserPrincipalName ?? user.Mail ?? string.Empty,
                        user.Id!))
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Email)),
                    args.IsOrgLeader);

                if (realUsers.Count == 0)
                {
                    return JsonSerializer.Serialize(new { 
                        message = $"No users found matching '{searchQuery}'. Could you provide their email address instead?",
                        users = Array.Empty<object>()
                    });
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
            public bool IsOrgLeader { get; set; } = true;
        }

        #endregion

        #region Validate Org Leader Tool

        private async Task<string> ExecuteValidateOrgLeaderToolAsync(
            string argumentsJson,
            OrgLeaderConfirmationGate confirmationGate)
        {
            try
            {
                var args = JsonSerializer.Deserialize<ValidateOrgLeaderArgs>(argumentsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var email = args?.Email?.Trim();

                if (string.IsNullOrEmpty(email))
                    return JsonSerializer.Serialize(new { valid = false, error = "No email provided" });

                if (!confirmationGate.TryGetConfirmed(email, out var confirmed) || confirmed == null)
                    return JsonSerializer.Serialize(new { valid = false, error = "User confirmation is required in a separate turn." });

                // Validate the identity the user actually confirmed, not an objectId cached on the service instance.
                var objectId = confirmed.ObjectId;
                if (string.IsNullOrWhiteSpace(objectId))
                {
                    return JsonSerializer.Serialize(new { valid = false, error = $"Could not find user '{email}' in Microsoft Entra ID" });
                }

                // Step 2: Validate in HR database (also retrieves maxDepth)
                var (exists, employeeId, maxDepth) = await ValidateUserInHrDbAsync(objectId);

                if (exists)
                {
                    confirmationGate.MarkValidated(confirmed);
                    var displayName = confirmed.Name;

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

        #region Search Group Tool

        private async Task<string> ExecuteSearchGroupToolAsync(string argumentsJson)
        {
            var searchQuery = string.Empty;
            try
            {
                var args = JsonSerializer.Deserialize<SearchGroupArgs>(argumentsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (string.IsNullOrWhiteSpace(args?.SearchQuery))
                {
                    return JsonSerializer.Serialize(new { error = "No search query specified" });
                }

                searchQuery = args.SearchQuery.Trim();
                var searchSafe = searchQuery.Replace("'", "''");

                using var scope = _serviceScopeFactory.CreateScope();
                var graphGroupRepository = scope.ServiceProvider.GetRequiredService<IGraphGroupRepository>();

                // Search by exact id (filter) or displayName/mail (search)
                List<AzureADGroup> groups;
                if (Guid.TryParse(searchQuery, out var groupId))
                {
                    // Microsoft Graph does not support "$filter=id eq '...'" on /groups (it returns 400),
                    // so resolve an exact group id with a direct lookup instead. Ids that don't resolve to
                    // a group come back without a name, so filter those out.
                    var groupsById = await graphGroupRepository.GetGroupsAsync(new List<Guid> { groupId });
                    groups = groupsById.Where(g => !string.IsNullOrEmpty(g.Name)).ToList();
                }
                else
                {
                    var search = $"\"displayName:{searchSafe}\" OR \"mail:{searchSafe}\" OR \"mailNickname:{searchSafe}\"";
                    groups = await graphGroupRepository.SearchDestinationsBySearchAsync(search);
                }

                if (groups == null || groups.Count == 0)
                {
                    return JsonSerializer.Serialize(new
                    {
                        message = $"No groups found matching '{searchQuery}'. Try a different name or provide the group's email address.",
                        groups = Array.Empty<object>()
                    });
                }

                var groupResults = groups.Select(g => new
                {
                    objectId = g.ObjectId.ToString(),
                    displayName = g.Name ?? "Unknown",
                    email = g.Email ?? "No email"
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    message = groupResults.Count == 1
                        ? $"Found 1 group matching '{searchQuery}'"
                        : $"Found {groupResults.Count} groups matching '{searchQuery}'",
                    groups = groupResults
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search group tool failed for query '{SearchQuery}'", searchQuery);
                return JsonSerializer.Serialize(new
                {
                    error = "We couldn't find groups matching your query right now. Try a different name or try again."
                });
            }
        }

        private class SearchGroupArgs
        {
            public string? SearchQuery { get; set; }
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
                var adfRepo = scope.ServiceProvider.GetService<IAdfRunHistoryRepository>();

                if (sqlRepo == null || adfRepo == null)
                {
                    _logger.CopilotHrDbServicesUnavailable();
                    return (false, 0, 0);
                }

                var tableName = (await adfRepo.GetLatestAdfRunAsync() ?? throw new InvalidOperationException(AdfRunHistory.NoEligibleRunMessage)).GetTableName();

                // Use GetOrgLeaderDetailsAsync to get both employeeId and maxDepth in one call
                var (maxDepth, employeeId) = await sqlRepo.GetOrgLeaderDetailsAsync(azureObjectId, tableName);
                return (employeeId > 0, employeeId, maxDepth);
            }
            catch (Exception ex)
            {
                _logger.CopilotHrValidationFailed(ex);
                return (false, 0, 0);
            }
        }

        /// <summary>
        /// Parses the strict-schema operation response { message, operations[] }. Because the response
        /// format is a strict json_schema, the payload is well-formed JSON — no markdown/regex repair needed.
        /// </summary>
        private (string message, List<EditOperation> operations) ParseOperationResponse(string responseText)
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                var parsed = JsonSerializer.Deserialize<CopilotOperationResponse>(responseText, jsonOptions);
                if (parsed != null)
                {
                    return (parsed.Message ?? string.Empty, parsed.Operations ?? new List<EditOperation>());
                }
            }
            catch (JsonException ex)
            {
                _logger.CopilotLlmResponseJsonParseError(ex);
            }

            // The strict schema guarantees a message; if deserialization somehow fails, surface the raw text
            // as a message with no operations (the working query is returned unchanged).
            return (responseText, new List<EditOperation>());
        }

        /// <summary>
        /// Resolves org leader identifiers only from a validated confirmation or the unchanged inbound query.
        /// </summary>
        internal static void EnrichOrgLeaderObjectIds(
            List<EditOperation> operations,
            OrgLeaderConfirmationGate confirmationGate,
            IReadOnlyList<CopilotSourcePartResult>? workingQuery)
        {
            var existingRoots = workingQuery?.Where(part => part.UseOrgStructure &&
                    !string.IsNullOrWhiteSpace(part.OrgLeaderEmail) &&
                    !string.IsNullOrWhiteSpace(part.OrgLeaderObjectId))
                .GroupBy(part => part.OrgLeaderEmail!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, CopilotSourcePartResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in GetOrgLeaderParts(operations))
            {
                if (part == null || !part.UseOrgStructure)
                    continue;
                if (!string.IsNullOrWhiteSpace(part.OrgLeaderEmail) &&
                    existingRoots.TryGetValue(part.OrgLeaderEmail, out var existing))
                {
                    part.OrgLeaderObjectId = existing.OrgLeaderObjectId;
                }
                else if (!string.IsNullOrWhiteSpace(part.OrgLeaderEmail) &&
                         confirmationGate.TryGetValidated(part.OrgLeaderEmail, out var validated) &&
                         validated != null)
                {
                    part.OrgLeaderObjectId = validated.ObjectId;
                }
                else
                {
                    part.OrgLeaderObjectId = null;
                }
            }
        }

        internal static bool FindUnconfirmedOrgRoot(
            IReadOnlyList<CopilotSourcePartResult>? workingQuery,
            IReadOnlyList<EditOperation> operations,
            OrgLeaderConfirmationGate confirmationGate)
        {
            var existingRoots = workingQuery?.Where(part => part.UseOrgStructure)
                .Where(part => !string.IsNullOrWhiteSpace(part.OrgLeaderEmail))
                .Select(part => part.OrgLeaderEmail!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var blocked = false;
            foreach (var part in GetOrgLeaderParts(operations).Where(part => part?.UseOrgStructure == true))
            {
                var email = part!.OrgLeaderEmail;
                if (!string.IsNullOrWhiteSpace(email) && existingRoots.Contains(email))
                    continue;
                if (!string.IsNullOrWhiteSpace(email) &&
                    confirmationGate.IsValidated(email, part.OrgLeaderObjectId))
                    continue;

                if (!string.IsNullOrWhiteSpace(email) && confirmationGate.IsConfirmed(email))
                    confirmationGate.RequireValidation(email);
                else
                    confirmationGate.RequireConfirmation(email);
                blocked = true;
            }
            return blocked;
        }

        internal static (CopilotOperationApplyResult ApplyResult, string? ConfirmationMessage)
            ApplyOperationsWithConfirmation(
                IReadOnlyList<CopilotSourcePartResult>? workingQuery,
                List<EditOperation> operations,
                OrgLeaderConfirmationGate confirmationGate,
                string modelMessage)
        {
            EnrichOrgLeaderObjectIds(operations, confirmationGate, workingQuery);
            var hasUnconfirmedOrgRoot = FindUnconfirmedOrgRoot(workingQuery, operations, confirmationGate);
            var confirmationMessage = confirmationGate.Finish(modelMessage);
            var applyResult = CopilotOperationApplier.Apply(
                workingQuery,
                hasUnconfirmedOrgRoot || confirmationMessage != null
                    ? Array.Empty<EditOperation>()
                    : operations);
            return (applyResult, confirmationMessage);
        }

        private static IEnumerable<CopilotSourcePartResult?> GetOrgLeaderParts(IEnumerable<EditOperation> operations) =>
            operations.SelectMany(operation =>
                (operation.Part == null
                    ? Enumerable.Empty<CopilotSourcePartResult>()
                    : new[] { operation.Part })
                .Concat(operation.Parts ?? Enumerable.Empty<CopilotSourcePartResult>()));

        private static bool TryAuthorizeValidation(
            string argumentsJson,
            OrgLeaderConfirmationGate confirmationGate)
        {
            try
            {
                var args = JsonSerializer.Deserialize<ValidateOrgLeaderArgs>(
                    argumentsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return string.IsNullOrWhiteSpace(args?.Email) ||
                    confirmationGate.AllowValidation(args.Email.Trim());
            }
            catch (JsonException)
            {
                return true;
            }
        }

        private static bool TryAuthorizeLookup(
            string argumentsJson,
            OrgLeaderConfirmationGate confirmationGate)
        {
            try
            {
                var args = JsonSerializer.Deserialize<LookupPersonArgs>(
                    argumentsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return confirmationGate.AllowLookup(args?.SearchQuery);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        /// <summary>
        /// Renders the inbound working query (v2) — every part's partId and source type plus a plain-language
        /// hint — as the model's context block. Falls back to a minimal legacy current-filter block for v1.
        /// Never emits secrets/PII; renders only the query's own configured values.
        /// </summary>
        private static string BuildWorkingQueryContext(List<CopilotSourcePartResult>? workingQuery)
        {
            if (workingQuery != null)
            {
                if (workingQuery.Count == 0)
                {
                    return CopilotPrompts.WorkingQueryContextTemplate.Replace("{0}", "(empty — the user is starting a brand-new query)");
                }

                var lines = new List<string>();
                foreach (var part in workingQuery)
                {
                    var sourceType = string.IsNullOrWhiteSpace(part.SourceType) ? "SqlMembership" : part.SourceType;
                    var editable = CopilotOperationApplier.IsSupported(part) ? "editable" : "NOT editable by Copilot — preserve unchanged";
                    var descriptor = new List<string>
                    {
                        $"partId: {part.PartId}",
                        $"sourceType: {sourceType} ({editable})"
                    };
                    if (!string.IsNullOrEmpty(part.Title)) descriptor.Add($"title: {part.Title}");
                    if (!string.IsNullOrEmpty(part.Filter)) descriptor.Add($"filter: {part.Filter}");
                    if (part.IsExclusion) descriptor.Add("exclusionary: true");
                    if (part.UseOrgStructure)
                    {
                        descriptor.Add("orgStructure: enabled");
                        if (!string.IsNullOrEmpty(part.OrgLeaderName)) descriptor.Add($"orgLeader: {part.OrgLeaderName}");
                        if (part.OrgLeaderDepth.HasValue) descriptor.Add($"depth: {part.OrgLeaderDepth.Value}");
                    }
                    if (!string.IsNullOrEmpty(part.GroupName)) descriptor.Add($"group: {part.GroupName}");
                    if (!string.IsNullOrEmpty(part.GroupId)) descriptor.Add($"groupId: {part.GroupId}");
                    lines.Add("- " + string.Join(" | ", descriptor));
                }

                return CopilotPrompts.WorkingQueryContextTemplate.Replace("{0}", string.Join("\n", lines));
            }

            return string.Empty;
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
                _logger.CopilotHrAttributesFetchFailed(ex);
                // Return empty list on error, don't crash
                return new List<HrAttributeInfo>();
            }
        }

        private static readonly MemoryCache _userContextCache = new(new MemoryCacheOptions
        {
            SizeLimit = 2000
        });
        private static readonly TimeSpan _userContextCacheDuration = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Resolves the caller's profile, manager, and HR eligibility from their token-derived object id.
        /// Best effort: any failed lookup leaves the corresponding fields empty and never fails the chat turn.
        /// Also pre-validates the user and manager as org leaders so their objectIds survive into the response.
        /// </summary>
        private async Task EnrichUserContextAsync(CopilotUserContext userContext)
        {
            if (string.IsNullOrEmpty(userContext.ObjectId)) return;

            if (_userContextCache.TryGetValue<CopilotUserContext>(userContext.ObjectId, out var cached) && cached != null)
            {
                CopyResolved(cached, userContext);
            }
            else
            {
                var resolved = new CopilotUserContext { ObjectId = userContext.ObjectId };
                var graphLookupSucceeded = false;

                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var graphClient = scope.ServiceProvider.GetRequiredService<GraphServiceClient>();

                    var me = await graphClient.Users[userContext.ObjectId].GetAsync(config =>
                    {
                        config.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName" };
                    });
                    resolved.DisplayName = me?.DisplayName;
                    resolved.Email = me?.Mail ?? me?.UserPrincipalName;
                    resolved.Alias = me?.UserPrincipalName?.Split('@')[0];

                    var manager = await graphClient.Users[userContext.ObjectId].Manager.GetAsync()
                        as Microsoft.Graph.Models.User;
                    if (manager?.Id != null)
                    {
                        resolved.ManagerObjectId = manager.Id;
                        resolved.ManagerName = manager.DisplayName;
                        resolved.ManagerEmail = manager.Mail ?? manager.UserPrincipalName;
                        resolved.ManagerAlias = manager.UserPrincipalName?.Split('@')[0];
                    }

                    graphLookupSucceeded = me != null;
                }
                catch (Exception ex)
                {
                    _logger.CopilotUserContextResolveFailed(ex);
                }

                var (userInHr, employeeId, maxDepth) = await ValidateUserInHrDbAsync(userContext.ObjectId);
                resolved.IsInHrDatabase = userInHr;
                resolved.EmployeeId = userInHr ? employeeId : null;
                resolved.OrgMaxDepth = userInHr ? maxDepth : null;

                if (!string.IsNullOrEmpty(resolved.ManagerObjectId))
                {
                    var (managerInHr, _, _) = await ValidateUserInHrDbAsync(resolved.ManagerObjectId);
                    resolved.ManagerIsInHrDatabase = managerInHr;
                }

                // Do not suppress retries after a transient Graph failure.
                if (graphLookupSucceeded)
                {
                    _userContextCache.Set(userContext.ObjectId, resolved, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _userContextCacheDuration,
                        Size = 1
                    });
                }
                CopyResolved(resolved, userContext);
            }

        }

        private static void CopyResolved(CopilotUserContext source, CopilotUserContext target)
        {
            // Token claims win for identity; Graph fills gaps.
            target.DisplayName = string.IsNullOrEmpty(target.DisplayName) ? source.DisplayName : target.DisplayName;
            target.Email = string.IsNullOrEmpty(target.Email) ? source.Email : target.Email;
            target.Alias = string.IsNullOrEmpty(target.Alias) ? source.Alias : target.Alias;
            target.EmployeeId = source.EmployeeId;
            target.OrgMaxDepth = source.OrgMaxDepth;
            target.IsInHrDatabase = source.IsInHrDatabase;
            target.ManagerObjectId = source.ManagerObjectId;
            target.ManagerName = source.ManagerName;
            target.ManagerEmail = source.ManagerEmail;
            target.ManagerAlias = source.ManagerAlias;
            target.ManagerIsInHrDatabase = source.ManagerIsInHrDatabase;
        }

        private string BuildUserContextSection(CopilotUserContext userContext)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(userContext.DisplayName) || !string.IsNullOrEmpty(userContext.Email))
            {
                var userInfo = userContext.DisplayName ?? userContext.Email;
                if (!string.IsNullOrEmpty(userContext.Email) && !string.IsNullOrEmpty(userContext.DisplayName))
                    userInfo += $" ({userContext.Email})";

                parts.Add($"- Logged-in user: {userInfo}");
                parts.Add(userContext.IsInHrDatabase
                    ? $"- Logged-in user is in the HR database and can be used as an org leader (max org depth: {userContext.OrgMaxDepth})"
                    : "- Logged-in user is NOT in the HR database and cannot be used as an org leader");
            }

            if (!string.IsNullOrEmpty(userContext.ManagerName))
            {
                var managerInfo = userContext.ManagerName;
                if (!string.IsNullOrEmpty(userContext.ManagerEmail))
                    managerInfo += $" ({userContext.ManagerEmail})";

                parts.Add($"- User's Manager: {managerInfo}");
                parts.Add(userContext.ManagerIsInHrDatabase
                    ? "- Manager is in the HR database"
                    : "- Manager is NOT in the HR database and cannot be used as an org leader");
            }

            if (parts.Count == 0) return string.Empty;

            return CopilotPrompts.UserContextTemplate.Replace("{0}", string.Join("\n", parts));
        }

        private async Task<string> FetchAttributeValuesAsync(List<string> attributeNames, string? searchTerm = null)
        {
            var normalizedSearch = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim();

            var cacheKey = string.Join("|",
                string.Join(",", attributeNames.OrderBy(a => a, StringComparer.OrdinalIgnoreCase)),
                normalizedSearch ?? string.Empty);
            if (_attributeValueCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var sqlMembershipRepository = scope.ServiceProvider.GetService<ISqlMembershipRepository>();
            var dataFactoryRepository = scope.ServiceProvider.GetService<IAdfRunHistoryRepository>();

            if (sqlMembershipRepository == null || dataFactoryRepository == null)
            {
                return JsonSerializer.Serialize(new { error = "Database services unavailable", attributes = attributeNames });
            }

            string tableName;
            try
            {
                tableName = (await dataFactoryRepository.GetLatestAdfRunAsync() ?? throw new InvalidOperationException(AdfRunHistory.NoEligibleRunMessage)).GetTableName();
            }
            catch (Exception ex)
            {
                _logger.CopilotAdfRunIdFetchFailed(ex);
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
                    var (mappings, hasMore) = await sqlMembershipRepository.GetAttributeMappingsPageAsync(baseAttrName, tableName, normalizedSearch, CopilotAttributeValueLimit, AttributeSearchMode.Substring);
                    return (attrName, mappings ?? new List<(string Code, string Description)>(), hasMore, (string?)null);
                }
                catch (Exception ex)
                {
                    return (attrName, new List<(string Code, string Description)>(), false, ex.Message);
                }
            });

            var results = await Task.WhenAll(fetchTasks);

            // Build structured JSON response for the tool
            var attributeValues = new Dictionary<string, object>();
            foreach (var (attrName, mappings, hasMore, error) in results)
            {
                if (!string.IsNullOrEmpty(error))
                {
                    attributeValues[attrName] = new { error };
                }
                else if (mappings.Count == 0)
                {
                    // Empty result — either the attribute has no predefined values, or a searchTerm
                    // filtered everything out. Signal both cases so the LLM knows what happened.
                    attributeValues[attrName] = new
                    {
                        values = Array.Empty<object>(),
                        hasMore = false,
                        searchTerm = normalizedSearch,
                        note = normalizedSearch == null
                            ? "No predefined values - use appropriate numeric or string values"
                            : $"No matches for searchTerm '{normalizedSearch}'. Ask the user to check the spelling or use a different value; do NOT substitute a value from another attribute."
                    };
                }
                else
                {
                    attributeValues[attrName] = new
                    {
                        values = mappings.Select(m => new { code = m.Code, description = m.Description }).ToList(),
                        hasMore,
                        searchTerm = normalizedSearch,
                        note = hasMore
                            ? (normalizedSearch == null
                                ? $"Showing first {mappings.Count} of many values. If the user named a specific value, call get_attribute_values again with searchTerm=<user's value>."
                                : $"Showing first {mappings.Count} matches for searchTerm '{normalizedSearch}'. More rows exist — ask the user to narrow (e.g. add another word) and call get_attribute_values again with a more specific searchTerm.")
                            : null
                    };
                }
            }

            // Wrap in an object with a validation reminder for the LLM
            var wrappedResult = new Dictionary<string, object>
            {
                ["attributes"] = attributeValues,
                ["VALIDATION_RULE"] = "CRITICAL: For each attribute in a filter, the value MUST exist in that SAME attribute's `values` above. If the user's requested value is NOT present and `hasMore` is true, you MUST call get_attribute_values AGAIN with searchTerm=<user's value> BEFORE claiming the value is missing. Only after a searchTerm call returns zero matches may you tell the user it was not found. NEVER substitute a value from another attribute."
            };

            var result = JsonSerializer.Serialize(wrappedResult, new JsonSerializerOptions { WriteIndented = false });

            // Store in request-scoped cache for reuse within this conversation
            _attributeValueCache.TryAdd(cacheKey, result);

            return result;
        }

        #endregion

        #region AI Settings and Prompt Helpers

        private async Task<Dictionary<SettingKey, string>> GetCachedAISettingsAsync()
        {
            lock (_aiSettingsLock)
            {
                if (_aiSettingsCache != null && DateTime.UtcNow < _aiSettingsCacheExpiry)
                {
                    return new Dictionary<SettingKey, string>(_aiSettingsCache);
                }
            }

            try
            {
                var settings = new Dictionary<SettingKey, string>();
                var keysToLoad = new[] { SettingKey.IsAICopilotEnabled, SettingKey.CopilotTemperature, SettingKey.CopilotTopP };

                foreach (var key in keysToLoad)
                {
                    try
                    {
                        var setting = await _settingsRepository.GetSettingByKeyAsync(key);
                        if (setting != null)
                        {
                            settings[key] = setting.SettingValue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load AI setting {SettingKey}", key);
                    }
                }

                lock (_aiSettingsLock)
                {
                    _aiSettingsCache = new Dictionary<SettingKey, string>(settings);
                    _aiSettingsCacheExpiry = DateTime.UtcNow.Add(_aiSettingsCacheDuration);
                }

                return new Dictionary<SettingKey, string>(settings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load AI settings from database, using defaults");
                var defaultSettings = new Dictionary<SettingKey, string>();
                lock (_aiSettingsLock)
                {
                    _aiSettingsCache = new Dictionary<SettingKey, string>(defaultSettings);
                    _aiSettingsCacheExpiry = DateTime.UtcNow.Add(_aiSettingsCacheDuration);
                }
                return new Dictionary<SettingKey, string>(defaultSettings);
            }
        }

        private async Task<string> GetCachedPromptAsync()
        {
            lock (_aiPromptLock)
            {
                if (_aiPromptCache != null && DateTime.UtcNow < _aiPromptCacheExpiry)
                {
                    return _aiPromptCache;
                }
            }

            try
            {
                var setting = await _settingsRepository.GetSettingByKeyAsync(SettingKey.CopilotInstructions);

                if (setting != null && !string.IsNullOrWhiteSpace(setting.SettingValue))
                {
                    lock (_aiPromptLock)
                    {
                        _aiPromptCache = setting.SettingValue;
                        _aiPromptCacheExpiry = DateTime.UtcNow.Add(_aiPromptCacheDuration);
                    }
                    return setting.SettingValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load AI prompt from database, using default");
            }

            var defaultPrompt = CopilotPrompts.DefaultInstructions;
            lock (_aiPromptLock)
            {
                _aiPromptCache = defaultPrompt;
                _aiPromptCacheExpiry = DateTime.UtcNow.Add(_aiPromptCacheDuration);
            }
            return defaultPrompt;
        }

        #endregion
    }
}
