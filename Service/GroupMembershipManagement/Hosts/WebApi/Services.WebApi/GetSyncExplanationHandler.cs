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

        private static readonly string SystemPrompt = @"You are a sync analysis assistant for Group Membership Management (GMM).
Given context about a sync run and a specific user, explain in 1-2 sentences why the user was added to or removed from the group during this sync.

GMM syncs membership from source parts (groups or HR/SQL filters) into a destination group. A sync job's configuration (its ""query"") is a list of source parts, each of which can be:
- A **GroupMembership** source: includes or excludes members of another Entra ID group.
- A **SqlMembership** source: includes or excludes employees matching an HR data filter (e.g., Building, Department).

Each source part may be **inclusionary** (members are added) or **exclusionary** (members are removed from the final result).

When explaining, use the most specific explanation that fits the data. Follow these patterns:

1. **New group source added**: ""This user was added/removed because a new group [source ID] was included/excluded in the membership configuration on [date], and they are/are not a member of that group.""
2. **Group source removed**: ""This user was removed/added because the source group [source ID] was removed from the membership configuration on [date], and they were a member of that group.""
3. **HR filter criteria changed**: ""This user was removed because the membership filter was updated on [date] to require [new criteria], and they do not meet that criteria (their [attribute] is '[value]').""
4. **Exclusionary part added**: ""This user was removed because a new exclusion rule was added on [date] that excludes employees where [criteria], and they match that exclusion.""
5. **User attribute changed (no config change)**: ""This user was removed because their [attribute] changed from [old value] to [new value], which no longer matches the filter criteria [filter].""
6. **No config change, user matches filter**: ""This user was added because their current attributes match the sync filter criteria.""

