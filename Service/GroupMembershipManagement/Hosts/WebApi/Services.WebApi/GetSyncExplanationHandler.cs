// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Models.SyncJobChange;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Services
{
    public class GetSyncExplanationHandler : RequestHandlerBase<GetSyncExplanationRequest, GetSyncExplanationResponse>
    {
        private const string FallbackExplanation = "The specific reason could not be determined from the available data.";

        private static readonly string SystemPrompt = @"You are a sync analysis assistant for Group Membership Management.
Given context about a sync run and a specific user, explain in 1-2 sentences why the user was added to or removed from the group during this sync.
Consider both configuration changes (filter updates) and attribute changes (user properties no longer matching the filter criteria).
Use only the provided data. Do not mention specific values of user attributes — describe changes generically (e.g., ""a property no longer matches the filter criteria"").
If you cannot determine the reason with reasonable confidence, say ""The specific reason could not be determined from the available data.""";

        private readonly ILogger<GetSyncExplanationHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        private readonly IOpenAIService _openAIService;

        public GetSyncExplanationHandler(
            ILogger<GetSyncExplanationHandler> logger,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobHistoryRepository syncJobHistoryRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IBlobStorageRepository blobStorageRepository,
            IDataFactoryRepository dataFactoryRepository,
            ISqlMembershipRepository sqlMembershipRepository,
            IDatabaseSqlMembershipSourcesRepository databaseSqlMembershipSourcesRepository,
            IOpenAIService openAIService) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _databaseSqlMembershipSourcesRepository = databaseSqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(databaseSqlMembershipSourcesRepository));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
        }

        protected override async Task<GetSyncExplanationResponse> ExecuteCoreAsync(GetSyncExplanationRequest request)
        {
            var response = new GetSyncExplanationResponse();

            try
            {
                var syncJob = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
                if (syncJob == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

                var runHistory = await _syncJobHistoryRepository.GetByRunIdAsync(request.RunId);
                if (runHistory == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

                var membershipChange = await GetUserMembershipChangeAsync(
                    syncJob.TargetOfficeGroupId.ToString(), request.RunId, request.UserObjectId);

                if (membershipChange == null)
                {
                    response.Explanation = FallbackExplanation;
                    response.StatusCode = HttpStatusCode.OK;
                    return response;
                }

                var configChanges = await GetRecentConfigChangesAsync(request.SyncJobId, runHistory);
                var userAttributes = await GetUserAttributesForPromptAsync(syncJob.Query, request.UserObjectId);

                var userPrompt = BuildUserPrompt(request, runHistory, syncJob.Query, membershipChange.Value, configChanges, userAttributes);
                var explanation = await _openAIService.GetCompletionAsync(SystemPrompt, userPrompt);

                response.Explanation = string.IsNullOrWhiteSpace(explanation) ? FallbackExplanation : explanation.Trim();
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "OpenAI call timed out for SyncJob {SyncJobId}, Run {RunId}", request.SyncJobId, request.RunId);
                response.Explanation = FallbackExplanation;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate sync explanation for SyncJob {SyncJobId}, Run {RunId}", request.SyncJobId, request.RunId);
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }

        private async Task<MembershipChangeType?> GetUserMembershipChangeAsync(string targetGroupId, Guid runId, Guid userObjectId)
        {
            var blobResult = await _blobStorageRepository.FindAggregatedFileByRunIdAsync(targetGroupId, runId.ToString());
            if (blobResult.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(blobResult.Path))
            {
                return null;
            }

            var fileContent = await _blobStorageRepository.DownloadFileAsync(blobResult.Path);
            if (fileContent.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(fileContent.Content))
            {
                return null;
            }

            var json = TryDecompress(fileContent.Content);
            return ParseMembershipChange(json, userObjectId);
        }

        private static string TryDecompress(string content)
        {
            try
            {
                return TextCompressor.Decompress(content);
            }
            catch (FormatException)
            {
                return content;
            }
        }

        private static MembershipChangeType? ParseMembershipChange(string json, Guid userObjectId)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                if (!TryGetPropertyCaseInsensitive(document.RootElement, "SourceMembers", out var sourceMembers)
                    || sourceMembers.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var member in sourceMembers.EnumerateArray())
                {
                    if (!TryReadObjectId(member, out var memberObjectId) || memberObjectId != userObjectId)
                        continue;

                    if (TryReadMembershipAction(member, out var action))
                    {
                        if (action == MembershipAction.Add) return MembershipChangeType.Added;
                        if (action == MembershipAction.Remove) return MembershipChangeType.Removed;
                    }
                }

                return null;
            }
            catch (JsonException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private async Task<string> GetRecentConfigChangesAsync(Guid syncJobId, Models.SyncJobHistory.SyncJobHistory runHistory)
        {
            try
            {
                var changesPage = await _syncJobChangeRepository.GetPageBySyncJobId(
                    syncJobId, startPage: 1, pageSize: 10,
                    SyncJobChangeSortingField.ChangeTime, sortAscending: false);

                if (changesPage?.Items == null || !changesPage.Items.Any())
                    return "None";

                var runTime = runHistory.EndTime ?? runHistory.StartTime ?? runHistory.UpdatedAt;
                var windowStart = runTime.AddDays(-1);
                var windowEnd = runTime.AddHours(1);

                var nearbyChanges = changesPage.Items
                    .Where(c => c.ChangeTime >= windowStart && c.ChangeTime <= windowEnd)
                    .ToList();

                if (!nearbyChanges.Any())
                    return "None";

                var sb = new StringBuilder();
                foreach (var change in nearbyChanges)
                {
                    sb.AppendLine($"- {change.ChangeTime:u}: {change.ChangeReason} by {change.ChangedByDisplayName ?? "system"}");
                    if (!string.IsNullOrWhiteSpace(change.BusinessJustification))
                        sb.AppendLine($"  Justification: {change.BusinessJustification}");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load config changes for SyncJob {SyncJobId}", syncJobId);
                return "Unable to retrieve configuration changes.";
            }
        }

        private async Task<string> GetUserAttributesForPromptAsync(string? query, Guid userObjectId)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "No filter configured.";

            try
            {
                var adfRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                if (string.IsNullOrWhiteSpace(adfRunId))
                    return "ADF data unavailable.";

                var tableName = adfRunId.Replace("-", "");
                var tableExists = await _sqlMembershipRepository.CheckIfTableExistsAsync(tableName);
                if (!tableExists)
                    return "ADF data table not found.";

                var userAttributes = await _sqlMembershipRepository.GetUserAttributesAsync(
                    userObjectId.ToString(), tableName);

                if (userAttributes == null || userAttributes.Count == 0)
                    return "User not found in HR data.";

                var sensitiveAttributes = await GetSensitiveAttributeNamesAsync();
                var filterAttributeNames = ExtractFilterAttributeNames(query);

                if (!filterAttributeNames.Any())
                    return "Could not parse filter attributes.";

                var sb = new StringBuilder();
                foreach (var attrName in filterAttributeNames)
                {
                    if (sensitiveAttributes.Contains(attrName))
                    {
                        sb.AppendLine($"{attrName}: [PROTECTED - value hidden]");
                        continue;
                    }

                    // Check both the attribute name and the _Code variant
                    var codeName = $"{attrName}_Code";
                    if (userAttributes.TryGetValue(attrName, out var value))
                    {
                        sb.AppendLine($"{attrName}: {value}");
                    }
                    else if (userAttributes.TryGetValue(codeName, out var codeValue))
                    {
                        sb.AppendLine($"{attrName}: {codeValue}");
                    }
                    else
                    {
                        sb.AppendLine($"{attrName}: [not present in user data]");
                    }
                }

                return sb.Length > 0 ? sb.ToString().TrimEnd() : "No matching attributes found.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load user attributes for {UserObjectId}", userObjectId);
                return "Unable to retrieve user attributes.";
            }
        }

        private async Task<HashSet<string>> GetSensitiveAttributeNamesAsync()
        {
            try
            {
                var attributes = await _databaseSqlMembershipSourcesRepository.GetDefaultSourceAttributesAsync();
                if (attributes == null)
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                return attributes
                    .Where(a => a.Sensitive)
                    .Select(a => a.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static HashSet<string> ExtractFilterAttributeNames(string query)
        {
            var attributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Parse the JSON query to extract filter parts
                var parts = JsonSerializer.Deserialize<List<JsonElement>>(query);
                if (parts == null) return attributeNames;

                foreach (var part in parts)
                {
                    if (part.TryGetProperty("filter", out var filterElement) ||
                        part.TryGetProperty("Filter", out filterElement))
                    {
                        var filter = filterElement.GetString();
                        if (!string.IsNullOrWhiteSpace(filter))
                        {
                            ExtractAttributeNamesFromSqlFilter(filter, attributeNames);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Query might be a raw SQL filter string, not JSON
                ExtractAttributeNamesFromSqlFilter(query, attributeNames);
            }

            return attributeNames;
        }

        private static void ExtractAttributeNamesFromSqlFilter(string filter, HashSet<string> attributeNames)
        {
            // Simple extraction: find identifiers before operators
            var operators = new[] { "=", "<>", ">=", "<=", ">", "<", " IN ", " NOT IN ", " LIKE ", " NOT LIKE " };
            var logicalOps = new[] { " AND ", " OR " };

            // Split by logical operators first
            var conditions = new List<string> { filter };
            foreach (var logOp in logicalOps)
            {
                var newConditions = new List<string>();
                foreach (var condition in conditions)
                {
                    newConditions.AddRange(condition.Split(new[] { logOp }, StringSplitOptions.RemoveEmptyEntries));
                }
                conditions = newConditions;
            }

            foreach (var condition in conditions)
            {
                var trimmed = condition.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                foreach (var op in operators)
                {
                    var opIndex = trimmed.IndexOf(op, StringComparison.OrdinalIgnoreCase);
                    if (opIndex > 0)
                    {
                        var attrName = trimmed.Substring(0, opIndex).Trim()
                            .TrimStart('(')
                            .Trim();
                        if (!string.IsNullOrWhiteSpace(attrName) && !attrName.Contains(' '))
                        {
                            attributeNames.Add(attrName);
                        }
                        break;
                    }
                }
            }
        }

        private static string BuildUserPrompt(
            GetSyncExplanationRequest request,
            Models.SyncJobHistory.SyncJobHistory runHistory,
            string? query,
            MembershipChangeType changeType,
            string configChanges,
            string userAttributes)
        {
            var endTime = runHistory.EndTime ?? runHistory.StartTime ?? runHistory.UpdatedAt;
            var changeVerb = changeType == MembershipChangeType.Added ? "Added" : "Removed";

            return $@"Sync Run: {request.RunId}
Date: {endTime:u}
Status: {runHistory.Status}
Before sync: {runHistory.BeforeSyncUserCount ?? 0} members | After sync: {runHistory.AfterSyncUserCount ?? 0} members
Users added: {runHistory.UsersAdded ?? 0} | Users removed: {runHistory.UsersRemoved ?? 0}

Job filter: {query ?? "None"}

User {request.UserObjectId} was {changeVerb} in this run.

User's current HR attributes (for filter-referenced fields):
{userAttributes}

Recent configuration changes (near this run):
{configChanges}";
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static bool TryReadObjectId(JsonElement member, out Guid objectId)
        {
            objectId = Guid.Empty;
            if (!TryGetPropertyCaseInsensitive(member, "ObjectId", out var objectIdElement)
                || objectIdElement.ValueKind != JsonValueKind.String)
                return false;
            return Guid.TryParse(objectIdElement.GetString(), out objectId);
        }

        private static bool TryReadMembershipAction(JsonElement member, out MembershipAction action)
        {
            action = MembershipAction.None;
            if (!TryGetPropertyCaseInsensitive(member, "MembershipAction", out var actionElement))
                return false;

            if (actionElement.ValueKind == JsonValueKind.Number)
            {
                if (actionElement.TryGetInt32(out var actionInt) && Enum.IsDefined(typeof(MembershipAction), actionInt))
                {
                    action = (MembershipAction)actionInt;
                    return true;
                }
                return false;
            }

            if (actionElement.ValueKind == JsonValueKind.String)
            {
                var actionString = actionElement.GetString();
                return Enum.TryParse(actionString, true, out action);
            }

            return false;
        }
    }
}
