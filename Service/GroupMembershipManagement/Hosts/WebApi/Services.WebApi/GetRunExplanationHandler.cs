// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Services
{
    public class GetRunExplanationHandler : RequestHandlerBase<GetRunExplanationRequest, GetRunExplanationResponse>
    {
        public const string FallbackExplanation = "The specific reason could not be determined from the available data.";
        public const string NotEnoughInformation = "Not enough information to determine a cause.";
        public const string NoMembershipChanges = "This sync completed with no membership changes.";

        // Cap on group-source display names resolved via Graph per row expansion; falls back to raw GUIDs on Graph failure.
        public const int MaxGroupNamesToResolve = 25;

        // Cap on manager scopes resolved to display names per row expansion (SQL + Graph roundtrip each).
        public const int MaxManagerNamesToResolve = 10;

        // Attribution caps: parallel per-part probes; 1000-user cap preserves bucket accuracy.
        public const int MaxSqlRulesToIntersect = 5;
        public const int MaxAddsForRuleAttribution = 1000;
        public const int MaxGroupPartsToAttribute = 10;

        // Per-run (population-level) system prompt; kept separate from the per-user prompt so each is independently tunable.
        private static readonly string SystemPrompt = @"You are a sync analysis assistant for Group Membership Management (GMM).
Given context about a sync run, explain in 1-2 sentences why this run added or removed the members it did.

GMM syncs membership from source parts into a destination group. A sync job's configuration (its ""query"") is a list of source parts, each of which can be:
- A **GroupMembership** source: includes or excludes members of another Entra ID group.
- A **SqlMembership** source: includes or excludes employees from the HR snapshot table. The shape has three variants, distinguished by what's in ""source"":
  - **Filter-only** (no ""source.manager""): ""source.filter"" is applied directly against the whole HR snapshot table — no hierarchy scoping, just a SQL WHERE clause over every row.
  - **Manager + unbounded depth** (""source.manager.id"" present, no ""depth""): a recursive CTE rooted at that employee, walking the management chain down indefinitely; ""source.filter"" is then applied to the result.
  - **Manager + depth cap** (""source.manager.id"" and ""source.manager.depth"" both present): same recursive CTE, but the outer query also enforces ""Depth <= [depth]"", limiting the scope to the manager + N levels of direct/indirect reports.
- A **PlaceMembership** source: includes or excludes users returned by a Microsoft Graph query with applied filters.
- A **GroupOwnership** source: includes or excludes owners of a specified Entra ID group.
- A **TeamsChannelMembership** source: includes or excludes members of a Microsoft Teams channel.

Each source part may be **inclusionary** (members are added) or **exclusionary** (members are removed from the final result).

When explaining, use the most specific explanation that fits the data. Write for the group owner (not an engineer): use ""membership rule"" instead of ""SqlMembership source"", ""source group"" instead of ""GroupMembership source"", ""the rule"" or ""the criteria"" instead of ""the inclusionary filter"". Keep literal filter expressions in backticks and manager IDs / depth values verbatim — those are the actual identifiers the owner needs.

OMIT from your sentence (already shown in the row the owner expanded):
- Added count, removed count, before-sync count, after-sync count
- Run timestamp, Run ID, ADF Run ID
Focus on the CAUSE. Owners can see the numbers in the row; they expanded it to learn WHY.

The CAUSE for this run comes from exactly two places in the prompt:
1. The ""What changed:"" section under ""Configuration history"" (configuration deltas — scope changed, filter changed, source added/removed/role-flipped, etc.).
2. The ""HR attribute changes"" section (per-user HR snapshot deltas with concrete old/new values).
The ""Job filter:"" line and the ""Source group display names"" / ""Manager display names"" reference tables are GLOSSARY material describing what currently exists in the query — they are NOT a list of changes for this run. NEVER attribute this run's delta to source groups, manager scopes, filter clauses, or HR attributes that do not appear in one of those two sections. Do NOT add ""Additionally..."", ""In addition..."", ""The sync also reflects..."", ""members were added from..."", or ""excluded based on..."" clauses about parts that did not change — even if those parts appear in the current query or in the reference tables.

When multiple changes appear in the configuration diff (e.g. scope change AND filter change AND new source), list them neutrally. Do NOT use ""primarily"", ""mainly"", ""mostly due to"", ""largely because of"" to rank one change as the primary cause unless the prompt provides explicit per-rule attribution (a ""Per-rule attribution for added users"" section is present). When that attribution section IS present, you may use those qualitative terms verbatim (e.g. ""most"", ""almost all"", ""a few"") to rank causes — but never translate them into exact counts or percentages.

Prefer these patterns:

**HARD RULE — stale config change:** If the ""Configuration history"" section contains the line ""(this change occurred BEFORE the previous run — configuration is unchanged for THIS sync..."", you MUST NOT use Patterns 1, 3, or 7 (change-based patterns). You MUST NOT invent, reconstruct, or infer a ""previous filter"" or ""previous scope"" — those values are NOT in the prompt. You MUST NOT quote the ""Last configuration change on [date]"" line as if the change is new for THIS sync. Do not say ""changed on [date]"", ""updated on [date]"", ""updated from ... to ..."". The configuration is steady-state for this sync. Pick a data-based pattern (2, 4, 5, 8, 9, 10) or fall back to pattern 6.

1. **HR filter criteria changed**: ""This sync reflects an updated membership filter (changed on [date] from [old] to [new])."" Only usable when the ""Configuration history"" section contains an explicit ""Previous configuration:"" line AND a ""Current configuration:"" line AND a ""What changed:"" line — never when the stale marker is present. Do NOT use set-theory verbs like ""expanded"", ""shrunk"", ""broadened"", ""narrowed"", ""widened"" unless the new filter's set is a proven superset or subset of the old one. When the change is a shift (e.g. one bound moved, or one predicate replaced another), say ""changed"" or ""updated"". If you must describe the *direction* of the change, only use ""more restrictive"" / ""less restrictive"" when one filter is a clear subset of the other (single inequality bound tightened/loosened on the same column with the same operator family).
2. **HR attributes changed (no config change)**: ""This sync reflects changes in HR data: some [added|removed] users had [attribute] change (e.g., [old] -> [new]), matching the membership rule."" NEVER use this pattern unless the prompt's ""HR attribute changes"" section contains actual change entries with concrete attribute names and old/new values. If that section reads ""(no HR cross-snapshot diff available)"" you MUST NOT claim any user's attribute value changed, MUST NOT invent a count, and MUST NOT name an attribute as the reason for the delta. A change to *which attribute the filter tests* (e.g. filter went from PayScaleStockLevelNbr to SupervisorInd) is NOT a change to that attribute's value — it's a Pattern 1 config change.
3. **New exclusionary part added**: ""This sync reflects a new exclusion rule added on [date] that excludes [criteria]."" Only usable when the ""Configuration history"" section shows an explicit ""What changed:"" line naming the new exclusionary part — never when the stale marker is present.
4. **Threshold blocked**: ""This sync proposed changes that were blocked because the change exceeded the configured threshold."" This applies BOTH when Status is ""ThresholdExceeded"" (job hit the disable cap and was paused) AND when Status is ""Idle"" with the counts line marked ""(blocked by threshold; ThresholdViolations X -> Y)"" — that's an early violation where the proposed delta was blocked even though the job is still under the disable cap. In the Idle-with-incremented-violations case, do NOT say ""no changes were applied because the filter returned nothing"" — the filter returned candidates, the threshold blocked them.
5. **Group sources changed (no in-GMM signal)**: ""This sync reflects changes in upstream source group memberships.""
6. **Insufficient signal**: ""The specific reason could not be determined from the available data.""
7. **Manager scope changed**: ""This sync's candidate population changed because the membership rule's scope was updated on [date] from [old scope] to [new scope]"" — where ""scope"" comes from the configDiff line and looks like ""id=N (depth<=M)"", ""id=N (unbounded depth)"", or ""none (filter-only, no hierarchy scope)"". Only usable when the ""Configuration history"" section shows an explicit ""What changed:"" line naming the scope change — never when the stale marker is present.
8. **Empty membership rule result**: ""This sync made no membership changes because the inclusionary membership rule matched no employees in the HR snapshot for this run."" Tailor the sentence to the variant: for filter-only, mention only the filter; for manager+unbounded, mention the manager root and the filter; for manager+depth-cap, mention the manager root, the depth cap (""depth<=N""), and the filter. Examples:
   - Filter-only: ""...filtered to [filter]...""
   - Manager+unbounded: ""...scoped to the management chain rooted at id=[manager.id] (unbounded depth) and filtered to [filter]...""
   - Manager+depth-cap: ""...scoped to the management chain rooted at id=[manager.id] (depth<=[depth]) and filtered to [filter]...""
9. **Per-rule attribution available**: When the prompt contains a ""Per-rule attribution for added users"" section, use those qualitative terms verbatim (e.g., ""most added users match the inclusionary HR rule scoped to id=100 (unbounded depth)""). Pair this with whichever change pattern (1, 7, etc.) is appropriate. NEVER translate ""most"" / ""almost all"" / ""a few"" into specific counts or percentages — the buckets are qualitative on purpose to avoid fabricated precision.
10. **IgnoreThresholdOnce applied**: When the Configuration history section contains an explicit ""IgnoreThresholdOnce activated on [date]"" line, that's the direct cause of this sync's delta: the previous run was blocked by the configured threshold, an owner (or automation) activated IgnoreThresholdOnce, and this sync applied the previously-pending changes. Use pattern: ""This sync applied the [adds|removes|adds and removes] that were previously blocked by the threshold, because IgnoreThresholdOnce was activated on [date]."" NEVER use this pattern unless the explicit ""IgnoreThresholdOnce activated on [date]"" line is present in the prompt — the marker is emitted only when the event was activated in THIS sync's window; otherwise, the ITO event is stale and MUST NOT be cited (even if the historical event is technically still visible elsewhere). Combine with pattern 1 / 7 phrasing when a rule change also drove the previously-pending delta (e.g., ""...applied the removes that were previously blocked, following the earlier filter change from `[old]` to `[new]`"").

When the membership rule returns no users (UsersAdded and UsersRemoved are both 0 AND the run status is MembershipDataNotFound or similar), prefer pattern 8 over saying ""HR data was unavailable"" — the HR table itself exists; what's empty is the result for this specific scope+filter combination.

Reference specific dates, attribute names, filter expressions, source group names (or IDs when no display name is available), and manager display names (or IDs when no display name is available) when available — but NOT counts (those are in the row already). When a ""Source group display names"" or ""Manager display names"" table is provided in the prompt, use ONLY names from that table — never invent or guess a manager's name if the table is empty or missing the id. When a manager scope inline is just an ID with no name (e.g., ""id=100 (unbounded depth)""), keep it as-is — do NOT synthesize a plausible-sounding name.
Use only the provided data. Output 1-2 sentences only.";

        private readonly ILogger<GetRunExplanationHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly IOpenAIService _openAIService;

        public GetRunExplanationHandler(
            ILogger<GetRunExplanationHandler> logger,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobHistoryRepository syncJobHistoryRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            IGraphGroupRepository graphGroupRepository,
            IBlobStorageRepository blobStorageRepository,
            ISqlMembershipRepository sqlMembershipRepository,
            IDataFactoryRepository dataFactoryRepository,
            IOpenAIService openAIService) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
        }

        protected override async Task<GetRunExplanationResponse> ExecuteCoreAsync(GetRunExplanationRequest request)
        {
            var response = new GetRunExplanationResponse();

            try
            {
                var syncJob = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
                if (syncJob == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

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

                // Prevent cross-job run-id disclosure: the run must belong to the requested sync job.
                if (runHistory.SyncJobId != request.SyncJobId)
                {
                    _logger.LogWarning("Cross-job run access attempt: RunId {RunId} belongs to SyncJob {ActualSyncJobId} but was requested against SyncJob {RequestedSyncJobId}.",
                        request.RunId, runHistory.SyncJobId, request.SyncJobId);
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

                // Resolve the query active AT THE TIME OF THIS RUN, not the current syncJob.Query — prevents historical runs being explained against the wrong filter.
                var asOfRunQuery = await GetAsOfRunQueryAsync(request.SyncJobId, runHistory) ?? syncJob.Query;
                var parts = ParseQueryParts(asOfRunQuery) ?? new List<QueryPartInfo>();
                var hasConfigChangeInWindow = await HasConfigChangeInWindowAsync(request.SyncJobId, runHistory);

                // Detect threshold-blocked runs by comparing ThresholdViolations counters — the disable-cap Idle path leaves Status=Idle with NULL adds/removes.
                var previousRun = await GetPreviousRunHistoryAsync(request.SyncJobId, runHistory);
                var prevThresholdViolations = previousRun?.ThresholdViolations ?? 0;
                var thisThresholdViolations = runHistory.ThresholdViolations ?? 0;
                var isThresholdBlocked = thisThresholdViolations > prevThresholdViolations
                    || string.Equals(runHistory.Status, "ThresholdExceeded", StringComparison.OrdinalIgnoreCase);

                // "Informative" statuses (MembershipDataNotFound, NestedGroupsFound, Error, etc.) always get explained; only Idle/Completed/Successful/Unknown are true no-ops.
                var isInformativeStatus = !string.IsNullOrEmpty(runHistory.Status)
                    && !string.Equals(runHistory.Status, "Idle", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(runHistory.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(runHistory.Status, "Successful", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(runHistory.Status, "Unknown", StringComparison.OrdinalIgnoreCase);

                // Skip Path A: no delta, no recent change, not blocked, plain status. Returns a friendly line so the owner sees an affirmative signal.
                var usersAdded = runHistory.UsersAdded ?? 0;
                var usersRemoved = runHistory.UsersRemoved ?? 0;
                if (usersAdded == 0 && usersRemoved == 0 && !hasConfigChangeInWindow && !isThresholdBlocked && !isInformativeStatus)
                {
                    response.Explanation = NoMembershipChanges;
                    response.StatusCode = HttpStatusCode.OK;
                    return response;
                }

                // Skip Path B: query has no SqlMembership parts + no recent config change + NOT threshold-blocked + NOT an informative status -> in-GMM data has no signal.
                var hasSqlPart = parts.Any(p => string.Equals(p.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase));
                if (!hasSqlPart && !hasConfigChangeInWindow && !isThresholdBlocked && !isInformativeStatus)
                {
                    response.Explanation = NotEnoughInformation;
                    response.StatusCode = HttpStatusCode.OK;
                    return response;
                }

                // Fire aggregated blob find + per-part file catalog in parallel; only enumerate parts when a group-family inclusionary part exists.
                var targetGroupId = syncJob.TargetOfficeGroupId.ToString();
                var hasInclusionaryGroupFamilyPart = parts.Any(p => !p.Exclusionary && IsGroupFamilyAttributionType(p.Type));
                Task<Dictionary<string, BlobResult>>? partFilesTask = hasInclusionaryGroupFamilyPart
                    ? _blobStorageRepository.FindPartFilesByRunIdAsync(targetGroupId, request.RunId.ToString())
                    : null;

                var (added, removed) = await ReadMembershipDeltaAsync(targetGroupId, request.RunId);
                var (cappedAdded, cappedRemoved) = CapAt150(added, removed);

                // Two EF queries against the same _readContext — MUST run sequentially (DbContext is not thread-safe).
                var configRunTime = runHistory.EndTime ?? runHistory.StartTime ?? runHistory.UpdatedAt;
                var recentChanges = await _syncJobChangeRepository.GetRecentConfigChangesBySyncJobIdAsync(
                    request.SyncJobId, configRunTime, count: 2);
                var recentIgnoreThresholdOnce = await _syncJobChangeRepository.GetRecentIgnoreThresholdOnceEventsAsync(
                    request.SyncJobId, configRunTime, count: 2);

                var previousQueryForNames = recentChanges != null && recentChanges.Count > 1
                    ? ExtractQueryFromChangeDetails(recentChanges[1].ChangeDetails)
                    : null;
                var previousPartsForNames = ParseQueryParts(previousQueryForNames) ?? new List<QueryPartInfo>();

                // Was IgnoreThresholdOnce activated between the previous run and this sync? If so
                // that's the CAUSE for this sync (see Pattern 4b in SystemPrompt).
                var windowStartForItO = previousRun?.UpdatedAt ?? DateTime.MinValue;
                var itoInWindow = recentIgnoreThresholdOnce != null
                    && recentIgnoreThresholdOnce.Count > 0
                    && recentIgnoreThresholdOnce[0].ChangeTime > windowStartForItO;
                var itoEvent = itoInWindow ? recentIgnoreThresholdOnce![0] : null;

                var groupGuids = CollectGroupSourceGuids(parts, previousPartsForNames);
                var groupNames = await ResolveGroupNamesSafelyAsync(groupGuids);

                var managerIds = CollectManagerIds(parts, previousPartsForNames);
                var managerNames = await ResolveManagerNamesSafelyAsync(managerIds, runHistory.AdfRunId);

                var configDiff = BuildConfigurationDiff(recentChanges, groupNames, managerNames, hasConfigChangeInWindow, itoEvent, previousRun?.Status);
                var hrDiff = await ComputeHrDiffSummaryAsync(parts, cappedAdded, cappedRemoved, runHistory.AdfRunId, previousRun?.AdfRunId);

                // Await the pre-fired part-files enumeration (already running concurrently).
                IReadOnlyDictionary<string, BlobResult> partFiles = new Dictionary<string, BlobResult>(StringComparer.OrdinalIgnoreCase);
                if (partFilesTask != null)
                {
                    try
                    {
                        partFiles = await partFilesTask;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enumerate per-part membership blobs for group {GroupId} run {RunId}; group-family attribution will be skipped.", targetGroupId, request.RunId);
                    }
                }

                // Per-part attribution for added users. Silent skip on any missing data.
                var addsAttribution = await ComputeAddsAttributionAsync(
                    parts, added, runHistory.AdfRunId, managerNames, groupNames, partFiles);

                var userPrompt = BuildRunPrompt(request, runHistory, asOfRunQuery, cappedAdded, cappedRemoved, added.Count, removed.Count, isThresholdBlocked, prevThresholdViolations, thisThresholdViolations, configDiff, hrDiff, groupNames, managerNames, addsAttribution);

                string explanation;
                using (_logger.BeginScope(new Dictionary<string, object> { ["AIFeature"] = "RunExplanation" }))
                {
                    explanation = await _openAIService.GetCompletionAsync(SystemPrompt, userPrompt);
                }

                response.Explanation = string.IsNullOrWhiteSpace(explanation) ? FallbackExplanation : explanation.Trim();
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "OpenAI call timed out for SyncJob {SyncJobId}, Run {RunId}", request.SyncJobId, request.RunId);
                response.Explanation = FallbackExplanation;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex) when (IsOpenAiRateLimited(ex))
            {
                // OpenAIService rethrows RequestFailedException(429) as a plain Exception with "(HTTP 429)" after Polly retries — degrade to fallback string.
                _logger.LogWarning(ex, "OpenAI rate limit (429) for SyncJob {SyncJobId}, Run {RunId} after retries", request.SyncJobId, request.RunId);
                response.Explanation = FallbackExplanation;
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate run explanation for SyncJob {SyncJobId}, Run {RunId}", request.SyncJobId, request.RunId);
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }

        private static bool IsOpenAiRateLimited(Exception ex)
        {
            if (ex == null) return false;
            var msg = ex.Message ?? string.Empty;
            return msg.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase);
        }

        // Returns the Query in effect at the time of this run (most recent Onboarding/Update SyncJobChange at-or-before EndTime), or null for legacy jobs.
        private async Task<string?> GetAsOfRunQueryAsync(Guid syncJobId, Models.SyncJobHistory.SyncJobHistory runHistory)
        {
            try
            {
                var asOf = runHistory.EndTime ?? runHistory.StartTime ?? runHistory.UpdatedAt;
                var recentChanges = await _syncJobChangeRepository.GetRecentConfigChangesBySyncJobIdAsync(syncJobId, asOf, count: 1);
                if (recentChanges == null || recentChanges.Count == 0)
                {
                    return null;
                }
                return ExtractQueryFromChangeDetails(recentChanges[0].ChangeDetails);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve as-of-run query for SyncJob {SyncJobId}, falling back to current query", syncJobId);
                return null;
            }
        }

        private async Task<bool> HasConfigChangeInWindowAsync(Guid syncJobId, Models.SyncJobHistory.SyncJobHistory runHistory)
        {
            var asOf = runHistory.EndTime ?? runHistory.StartTime ?? runHistory.UpdatedAt;
            var recentChanges = await _syncJobChangeRepository.GetRecentConfigChangesBySyncJobIdAsync(syncJobId, asOf, count: 1);
            if (recentChanges == null || recentChanges.Count == 0)
            {
                return false;
            }

            var previousRun = await GetPreviousRunHistoryAsync(syncJobId, runHistory);
            var windowStart = previousRun?.UpdatedAt ?? DateTime.MinValue;
            return recentChanges[0].ChangeTime > windowStart;
        }

        // Most recent history row strictly before this run's UpdatedAt — window baseline for stale-config, ITO, and threshold-violation delta.
        // Pages through history (100/page, up to 20 pages) with an early exit; older runs need more pages than newer ones.
        private async Task<Models.SyncJobHistory.SyncJobHistory?> GetPreviousRunHistoryAsync(Guid syncJobId, Models.SyncJobHistory.SyncJobHistory runHistory)
        {
            const int pageSize = 100;
            const int maxPages = 20;

            for (int page = 1; page <= maxPages; page++)
            {
                var historyPage = await _syncJobHistoryRepository.GetBySyncJobIdAsync(syncJobId, pageSize: pageSize, pageNumber: page);
                if (historyPage == null || historyPage.Count == 0) return null;

                var candidate = historyPage
                    .Where(h => h.RunId != runHistory.RunId && h.UpdatedAt < runHistory.UpdatedAt)
                    .OrderByDescending(h => h.UpdatedAt)
                    .FirstOrDefault();
                if (candidate != null) return candidate;

                // Once we page past a row older than the current run without finding a match, there is no previous run.
                if (historyPage.Any(h => h.UpdatedAt < runHistory.UpdatedAt)) return null;
            }
            return null;
        }

        public const int MaxUsersPerRunExplanation = 150;

        private async Task<(List<Guid> Added, List<Guid> Removed)> ReadMembershipDeltaAsync(string targetGroupId, Guid runId)
        {
            var empty = (new List<Guid>(), new List<Guid>());

            var blobResult = await _blobStorageRepository.FindAggregatedFileByRunIdAsync(targetGroupId, runId.ToString());
            if (blobResult.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(blobResult.Path))
            {
                return empty;
            }

            var fileContent = await _blobStorageRepository.DownloadFileAsync(blobResult.Path);
            if (fileContent.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(fileContent.Content))
            {
                return empty;
            }

            var json = TryDecompress(fileContent.Content);
            if (string.IsNullOrWhiteSpace(json))
            {
                return empty;
            }

            return ParseMembershipDelta(json);
        }

        // Forward-only Utf8JsonReader parse; ref struct prohibits use inside async, so this lives in a sync helper.
        public static (List<Guid> Added, List<Guid> Removed) ParseMembershipDelta(string json)
        {
            var added = new List<Guid>();
            var removed = new List<Guid>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return (added, removed);
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    return (added, removed);
                }

                // Scan root-level properties for "SourceMembers"
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return (added, removed);
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName &&
                        (reader.ValueTextEquals("SourceMembers"u8) || reader.ValueTextEquals("sourceMembers"u8)))
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        reader.Read();
                        reader.TrySkip();
                    }
                }

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                {
                    return (added, removed);
                }

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.TrySkip();
                        continue;
                    }

                    Guid? objectId = null;
                    MembershipAction? action = null;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;

                        if (reader.ValueTextEquals("ObjectId"u8) || reader.ValueTextEquals("objectId"u8))
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String &&
                                Guid.TryParse(reader.GetString(), out var id))
                            {
                                objectId = id;
                            }
                        }
                        else if (reader.ValueTextEquals("MembershipAction"u8) || reader.ValueTextEquals("membershipAction"u8))
                        {
                            if (reader.Read())
                            {
                                if (reader.TokenType == JsonTokenType.Number &&
                                    reader.TryGetInt32(out var actionInt) &&
                                    Enum.IsDefined(typeof(MembershipAction), actionInt))
                                {
                                    action = (MembershipAction)actionInt;
                                }
                                else if (reader.TokenType == JsonTokenType.String &&
                                         Enum.TryParse<MembershipAction>(reader.GetString(), true, out var actionEnum))
                                {
                                    action = actionEnum;
                                }
                            }
                        }
                        else
                        {
                            reader.Read();
                            reader.TrySkip();
                        }
                    }

                    if (objectId.HasValue && action.HasValue)
                    {
                        if (action.Value == MembershipAction.Add) added.Add(objectId.Value);
                        else if (action.Value == MembershipAction.Remove) removed.Add(objectId.Value);
                    }
                }
            }
            catch (JsonException) { /* fall through with whatever we got */ }
            catch (InvalidOperationException) { /* fall through with whatever we got */ }

            return (added, removed);
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

        // Cap combined users at MaxUsersPerRunExplanation, biased toward keeping both sides represented
        // when one side dominates. If total <= cap, returns inputs unchanged.
        public static (List<Guid> Added, List<Guid> Removed) CapAt150(IReadOnlyList<Guid> added, IReadOnlyList<Guid> removed)
        {
            int total = added.Count + removed.Count;
            if (total <= MaxUsersPerRunExplanation)
            {
                return (added.ToList(), removed.ToList());
            }

            int half = MaxUsersPerRunExplanation / 2;
            int addedTake;
            int removedTake;

            if (added.Count <= half)
            {
                addedTake = added.Count;
                removedTake = MaxUsersPerRunExplanation - addedTake;
            }
            else if (removed.Count <= half)
            {
                removedTake = removed.Count;
                addedTake = MaxUsersPerRunExplanation - removedTake;
            }
            else
            {
                addedTake = half;
                removedTake = MaxUsersPerRunExplanation - addedTake;
            }

            return (added.Take(addedTake).ToList(), removed.Take(removedTake).ToList());
        }

        // Per-run HR cross-snapshot diff: compares affected users' HR attributes across this-run and previous-run ADF tables, attributing changes to specific parts. Empty string when no signal.
        private async Task<string> ComputeHrDiffSummaryAsync(
            IReadOnlyList<QueryPartInfo> parts,
            IReadOnlyList<Guid> addedUsers,
            IReadOnlyList<Guid> removedUsers,
            Guid? currentAdfRunId,
            Guid? previousAdfRunId)
        {
            if (currentAdfRunId == null || currentAdfRunId.Value == Guid.Empty)
            {
                return string.Empty;
            }

            var sqlParts = parts
                .Where(p => string.Equals(p.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Filter))
                .ToList();
            if (sqlParts.Count == 0)
            {
                return string.Empty;
            }

            var addedIds = addedUsers.Select(g => g.ToString()).ToList();
            var removedIds = removedUsers.Select(g => g.ToString()).ToList();
            var allIds = addedIds.Concat(removedIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allIds.Count == 0)
            {
                return string.Empty;
            }

            var addedSet = new HashSet<string>(addedIds, StringComparer.OrdinalIgnoreCase);

            // A useful cross-snapshot diff requires previous-run AdfRunId to exist AND differ from current — skip both the table probe and batch load on same-snapshot runs.
            var hasDiffablePrevious = previousAdfRunId.HasValue
                && previousAdfRunId.Value != Guid.Empty
                && previousAdfRunId.Value != currentAdfRunId.Value;
            if (!hasDiffablePrevious)
            {
                return string.Empty;
            }

            // Current-run table is required; bail gracefully if it's gone.
            var currentTable = currentAdfRunId.Value.ToString().Replace("-", string.Empty);
            try
            {
                if (!await _sqlMembershipRepository.CheckIfTableExistsAsync(currentTable))
                {
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify ADF table {Table} for HR diff", currentTable);
                return string.Empty;
            }

            Dictionary<string, Dictionary<string, string>> currentAttrs;
            try
            {
                currentAttrs = await _sqlMembershipRepository.GetUserAttributesBatchAsync(allIds, currentTable);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to batch-load HR attributes from {Table}", currentTable);
                return string.Empty;
            }

            // Previous-run table is optional; if missing or unreadable, skip the cross-snapshot diff.
            Dictionary<string, Dictionary<string, string>>? previousAttrs = null;
            {
                var previousTable = previousAdfRunId!.Value.ToString().Replace("-", string.Empty);
                try
                {
                    if (await _sqlMembershipRepository.CheckIfTableExistsAsync(previousTable))
                    {
                        previousAttrs = await _sqlMembershipRepository.GetUserAttributesBatchAsync(allIds, previousTable);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load previous-run HR attributes from {Table}; continuing without diff", previousTable);
                }
            }

            if (previousAttrs == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var part in sqlParts)
            {
                var attributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ExtractAttributeNamesFromSqlFilter(part.Filter!, attributeNames);
                if (attributeNames.Count == 0)
                {
                    continue;
                }

                var role = part.Exclusionary ? "exclusionary" : "inclusionary";

                foreach (var attrName in attributeNames)
                {
                    int addedChanged = 0;
                    int removedChanged = 0;
                    string? sampleOld = null;
                    string? sampleNew = null;

                    foreach (var userId in allIds)
                    {
                        previousAttrs.TryGetValue(userId, out var prev);
                        currentAttrs.TryGetValue(userId, out var cur);

                        var oldVal = LookupAttrValue(prev, attrName);
                        var newVal = LookupAttrValue(cur, attrName);
                        if (oldVal == null || newVal == null) continue;
                        if (string.Equals(oldVal, newVal, StringComparison.OrdinalIgnoreCase)) continue;

                        if (addedSet.Contains(userId)) addedChanged++;
                        else removedChanged++;

                        sampleOld ??= oldVal;
                        sampleNew ??= newVal;
                    }

                    if (addedChanged == 0 && removedChanged == 0) continue;

                    sb.Append($"- Part {part.Index} ({role} SQL filter `{part.Filter}`): ");
                    if (addedChanged > 0)
                    {
                        sb.Append($"{addedChanged} added users had {attrName} change");
                    }
                    if (removedChanged > 0)
                    {
                        if (addedChanged > 0) sb.Append(" and ");
                        sb.Append($"{removedChanged} removed users had {attrName} change");
                    }
                    if (sampleOld != null && sampleNew != null)
                    {
                        sb.Append($" (sample: {attrName} {sampleOld} -> {sampleNew})");
                    }
                    sb.AppendLine();
                }
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : string.Empty;
        }

        // Look up an attribute by exact name or by its "_Code" variant (HR snapshots often store both).
        private static string? LookupAttrValue(Dictionary<string, string>? attrs, string attrName)
        {
            if (attrs == null) return null;
            if (attrs.TryGetValue(attrName, out var v)) return v;
            if (attrs.TryGetValue($"{attrName}_Code", out var cv)) return cv;
            return null;
        }

        // Inlined from GetSyncExplanationHandler.ExtractAttributeNamesFromSqlFilter for now.
        public static void ExtractAttributeNamesFromSqlFilter(string filter, HashSet<string> attributeNames)
        {
            var operators = new[] { "=", "<>", ">=", "<=", ">", "<", " IN ", " NOT IN ", " LIKE ", " NOT LIKE " };
            var logicalOps = new[] { " AND ", " OR " };

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

        // Builds the "Configuration history" prompt section. hasConfigChangeInWindow gates the structural diff;
        // ignoreThresholdOnceInWindow is populated when an ITO event fired between the previous run and this sync.
        private string BuildConfigurationDiff(
            IReadOnlyList<Models.SyncJobChange.SyncJobChange>? recentChanges,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<int, string>? managerNames,
            bool hasConfigChangeInWindow,
            Models.SyncJobChange.SyncJobChange? ignoreThresholdOnceInWindow,
            string? previousRunStatus)
        {
            try
            {
                var sb = new StringBuilder();

                if (recentChanges == null || recentChanges.Count == 0)
                {
                    sb.AppendLine("No configuration changes found.");
                }
                else
                {
                    var currentChange = recentChanges[0];
                    var currentQuery = ExtractQueryFromChangeDetails(currentChange.ChangeDetails);
                    var previousQuery = recentChanges.Count > 1
                        ? ExtractQueryFromChangeDetails(recentChanges[1].ChangeDetails)
                        : null;

                    sb.AppendLine($"Last configuration change on {currentChange.ChangeTime:yyyy-MM-dd} by {currentChange.ChangedByDisplayName ?? "system"} ({currentChange.ChangeReason}):");

                    if (!hasConfigChangeInWindow)
                    {
                        sb.AppendLine($"(this change occurred BEFORE the previous run — configuration is unchanged for THIS sync; do not attribute this run's adds/removes to a configuration change)");
                        sb.AppendLine($"Configuration as of this run: {currentQuery ?? "None"}");
                    }
                    else if (previousQuery != null && !string.Equals(NormalizeQuery(previousQuery), NormalizeQuery(currentQuery), StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"Previous configuration: {previousQuery}");
                        sb.AppendLine($"Current configuration: {currentQuery ?? "None"}");
                        var structuralDiff = DescribeQueryDiff(previousQuery, currentQuery, groupNames, managerNames);
                        if (!string.IsNullOrWhiteSpace(structuralDiff))
                        {
                            sb.AppendLine();
                            sb.AppendLine("What changed:");
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
                }

                // Emit ITO line regardless of query-change presence — the two are independent causes. Pair with previous-run status.
                if (ignoreThresholdOnceInWindow != null)
                {
                    sb.AppendLine();
                    var prevStatusNote = string.IsNullOrEmpty(previousRunStatus)
                        ? ""
                        : $" (previous run status: {previousRunStatus})";
                    sb.AppendLine($"IgnoreThresholdOnce activated on {ignoreThresholdOnceInWindow.ChangeTime:yyyy-MM-dd HH:mm} by {ignoreThresholdOnceInWindow.ChangedByDisplayName ?? "system"}{prevStatusNote} — proposed changes that were previously blocked by the threshold were allowed to apply for THIS sync.");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build configuration diff from pre-fetched changes.");
                return "Unable to retrieve configuration history.";
            }
        }

        private static string BuildRunPrompt(
            GetRunExplanationRequest request,
            Models.SyncJobHistory.SyncJobHistory runHistory,
            string? query,
            IReadOnlyList<Guid> sampleAdded,
            IReadOnlyList<Guid> sampleRemoved,
            int fullAddedFromBlob,
            int fullRemovedFromBlob,
            bool isThresholdBlocked,
            int prevThresholdViolations,
            int thisThresholdViolations,
            string configDiff,
            string hrDiffSummary,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<int, string>? managerNames,
            string addsAttribution)
        {
            var endTime = runHistory.EndTime ?? runHistory.StartTime ?? runHistory.UpdatedAt;
            var status = runHistory.Status ?? "Unknown";
            // Fall back to blob counts when DB columns are NULL — critical for early-violation Idle runs (ThresholdViolations++, UsersAdded/Removed NULL).
            var addedCount = runHistory.UsersAdded ?? fullAddedFromBlob;
            var removedCount = runHistory.UsersRemoved ?? fullRemovedFromBlob;

            var countsLine = isThresholdBlocked
                ? $"Proposed users added: {addedCount} | Proposed users removed: {removedCount} (blocked by threshold; ThresholdViolations {prevThresholdViolations} -> {thisThresholdViolations})"
                : $"Users added: {addedCount} | Users removed: {removedCount}";

            var hrSection = string.IsNullOrWhiteSpace(hrDiffSummary)
                ? "(no HR cross-snapshot diff available)"
                : hrDiffSummary;

            // Optional reference tables: only emitted when at least one entry was resolved; lets the AI swap friendly names for GUIDs.
            var groupNamesSection = BuildGroupNameReferenceTable(groupNames);
            var managerNamesSection = BuildManagerNameReferenceTable(managerNames);

            return $@"Sync Run: {request.RunId}
Date: {endTime:u}
Status: {status}
Before sync: {runHistory.BeforeSyncUserCount ?? 0} members | After sync: {runHistory.AfterSyncUserCount ?? 0} members
{countsLine}

Job filter: {query ?? "None"}

Sampled adds: {sampleAdded.Count} of {addedCount} included
Sampled removes: {sampleRemoved.Count} of {removedCount} included

HR attribute changes (between previous-run and this-run snapshots, grouped by part):
{hrSection}

Configuration history:
{configDiff}{addsAttribution}{groupNamesSection}{managerNamesSection}";
        }

        // Inlined from GetSyncExplanationHandler for now; see plan.md follow-up to extract into a shared helper.
        private static string? ExtractQueryFromChangeDetails(string? changeDetails)
        {
            if (string.IsNullOrWhiteSpace(changeDetails))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(changeDetails);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                if (document.RootElement.TryGetProperty("query", out var camel) && camel.ValueKind == JsonValueKind.String)
                {
                    return camel.GetString();
                }
                if (document.RootElement.TryGetProperty("Query", out var pascal) && pascal.ValueKind == JsonValueKind.String)
                {
                    return pascal.GetString();
                }
            }
            catch (JsonException) { }
            return null;
        }

        private static string? NormalizeQuery(string? query)
        {
            return string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        }

        // Inlined from GetSyncExplanationHandler for now; see plan.md follow-up to extract into a shared helper.
        private static string DescribeQueryDiff(
            string? previousQuery,
            string? currentQuery,
            IReadOnlyDictionary<Guid, string>? groupNames = null,
            IReadOnlyDictionary<int, string>? managerNames = null)
        {
            var previousParts = ParseQueryParts(previousQuery);
            var currentParts = ParseQueryParts(currentQuery);

            if (previousParts == null && currentParts == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            var previousByKey = (previousParts ?? new List<QueryPartInfo>()).ToDictionary(p => p.Key, p => p);
            var currentByKey = (currentParts ?? new List<QueryPartInfo>()).ToDictionary(p => p.Key, p => p);

            // Parts added
            foreach (var kvp in currentByKey)
            {
                if (previousByKey.ContainsKey(kvp.Key)) continue;
                var part = kvp.Value;
                var role = part.Exclusionary ? "exclusionary" : "inclusionary";
                if (part.Type.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- New {role} group source added: {FormatGroupRef(part.Source, groupNames)}");
                }
                else if (part.Type.Equals("SqlMembership", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- New {role} HR/SQL filter source added: {part.Filter ?? "no filter"}");
                }
                else
                {
                    sb.AppendLine($"- New {role} source added (type: {part.Type}): {FormatGroupRef(part.Source, groupNames)}");
                }
            }

            // Parts removed
            foreach (var kvp in previousByKey)
            {
                if (currentByKey.ContainsKey(kvp.Key)) continue;
                var part = kvp.Value;
                var role = part.Exclusionary ? "exclusionary" : "inclusionary";
                var roleLabel = char.ToUpperInvariant(role[0]) + role.Substring(1);
                if (part.Type.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- {roleLabel} group source removed: {FormatGroupRef(part.Source, groupNames)}");
                }
                else if (part.Type.Equals("SqlMembership", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- {roleLabel} HR/SQL filter source removed: {part.Filter ?? "no filter"}");
                }
                else
                {
                    sb.AppendLine($"- {roleLabel} source removed (type: {part.Type}): {FormatGroupRef(part.Source, groupNames)}");
                }
            }

            // Parts modified
            foreach (var kvp in currentByKey)
            {
                if (!previousByKey.TryGetValue(kvp.Key, out var prevPart)) continue;
                var currPart = kvp.Value;

                if (prevPart.Exclusionary != currPart.Exclusionary)
                {
                    var oldRole = prevPart.Exclusionary ? "exclusionary" : "inclusionary";
                    var newRole = currPart.Exclusionary ? "exclusionary" : "inclusionary";
                    // Format the source as a friendly group ref when present; otherwise fall back to
                    // the filter expression / type label so the diff line still identifies the part.
                    var formattedSource = string.IsNullOrEmpty(currPart.Source) ? null : FormatGroupRef(currPart.Source, groupNames);
                    sb.AppendLine($"- Source {formattedSource ?? currPart.Filter ?? currPart.Type} changed from {oldRole} to {newRole}");
                }

                if (!string.Equals(prevPart.Filter?.Trim(), currPart.Filter?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- Filter changed from \"{prevPart.Filter ?? "none"}\" to \"{currPart.Filter ?? "none"}\"");
                }

                if (!string.Equals(prevPart.ManagerId, currPart.ManagerId, StringComparison.Ordinal)
                    || prevPart.ManagerDepth != currPart.ManagerDepth)
                {
                    sb.AppendLine($"- Manager scope (SqlMembership recursive-CTE root) changed from {FormatManagerScopeWithName(prevPart, managerNames)} to {FormatManagerScopeWithName(currPart, managerNames)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        // Inlined from GetSyncExplanationHandler for now; see plan.md follow-up to extract into a shared helper.
        public static List<QueryPartInfo>? ParseQueryParts(string? query)
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
                    part.Type = GetStringProperty(element, "type") ?? "Unknown";
                    part.Source = GetStringProperty(element, "source");
                    part.Filter = GetStringProperty(element, "filter");

                    // SqlMembership-style parts have an object "source" with nested "filter" and "manager":{"id":N}; dig in so the CTE root and filter aren't silently dropped.
                    if (element.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.Object)
                    {
                        var nestedFilter = GetStringProperty(srcEl, "filter");
                        if (!string.IsNullOrWhiteSpace(nestedFilter))
                            part.Filter = nestedFilter;

                        if (srcEl.TryGetProperty("manager", out var mgrEl)
                            && mgrEl.ValueKind == JsonValueKind.Object
                            && mgrEl.TryGetProperty("id", out var idEl))
                        {
                            part.ManagerId = idEl.ValueKind switch
                            {
                                JsonValueKind.String => idEl.GetString(),
                                JsonValueKind.Number => idEl.GetRawText(),
                                _ => null
                            };

                    // Optional recursion-depth cap; when omitted the recursive CTE is unbounded, otherwise "Depth <= N" is added to the outer WHERE.
                    if (mgrEl.TryGetProperty("depth", out var depthEl))
                            {
                                part.ManagerDepth = depthEl.ValueKind switch
                                {
                                    JsonValueKind.Number when depthEl.TryGetInt32(out var d) => d,
                                    JsonValueKind.String when int.TryParse(depthEl.GetString(), out var d) => d,
                                    _ => null
                                };
                            }
                        }
                    }

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

        private static string? GetStringProperty(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(propertyName, out var prop)) return null;
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
        }

        // Source-group display-name resolution: batched Graph lookup for GroupMembership / GroupOwnership /
        // TeamsChannelMembership source GUIDs. Silent fallback to raw GUIDs on any failure.

        // Collects unique non-empty source GUIDs across query parts, filtering to group-referencing types.
        public static HashSet<Guid> CollectGroupSourceGuids(params IReadOnlyList<QueryPartInfo>?[] partLists)
        {
            var result = new HashSet<Guid>();
            if (partLists == null) return result;
            foreach (var parts in partLists)
            {
                if (parts == null) continue;
                foreach (var part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part.Source)) continue;
                    if (!IsGroupReferencingPartType(part.Type)) continue;
                    if (Guid.TryParse(part.Source, out var guid) && guid != Guid.Empty)
                    {
                        result.Add(guid);
                    }
                }
            }
            return result;
        }

        // Whitelist of part types whose "source" is an Entra ID group object id (excludes SqlMembership and PlaceMembership).
        private static bool IsGroupReferencingPartType(string? type)
        {
            return string.Equals(type, "GroupMembership", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "GroupOwnership", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "TeamsChannelMembership", StringComparison.OrdinalIgnoreCase);
        }

        // Single-batch Graph lookup with hard cap and silent fallback; empty cache means "render raw GUIDs" (same as pre-Phase-7).
        private async Task<IReadOnlyDictionary<Guid, string>> ResolveGroupNamesSafelyAsync(IReadOnlyCollection<Guid> guids)
        {
            var empty = (IReadOnlyDictionary<Guid, string>)new Dictionary<Guid, string>(0);
            if (guids == null || guids.Count == 0) return empty;

            var toResolve = guids
                .Where(g => g != Guid.Empty)
                .Take(MaxGroupNamesToResolve)
                .ToList();
            if (toResolve.Count == 0) return empty;

            try
            {
                var raw = await _graphGroupRepository.GetGroupNamesAsync(toResolve);
                if (raw == null || raw.Count == 0) return empty;

                // Drop entries Graph couldn't resolve (deleted group, no access). A missing entry
                // is treated as "no name available" by FormatGroupRef — identical to a Graph failure.
                var resolved = new Dictionary<Guid, string>(raw.Count);
                foreach (var kvp in raw)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        resolved[kvp.Key] = kvp.Value;
                    }
                }
                return resolved;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve source-group display names; AI prompt will fall back to raw GUIDs.");
                return empty;
            }
        }

        // Renders a source GUID as "'Display Name' (guid)" when a name is cached, otherwise passes through unchanged.
        public static string FormatGroupRef(string? rawSource, IReadOnlyDictionary<Guid, string>? nameCache)
        {
            if (string.IsNullOrEmpty(rawSource)) return rawSource ?? string.Empty;
            if (nameCache != null
                && Guid.TryParse(rawSource, out var guid)
                && nameCache.TryGetValue(guid, out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                return $"'{name}' ({rawSource})";
            }
            return rawSource;
        }

        // Builds the optional "Source group display names" prompt section. Returns an empty string
        // when no names were resolved, so the prompt stays unchanged in the fallback path.
        public static string BuildGroupNameReferenceTable(IReadOnlyDictionary<Guid, string>? groupNames)
        {
            if (groupNames == null || groupNames.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Source group display names (prefer these in your output over the raw GUIDs):");
            foreach (var kvp in groupNames.OrderBy(k => k.Value, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {kvp.Key}: \"{kvp.Value}\"");
            }
            return sb.ToString().TrimEnd();
        }

        // Manager-scope display-name resolution: SqlMembership parts reference an HR employee number, so we do a two-hop lookup
        // (SQL for AzureObjectId, then Graph for DisplayName). Sequential per manager id; silent fallback to id-only on any failure.

        // Collects unique non-empty positive manager IDs from parsed query part lists.
        public static HashSet<int> CollectManagerIds(params IReadOnlyList<QueryPartInfo>?[] partLists)
        {
            var result = new HashSet<int>();
            if (partLists == null) return result;
            foreach (var parts in partLists)
            {
                if (parts == null) continue;
                foreach (var part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part.ManagerId)) continue;
                    if (int.TryParse(part.ManagerId, out var id) && id > 0)
                    {
                        result.Add(id);
                    }
                }
            }
            return result;
        }

        // SQL+Graph resolution with hard cap and silent fallback (empty cache → id-only formatting everywhere).
        // Two-tier: first try the run's historical ADF snapshot, then fall back to the latest for unresolved employees (display names are essentially static).
        private async Task<IReadOnlyDictionary<int, string>> ResolveManagerNamesSafelyAsync(
            IReadOnlyCollection<int> managerIds,
            Guid? runAdfRunId)
        {
            var empty = (IReadOnlyDictionary<int, string>)new Dictionary<int, string>(0);
            if (managerIds == null || managerIds.Count == 0) return empty;

            var toResolve = managerIds.Where(id => id > 0).Take(MaxManagerNamesToResolve).ToList();
            if (toResolve.Count == 0) return empty;

            var resolved = new Dictionary<int, string>(toResolve.Count);

            // Tier 1: run's historical ADF snapshot.
            if (runAdfRunId.HasValue && runAdfRunId.Value != Guid.Empty)
            {
                var historicalTable = runAdfRunId.Value.ToString().Replace("-", string.Empty);
                await TryResolveManagerNamesFromTableAsync(toResolve, historicalTable, resolved, tier: "historical");
            }

            // Tier 2: latest ADF snapshot for any employees still unresolved.
            var stillMissing = toResolve.Where(id => !resolved.ContainsKey(id)).ToList();
            if (stillMissing.Count > 0)
            {
                string? latestAdfRunId = null;
                try
                {
                    latestAdfRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch latest ADF run id for manager-name fallback.");
                }
                if (!string.IsNullOrWhiteSpace(latestAdfRunId))
                {
                    var latestTable = latestAdfRunId.Replace("-", string.Empty);
                    // Only bother if the fallback table differs from the tier-1 table.
                    var historicalTable = runAdfRunId.HasValue ? runAdfRunId.Value.ToString().Replace("-", string.Empty) : null;
                    if (!string.Equals(latestTable, historicalTable, StringComparison.OrdinalIgnoreCase))
                    {
                        await TryResolveManagerNamesFromTableAsync(stillMissing, latestTable, resolved, tier: "latest");
                    }
                }
            }

            return resolved;
        }

        // Populates `resolved` with EmployeeId -> DisplayName pairs sourced from `tableName`; skips ids already resolved so tier 1 wins over tier 2.
        private async Task TryResolveManagerNamesFromTableAsync(
            IReadOnlyList<int> managerIds,
            string tableName,
            Dictionary<int, string> resolved,
            string tier)
        {
            try
            {
                if (!await _sqlMembershipRepository.CheckIfTableExistsAsync(tableName))
                {
                    _logger.LogInformation("Manager-name resolution ({Tier}): table {Table} does not exist; skipping tier.", tier, tableName);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to probe ADF table {Table} ({Tier}) for manager-name resolution.", tableName, tier);
                return;
            }

            foreach (var id in managerIds)
            {
                if (resolved.ContainsKey(id)) continue;
                try
                {
                    var (_, azureObjectId) = await _sqlMembershipRepository.GetOrgLeaderAsync(id, tableName);
                    if (string.IsNullOrWhiteSpace(azureObjectId))
                    {
                        _logger.LogInformation("Manager-name resolution ({Tier}): id={ManagerId} skipped — empty AzureObjectId in {Table}.", tier, id, tableName);
                        continue;
                    }

                    var user = await _graphGroupRepository.GetUserByUpnOrIdAsync(azureObjectId, includeMailProperty: false);
                    if (user == null)
                    {
                        _logger.LogInformation("Manager-name resolution ({Tier}): id={ManagerId} (aoid={ObjectId}) skipped — Graph returned null user.", tier, id, azureObjectId);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(user.DisplayName))
                    {
                        _logger.LogInformation("Manager-name resolution ({Tier}): id={ManagerId} (aoid={ObjectId}) skipped — empty DisplayName.", tier, id, azureObjectId);
                        continue;
                    }

                    _logger.LogInformation("Manager-name resolution ({Tier}): id={ManagerId} -> '{DisplayName}' (aoid={ObjectId}).", tier, id, user.DisplayName, azureObjectId);
                    resolved[id] = user.DisplayName;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve display name for manager id {ManagerId} ({Tier}); falling back to ID.", id, tier);
                }
            }
        }

        // Wraps QueryPartInfo.FormatManagerScope() to swap the manager's employee ID for their display name when available.
        // Unresolved IDs pass through unchanged so callers always have a usable identifier.
        public static string FormatManagerScopeWithName(QueryPartInfo part, IReadOnlyDictionary<int, string>? managerNames)
        {
            var baseScope = part.FormatManagerScope();
            if (managerNames == null || managerNames.Count == 0) return baseScope;
            if (!int.TryParse(part.ManagerId, out var id)) return baseScope;
            if (!managerNames.TryGetValue(id, out var name) || string.IsNullOrWhiteSpace(name)) return baseScope;

            // base looks like "id=N (depth<=M)" / "id=N (unbounded depth)". Replace the "id=N "
            // prefix with "'Name' " so we get "'Name' (depth<=M)" / "'Name' (unbounded depth)".
            var openParen = baseScope.IndexOf('(');
            if (openParen < 0) return baseScope;
            return $"'{name}' {baseScope.Substring(openParen)}";
        }

        // Builds the optional "Manager display names" prompt section. Returns an empty string
        // when no names were resolved, so the prompt stays unchanged in the fallback path.
        public static string BuildManagerNameReferenceTable(IReadOnlyDictionary<int, string>? managerNames)
        {
            if (managerNames == null || managerNames.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Manager display names (prefer these in your output over the raw employee IDs):");
            foreach (var kvp in managerNames.OrderBy(k => k.Value, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- id={kvp.Key}: \"{kvp.Value}\"");
            }
            return sb.ToString().TrimEnd();
        }

        // Per-part attribution for added users: SqlMembership parts re-run the recursive CTE; group-family parts read the per-part blob.
        // Missing data silently skips that line — never guesses. Caller pre-fetches partFiles to overlap the two blob enumerations.
        private async Task<string> ComputeAddsAttributionAsync(
            IReadOnlyList<QueryPartInfo> parts,
            IReadOnlyCollection<Guid> addedUsers,
            Guid? runAdfRunId,
            IReadOnlyDictionary<int, string>? managerNames,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<string, BlobResult> partFiles)
        {
            if (addedUsers == null || addedUsers.Count == 0) return string.Empty;

            var inclusionaryParts = (parts ?? new List<QueryPartInfo>())
                .Where(p => !p.Exclusionary && IsAttributionCandidate(p.Type))
                .ToList();
            if (inclusionaryParts.Count == 0) return string.Empty;

            var sqlParts = inclusionaryParts
                .Where(p => string.Equals(p.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase))
                .Take(MaxSqlRulesToIntersect)
                .ToList();
            var groupFamilyParts = inclusionaryParts
                .Where(p => IsGroupFamilyAttributionType(p.Type))
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (sqlParts.Count == 0 && groupFamilyParts.Count == 0) return string.Empty;

            var addedSet = new HashSet<string>(
                addedUsers.Take(MaxAddsForRuleAttribution).Select(g => g.ToString()),
                StringComparer.OrdinalIgnoreCase);
            var totalSampled = addedSet.Count;
            if (totalSampled == 0) return string.Empty;

            // Probe ADF table once for all SQL parts (partFiles was already fetched by caller).
            string? sqlTableName = null;
            var sqlAvailable = false;
            if (sqlParts.Count > 0 && runAdfRunId.HasValue && runAdfRunId.Value != Guid.Empty)
            {
                sqlTableName = runAdfRunId.Value.ToString().Replace("-", string.Empty);
                try
                {
                    sqlAvailable = await _sqlMembershipRepository.CheckIfTableExistsAsync(sqlTableName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to probe ADF table {Table} for per-rule attribution; SQL parts will be skipped.", sqlTableName);
                    sqlAvailable = false;
                }
            }

            // SQL parts first, then group parts, each in query-index order.
            var orderedParts = sqlParts.Concat(groupFamilyParts).ToList();
            var tasks = orderedParts.Select(part =>
            {
                if (string.Equals(part.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase))
                {
                    return sqlAvailable && sqlTableName != null
                        ? AttributeSqlPartForAddsAsync(part, addedSet, totalSampled, sqlTableName, managerNames)
                        : Task.FromResult<string?>(null);
                }
                return AttributeGroupFamilyPartForAddsAsync(part, addedSet, totalSampled, partFiles, groupNames);
            });
            var results = await Task.WhenAll(tasks);
            var lines = results.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();
            if (lines.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Per-part attribution for added users (qualitative buckets — use these terms verbatim in your output; DO NOT translate them to exact counts or percentages):");
            foreach (var line in lines)
            {
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        private static bool IsAttributionCandidate(string? type)
        {
            return string.Equals(type, "SqlMembership", StringComparison.OrdinalIgnoreCase)
                || IsGroupFamilyAttributionType(type);
        }

        private static bool IsGroupFamilyAttributionType(string? type)
        {
            return string.Equals(type, "GroupMembership", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "GroupOwnership", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "TeamsChannelMembership", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "PlaceMembership", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> AttributeSqlPartForAddsAsync(
            QueryPartInfo part,
            HashSet<string> addedSet,
            int totalSampled,
            string tableName,
            IReadOnlyDictionary<int, string>? managerNames)
        {
            try
            {
                List<SqlMembershipObtainer.Entities.PersonEntity>? ruleMembers = null;
                if (int.TryParse(part.ManagerId, out var mgrId) && mgrId > 0)
                {
                    // Manager-scoped variant: recursive CTE rooted at the manager. Pass depth=0
                    // to mean unbounded (matches GetChildEntitiesAsync's contract).
                    ruleMembers = await _sqlMembershipRepository.GetChildEntitiesAsync(
                        part.Filter, mgrId, tableName, part.ManagerDepth ?? 0);
                }
                else if (!string.IsNullOrWhiteSpace(part.Filter))
                {
                    // Filter-only variant: plain WHERE clause over the whole HR table.
                    ruleMembers = await _sqlMembershipRepository.FilterChildEntitiesAsync(
                        part.Filter, tableName);
                }
                if (ruleMembers == null || ruleMembers.Count == 0)
                {
                    // Empty rule output — Pattern 8 ("Empty membership rule result") in the prompt
                    // already covers this case; don't emit an attribution line.
                    return null;
                }

                var matched = ruleMembers.Count(m =>
                    !string.IsNullOrWhiteSpace(m.AzureObjectId)
                    && addedSet.Contains(m.AzureObjectId));
                var bucket = BucketAttribution(matched, totalSampled);
                var label = FormatSqlPartLabel(part, managerNames);
                return $"- {label}: {bucket} of the added users match this rule";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute SQL attribution for part #{Index}; skipping that part.", part.Index);
                return null;
            }
        }

        // Reads the per-part blob and returns an attribution line, or null if missing/unreadable/empty (silent skip — losing one line beats a false attribution).
        private async Task<string?> AttributeGroupFamilyPartForAddsAsync(
            QueryPartInfo part,
            HashSet<string> addedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            // Blob suffix convention: 1-based part index. Verified against LP file listings and
            // OrchestratorFunction's `CurrentPart <= 0` guard which expects >= 1.
            var tag = $"{part.Type}_{part.Index + 1}";
            if (!partFiles.TryGetValue(tag, out var blob) || blob.BlobStatus != BlobStatus.Found)
            {
                return null;
            }

            HashSet<Guid> sourceMembers;
            try
            {
                sourceMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(blob.Path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read per-part blob {Path} for part #{Index} ({Type}); skipping that part.", blob.Path, part.Index, part.Type);
                return null;
            }
            if (sourceMembers == null || sourceMembers.Count == 0)
            {
                return null;
            }

            var matched = 0;
            foreach (var g in sourceMembers)
            {
                if (addedSet.Contains(g.ToString())) matched++;
            }
            var bucket = BucketAttribution(matched, totalSampled);
            var label = FormatGroupFamilyPartLabel(part, groupNames);
            return $"- {label}: {bucket} of the added users came from this source";
        }

        // Qualitative bucketing for attribution ratios. The buckets are deliberately wide so the
        // AI can quote them verbatim without inviting precision-fabrication ("most" vs "73%").
        public static string BucketAttribution(int matched, int total)
        {
            if (total <= 0 || matched == 0) return "none";
            if (matched >= total) return "all";

            var ratio = (double)matched / total;
            if (ratio >= 0.85) return "almost all";
            if (ratio >= 0.60) return "most";
            if (ratio >= 0.40) return "about half";
            if (ratio >= 0.15) return "some";
            return "a few";
        }

        // Owner-facing label for an inclusionary SqlMembership part in the attribution section (rule # + scope only, no filter expression).
        public static string FormatSqlPartLabel(QueryPartInfo part, IReadOnlyDictionary<int, string>? managerNames)
        {
            if (string.IsNullOrEmpty(part.ManagerId))
            {
                return $"Inclusionary HR rule #{part.Index + 1} (filter-only)";
            }
            var scope = FormatManagerScopeWithName(part, managerNames);
            return $"Inclusionary HR rule #{part.Index + 1} (scope={scope})";
        }

        // Owner-facing label for a GroupMembership-family part; uses resolved display name when available, falls back to raw source GUID.
        public static string FormatGroupFamilyPartLabel(QueryPartInfo part, IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var sourceLabel = FormatGroupRef(part.Source, groupNames);
            return $"Inclusionary {part.Type} part #{part.Index + 1} ({sourceLabel})";
        }

        public class QueryPartInfo
        {
            public int Index { get; set; }
            public string Type { get; set; } = "Unknown";
            public string? Source { get; set; }
            public string? Filter { get; set; }
            public string? ManagerId { get; set; }
            public int? ManagerDepth { get; set; }
            public bool Exclusionary { get; set; }

            public string Key => !string.IsNullOrEmpty(Source)
                ? $"{Type}|{Source}"
                : $"{Type}|{Index}";

            public string FormatManagerScope() =>
                ManagerId == null ? "none (filter-only, no hierarchy scope)"
                : ManagerDepth.HasValue ? $"id={ManagerId} (depth<={ManagerDepth})"
                : $"id={ManagerId} (unbounded depth)";
        }
    }
}