Always reference specific dates, attribute names, and values from the provided data when available.
Use only the provided data. If you cannot determine the reason with reasonable confidence, say ""The specific reason could not be determined from the available data.""";

        private readonly ILogger<GetSyncExplanationHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
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
            IGraphGroupRepository graphGroupRepository,
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
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
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

                // If user doesn't have AI_SYNC_JOB or tenant-level role, check group ownership
                if (!request.HasAiSyncJobRole)
                {
                    var isOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(
                        request.UserIdentity, syncJob.TargetOfficeGroupId);
                    if (!isOwner)
                    {
                        response.StatusCode = HttpStatusCode.Forbidden;
                        return response;
                    }
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
                var configDiff = await GetConfigurationDiffAsync(request.SyncJobId, runHistory);
                var userAttributes = await GetUserAttributesForPromptAsync(syncJob.Query, request.UserObjectId);

                var userPrompt = BuildUserPrompt(request, runHistory, syncJob.Query, membershipChange.Value, configChanges, configDiff, userAttributes);
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

        /// <summary>
        /// Scans the SourceMembers array using a forward-only Utf8JsonReader
        /// to find the target user. This avoids allocating a full JsonDocument
        /// DOM for the entire blob and exits as soon as the user is found.
        /// </summary>
        private static MembershipChangeType? ParseMembershipChange(string json, Guid userObjectId)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                // Read past root StartObject
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return null;

                // Scan root-level properties for "SourceMembers"
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        return null;

                    if (reader.TokenType == JsonTokenType.PropertyName &&
                        (reader.ValueTextEquals("SourceMembers"u8) || reader.ValueTextEquals("sourceMembers"u8)))
                    {
                        break;
                    }

                    // Skip the value of non-matching properties
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        reader.Read();
                        reader.TrySkip();
                    }
                }

                // Expect array start
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                    return null;

                // Scan each member object in the array
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        break;

                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.TrySkip();
                        continue;
                    }

                    Guid? objectId = null;
                    MembershipAction? membershipAction = null;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            if (reader.ValueTextEquals("ObjectId"u8) ||
                                reader.ValueTextEquals("objectId"u8))
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.String &&
                                    Guid.TryParse(reader.GetString(), out var id))
                                {
                                    objectId = id;
                                }
                            }
                            else if (reader.ValueTextEquals("MembershipAction"u8) ||
                                     reader.ValueTextEquals("membershipAction"u8))
                            {
                                if (reader.Read())
                                {
                                    if (reader.TokenType == JsonTokenType.Number &&
                                        reader.TryGetInt32(out var actionInt) &&
                                        Enum.IsDefined(typeof(MembershipAction), actionInt))
                                    {
                                        membershipAction = (MembershipAction)actionInt;
                                    }
                                    else if (reader.TokenType == JsonTokenType.String &&
                                             Enum.TryParse<MembershipAction>(reader.GetString(), true, out var actionEnum))
                                    {
                                        membershipAction = actionEnum;
                                    }
                                }
                            }
                            else
                            {
                                reader.Read();
                                reader.TrySkip();
                            }
                        }
                    }

                    // Early exit: return as soon as we find the target user
                    if (objectId == userObjectId && membershipAction.HasValue)
                    {
                        if (membershipAction.Value == MembershipAction.Add) return MembershipChangeType.Added;
                        if (membershipAction.Value == MembershipAction.Remove) return MembershipChangeType.Removed;
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

        private async Task<string> GetConfigurationDiffAsync(Guid syncJobId, Models.SyncJobHistory.SyncJobHistory runHistory)
        {
            try
            {
                var runTime = runHistory.EndTime ?? runHistory.StartTime ?? runHistory.UpdatedAt;
                var recentChanges = await _syncJobChangeRepository.GetRecentConfigChangesBySyncJobIdAsync(
                    syncJobId, runTime, count: 2);

                if (recentChanges == null || recentChanges.Count == 0)
                    return "No configuration changes found.";

                var currentChange = recentChanges[0];
                var currentQuery = ExtractQueryFromChangeDetails(currentChange.ChangeDetails);
                var previousQuery = recentChanges.Count > 1
                    ? ExtractQueryFromChangeDetails(recentChanges[1].ChangeDetails)
                    : null;

                var sb = new StringBuilder();
                sb.AppendLine($"Last configuration change on {currentChange.ChangeTime:yyyy-MM-dd} by {currentChange.ChangedByDisplayName ?? "system"} ({currentChange.ChangeReason}):");

                if (previousQuery != null && !string.Equals(NormalizeQuery(previousQuery), NormalizeQuery(currentQuery), StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"Previous configuration: {previousQuery}");
                    sb.AppendLine($"Current configuration: {currentQuery ?? "None"}");
                    var structuralDiff = DescribeQueryDiff(previousQuery, currentQuery);
                    if (!string.IsNullOrWhiteSpace(structuralDiff))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"What changed:");
                        sb.Append(structuralDiff);
                    }
                }
                else if (previousQuery != null)
                {
                    sb.AppendLine($"Configuration (unchanged): {currentQuery ?? "None"}");
                }
                else
                {
                    sb.AppendLine($"Initial configuration: {currentQuery ?? "None"}");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load configuration diff for SyncJob {SyncJobId}", syncJobId);
                return "Unable to retrieve configuration history.";
            }
        }

        private static string DescribeQueryDiff(string? previousQuery, string? currentQuery)
        {
            var previousParts = ParseQueryParts(previousQuery);
            var currentParts = ParseQueryParts(currentQuery);

            if (previousParts == null && currentParts == null)
                return string.Empty;

            var sb = new StringBuilder();

            var previousByKey = (previousParts ?? new List<QueryPartInfo>())
                .ToDictionary(p => p.Key, p => p);
            var currentByKey = (currentParts ?? new List<QueryPartInfo>())
                .ToDictionary(p => p.Key, p => p);

            // Parts added
            foreach (var kvp in currentByKey)
            {
                if (!previousByKey.ContainsKey(kvp.Key))
                {
                    var part = kvp.Value;
                    var role = part.Exclusionary ? "exclusionary" : "inclusionary";
                    if (part.Type.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- New {role} group source added: {part.Source}");
                    }
                    else if (part.Type.Equals("SqlMembership", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- New {role} HR/SQL filter source added: {part.Filter ?? "no filter"}");
                    }
                    else
                    {
                        sb.AppendLine($"- New {role} source added (type: {part.Type}): {part.Source}");
                    }
                }
            }

            // Parts removed
            foreach (var kvp in previousByKey)
            {
                if (!currentByKey.ContainsKey(kvp.Key))
                {
                    var part = kvp.Value;
                    var role = part.Exclusionary ? "exclusionary" : "inclusionary";
                    if (part.Type.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- {role.Substring(0, 1).ToUpper() + role.Substring(1)} group source removed: {part.Source}");
                    }
                    else if (part.Type.Equals("SqlMembership", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- {role.Substring(0, 1).ToUpper() + role.Substring(1)} HR/SQL filter source removed: {part.Filter ?? "no filter"}");
                    }
                    else
                    {
                        sb.AppendLine($"- {role.Substring(0, 1).ToUpper() + role.Substring(1)} source removed (type: {part.Type}): {part.Source}");
                    }
                }
            }

            // Parts modified (same key but different properties)
            foreach (var kvp in currentByKey)
            {
                if (previousByKey.TryGetValue(kvp.Key, out var prevPart))
                {
                    var currPart = kvp.Value;

                    if (prevPart.Exclusionary != currPart.Exclusionary)
                    {
                        var oldRole = prevPart.Exclusionary ? "exclusionary" : "inclusionary";
                        var newRole = currPart.Exclusionary ? "exclusionary" : "inclusionary";
                        sb.AppendLine($"- Source {currPart.Source ?? currPart.Filter ?? currPart.Type} changed from {oldRole} to {newRole}");
                    }

                    if (!string.Equals(prevPart.Filter?.Trim(), currPart.Filter?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- Filter changed from \"{prevPart.Filter ?? "none"}\" to \"{currPart.Filter ?? "none"}\"");
                    }
                }
            }

            return sb.ToString();
        }

        private static List<QueryPartInfo>? ParseQueryParts(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            try
            {
                var parts = new List<QueryPartInfo>();
                using var document = JsonDocument.Parse(query);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return null;

                int index = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    var part = new QueryPartInfo { Index = index++ };

                    if (element.TryGetProperty("type", out var typeEl))
                        part.Type = typeEl.GetString() ?? "Unknown";

                    if (element.TryGetProperty("source", out var sourceEl))
                        part.Source = sourceEl.GetString();

                    if (element.TryGetProperty("filter", out var filterEl) ||
                        element.TryGetProperty("Filter", out filterEl))
                        part.Filter = filterEl.GetString();

                    if (element.TryGetProperty("exclusionary", out var exclEl))
                        part.Exclusionary = exclEl.ValueKind == JsonValueKind.True;

                    parts.Add(part);
                }

                return parts;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private class QueryPartInfo
        {
            public int Index { get; set; }
            public string Type { get; set; } = "Unknown";
            public string? Source { get; set; }
            public string? Filter { get; set; }
            public bool Exclusionary { get; set; }

            // Key for matching parts across configs: type + source for group-based parts,
            // type + index for SQL parts (since they don't have a unique source ID)
            public string Key => !string.IsNullOrEmpty(Source)
                ? $"{Type}|{Source}"
                : $"{Type}|{Index}";
        }

        private static string? ExtractQueryFromChangeDetails(string? changeDetails)
        {
            if (string.IsNullOrWhiteSpace(changeDetails))
                return null;

            try
            {
                using var document = JsonDocument.Parse(changeDetails);
                if (document.RootElement.TryGetProperty("Query", out var queryElement)
                    || document.RootElement.TryGetProperty("query", out queryElement))
                {
                    return queryElement.GetString();
                }
            }
            catch (JsonException) { }
            return null;
        }

        private static string? NormalizeQuery(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;
            return query.Trim();
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
            string configDiff,
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

Configuration history:
{configDiff}

Recent configuration changes (near this run):
{configChanges}";
        }

    }
}
