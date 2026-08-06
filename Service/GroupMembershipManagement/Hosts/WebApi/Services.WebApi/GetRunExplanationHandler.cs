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
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services
{
    public class GetRunExplanationHandler : RequestHandlerBase<GetRunExplanationRequest, GetRunExplanationResponse>
    {
        public const string FallbackExplanation = "The specific reason could not be determined from the available data.";
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

When explaining, use the most specific explanation that fits the data. Write for the group owner (not an engineer): use ""membership rule"" instead of ""SqlMembership source"", ""source group"" instead of ""GroupMembership source"", ""the rule"" or ""the criteria"" instead of ""the inclusionary filter"".

**HARD RULE — owner-friendly criteria only:** The prompt's membership criteria have already been translated into plain English. Use those descriptions instead of raw implementation syntax. NEVER output raw SQL/JSON filters, comparison symbols, internal code-field suffixes, or numeric mapping codes when a mapped description is available. Use readable attribute labels, describe comparisons in words, and preserve exact mapped descriptions supplied in the prompt.

OMIT from your sentence (already shown in the row the owner expanded):
- Added count, removed count, before-sync count, after-sync count
- Run timestamp, Run ID, ADF Run ID
Focus on the CAUSE. Owners can see the numbers in the row; they expanded it to learn WHY.

The CAUSE for this run comes from exactly two places in the prompt:
1. The ""What changed:"" section under ""Configuration history"" (configuration deltas — scope changed, filter changed, source added/removed/role-flipped, etc.).
2. The ""HR attribute changes"" section (per-user HR snapshot deltas with concrete old/new values).
The ""Membership rules as of this run:"" section and the ""Source group display names"" / ""Manager display names"" reference tables are GLOSSARY material describing what currently exists in the query — they are NOT a list of changes for this run. **EXCEPTION — first recorded run:** when the prompt contains a ""Run sequence: This is the FIRST recorded run for this job"" line, there is NO prior state to compare against, so the membership rules THEMSELVES are the cause for this run — describe the initial population using their owner-friendly descriptions (see pattern 13) and do NOT hedge. NEVER attribute this run's delta to source groups, manager scopes, filter clauses, or HR attributes that do not appear in one of those two sections. Do NOT add ""Additionally..."", ""In addition..."", ""The sync also reflects..."", ""members were added from..."", or ""excluded based on..."" clauses about parts that did not change — even if those parts appear in the current query or in the reference tables.

When multiple changes appear in the configuration diff (e.g. scope change AND filter change AND new source), list them neutrally. Do NOT use ""primarily"", ""mainly"", ""mostly due to"", ""largely because of"" to rank one change as the primary cause unless the prompt provides explicit per-rule attribution (a ""Per-rule attribution for added users"" section is present). When that attribution section IS present, you may use those qualitative terms verbatim (e.g. ""most"", ""almost all"", ""a few"") to rank causes — but never translate them into exact counts or percentages.

Prefer these patterns:

**HARD RULE — stale config change:** If the ""Configuration history"" section contains the line ""(this change occurred BEFORE the previous run — configuration is unchanged for THIS sync..."", you MUST NOT use Patterns 1, 3, or 7 (change-based patterns). You MUST NOT invent, reconstruct, or infer a ""previous filter"" or ""previous scope"" — those values are NOT in the prompt. You MUST NOT quote the ""Last configuration change on [date]"" line as if the change is new for THIS sync. Do not say ""changed on [date]"", ""updated on [date]"", ""updated from ... to ..."". The configuration is steady-state for this sync. Pick a data-based pattern (2, 4, 5, 8, 9, 10) or fall back to pattern 6.

1. **HR filter criteria changed**: ""This sync reflects an updated membership filter (changed on [date] from [old] to [new])."" Only usable when the ""Configuration history"" section contains an explicit ""Previous configuration:"" line AND a ""Current configuration:"" line AND a ""What changed:"" line — never when the stale marker is present. Do NOT use set-theory verbs like ""expanded"", ""shrunk"", ""broadened"", ""narrowed"", ""widened"" unless the new filter's set is a proven superset or subset of the old one. When the change is a shift (e.g. one bound moved, or one predicate replaced another), say ""changed"" or ""updated"". If you must describe the *direction* of the change, only use ""more restrictive"" / ""less restrictive"" when one filter is a clear subset of the other (single inequality bound tightened/loosened on the same column with the same operator family).
2. **HR attributes changed (no config change)**: ""This sync reflects changes in HR data: some [added|removed] users had [attribute] change (e.g., [old] -> [new]), matching the membership rule."" NEVER use this pattern unless the prompt's ""HR attribute changes"" section contains actual change entries with concrete attribute names and old/new values. If that section reads ""(no HR cross-snapshot diff available)"" you MUST NOT claim any user's attribute value changed, MUST NOT invent a count, and MUST NOT name an attribute as the reason for the delta. A change to *which attribute the filter tests* (e.g. filter went from one HR attribute to a different HR attribute) is NOT a change to that attribute's value — it's a Pattern 1 config change.
3. **New exclusionary part added**: ""This sync reflects a new exclusion rule added on [date] that excludes [criteria]."" Only usable when the ""Configuration history"" section shows an explicit ""What changed:"" line naming the new exclusionary part — never when the stale marker is present.
4. **Threshold blocked**: ""This sync proposed changes that were blocked because the change exceeded the configured threshold."" This applies BOTH when Status is ""ThresholdExceeded"" (job hit the disable cap and was paused) AND when Status is ""Idle"" with the counts line marked ""(blocked by threshold; ThresholdViolations X -> Y)"" — that's an early violation where the proposed delta was blocked even though the job is still under the disable cap. In the Idle-with-incremented-violations case, do NOT say ""no changes were applied because the filter returned nothing"" — the filter returned candidates, the threshold blocked them. When a ""Per-part attribution for removed users"" or ""for added users"" section is also present, cite the specific rule(s) from those sections (e.g., ""...blocked, and most of the proposed removals came from users leaving the source group `X`""). Do NOT invent a per-side cause (e.g., ""blocked because the removals exceeded the threshold"") unless the attribution section actually shows that split — the threshold check considers the total delta.
5. **Group sources changed (no in-GMM signal)**: ""This sync reflects changes in upstream source group memberships.""
6. **Insufficient signal**: ""The specific reason could not be determined from the available data.""
7. **Manager scope changed**: ""This sync's candidate population changed because the membership rule's scope was updated on [date] from [old scope] to [new scope]"" — where ""scope"" comes from the configDiff line and looks like ""id=N (depth<=M)"", ""id=N (unbounded depth)"", or ""none (filter-only, no hierarchy scope)"". Only usable when the ""Configuration history"" section shows an explicit ""What changed:"" line naming the scope change — never when the stale marker is present.
8. **Empty membership rule result**: ""This sync made no membership changes because the inclusionary membership rule matched no employees in the HR snapshot for this run."" Tailor the sentence to the variant: for filter-only, mention only the owner-friendly criteria; for manager+unbounded, mention the manager root and the criteria; for manager+depth-cap, mention the manager root, the depth cap, and the criteria. Never reconstruct the raw filter.
9. **Per-rule attribution available**: When the prompt contains a ""Per-rule attribution for added users"" section, use those qualitative terms verbatim (e.g., ""most added users match the inclusionary HR rule scoped to id=100 (unbounded depth)""). Pair this with whichever change pattern (1, 7, etc.) is appropriate. NEVER translate ""most"" / ""almost all"" / ""a few"" into specific counts or percentages — the buckets are qualitative on purpose to avoid fabricated precision.
10. **IgnoreThresholdOnce applied**: When the Configuration history section contains an explicit ""IgnoreThresholdOnce activated on [date]"" line, that's the direct cause of this sync's delta: the previous run was blocked by the configured threshold, an owner (or automation) activated IgnoreThresholdOnce, and this sync applied the previously-pending changes. Use pattern: ""This sync applied the [adds|removes|adds and removes] that were previously blocked by the threshold, because IgnoreThresholdOnce was activated on [date]."" NEVER use this pattern unless the explicit ""IgnoreThresholdOnce activated on [date]"" line is present in the prompt — the marker is emitted only when the event was activated in THIS sync's window; otherwise, the ITO event is stale and MUST NOT be cited (even if the historical event is technically still visible elsewhere). Combine with pattern 1 / 7 phrasing when a rule change also drove the previously-pending delta, using the owner-friendly old and new criteria from the prompt.
11. **Per-part attribution for removed users available**: When the prompt contains a ""Per-part attribution for removed users"" section, use those qualitative terms verbatim to explain the removals (e.g., ""most removed users left the source group `TestGroupMember`"", or ""a few removed users no longer match the inclusionary HR rule"", or — when a source was dropped by a recent config update — ""all of the removed users were previously sourced from the group `X` which was removed from the query""). When the attribution line for a deleted source says ""per-part membership counts are unavailable, but this deleted source is the likely cause"", phrase it as: ""This sync's removals likely came from users who were previously sourced from the group `X`, which was removed from the query in a recent config update."" When the attribution line indicates a source ""flipped from inclusionary to exclusionary"", phrase it as: ""This sync's removals came from users who are members of `X`, which was recently flipped from an inclusionary source to an exclusionary source in the query."" When the attribution line indicates a ""New exclusionary source added"", phrase it as: ""This sync's removals came from users who are members of `X`, which was recently added to the query as an exclusionary source."" **When multiple ""Source removed from query"" attribution lines are present, you MUST name EVERY deleted source in your output — not just the first one. Combine them naturally: ""...from the groups `X` and `Y`"" for two, or ""...from the groups `X`, `Y`, and `Z`"" for three or more. Under no circumstances omit any deleted source that is cited in the attribution section.** Same rule applies to ""Source flipped"" and ""New exclusionary source added"" attribution lines — name EVERY cited source. **Under no circumstances name a source that is NOT cited in the attribution section.** If exactly one source is cited, name exactly that one source and do not add a second name to make the sentence plural. Prefer specific attribution over generic phrasing like ""the specific reason could not be determined"". Pair with pattern 4 for threshold-blocked runs (e.g., ""...blocked by the threshold. All of the proposed removals were previously sourced from the groups `X` and `Y`, both of which were removed from the query.""). If the section is absent and there are removed users, either omit any per-removal explanation or fall back to pattern 5 (""upstream source group changes"") — NEVER invent an attribution. **CRITICAL anti-hallucination rule**: NEVER name a specific source group, HR rule, or exclusionary source as the cause of removals unless it is cited in the ""Per-part attribution for removed users"" section OR the ""Configuration history"" section shows an explicit ""What changed"" line involving that source in this window. The current query listed under ""Configuration as of this run"" is NOT proof of attribution — a source being listed as CURRENT does not mean users were removed FROM it. Removals typically come from sources that WERE in the query previously but are NO LONGER in the query, from users who no longer match the current sources' criteria, or from sources whose role flipped from inclusionary to exclusionary, or from newly-added exclusionary sources — do not conflate these cases. **BAD example — process-of-elimination hallucination**: given ""Per-part attribution for added users: all of the added users match source group `X`"" and NO removes attribution section (or a ""(no attributable source found)"" removes marker), it is FORBIDDEN to output ""all removals came from users leaving `Y`"" just because `Y` is the OTHER source in the current query. That reasoning is process-of-elimination guessing, not evidence — the correct output is to describe removals without naming any specific source (pattern 4 without a per-removal source, or pattern 5, or pattern 6). Same rule applies in reverse for adds — never mirror the removes attribution shape onto adds when the adds attribution section is empty or marked ""(no attributable source found)"". **BAD example — inventing a second name to make a sentence plural**: given a single attribution line like ""- Source removed from query (previously group `X`): all of the removed users were sourced from this now-deleted part"", it is FORBIDDEN to output ""...came from the groups `X` and `Y`"" — you must output ""...came from the group `X`"" (singular) since only ONE source is cited. Never pluralize by inventing a second source name from the current query or from prior conversations.
12. **Per-part attribution for added users available**: When the prompt contains a ""Per-part attribution for added users"" section, use its qualitative terms verbatim to explain the additions. In particular, when the attribution line says ""Exclusionary source removed from query in the recent config update"", phrase it as: ""This sync's additions came from users who were previously excluded by `X`, which was removed from the query in a recent config update."" When the attribution line indicates a source ""flipped from exclusionary to inclusionary"", phrase it as: ""This sync's additions came from users who are members of `X`, which was recently flipped from an exclusionary source to an inclusionary source in the query."" **When multiple attribution lines are present for adds, you MUST name EVERY cited source, combining them naturally (""...from the groups `X` and `Y`"" for two, ""...from the groups `X`, `Y`, and `Z`"" for three or more).** All the anti-hallucination rules from pattern 11 apply symmetrically to adds — never invent a second name to make a sentence plural, never process-of-elimination guess a source, never name a source that is not cited in the attribution section.
13. **First recorded run (initial population)**: When the prompt contains a ""Run sequence: This is the FIRST recorded run for this job"" line, this run established the destination group's membership for the first time. For THIS case ONLY, the ""Membership rules as of this run:"" section IS the cause — describe the initial population using those owner-friendly rule descriptions (e.g., ""This first sync populated the group from its configured rules: employees whose Cost Center is Field Sales - West, plus members of the source group `Contoso Sales Team`.""). You MUST NOT hedge with pattern 6 on a first run that added or removed members, and you MUST NOT use change-based patterns (1, 3, 7) or claim any prior configuration or membership existed — there is no previous state. When a ""Per-rule attribution"" section is also present, you may fold in its qualitative terms. Keep it to 1-2 sentences; when many rules are configured, summarize the most relevant inclusionary rules rather than listing every part, and never emit raw filter syntax or numeric codes.

When the membership rule returns no users (UsersAdded and UsersRemoved are both 0 AND the run status is MembershipDataNotFound or similar), prefer pattern 8 over saying ""HR data was unavailable"" — the HR table itself exists; what's empty is the result for this specific scope+filter combination.

Reference specific dates, owner-friendly attribute names and criteria, mapped descriptions, source group names (or IDs when no display name is available), and manager display names (or IDs when no display name is available) when available — but NOT counts (those are in the row already). When a ""Source group display names"" or ""Manager display names"" table is provided in the prompt, use ONLY names from that table — never invent or guess a manager's name if the table is empty or missing the id. When a manager scope inline is just an ID with no name (e.g., ""id=100 (unbounded depth)""), keep it as-is — do NOT synthesize a plausible-sounding name.
Use only the provided data. Output 1-2 sentences normally, or up to 3 sentences when the ""Per-part attribution"" section names two or more sources (so every cited source can be included).";

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
                // A job's first run has no prior run to diff against; used below for an accurate initial-population explanation instead of the generic hedge.
                var isInitialRun = previousRun == null;
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

                // Fire aggregated blob find + per-part file catalog (this-run and previous-run) in parallel.
                var targetGroupId = syncJob.TargetOfficeGroupId.ToString();
                var hasAttributableInclusionaryPart = parts.Any(p => IsAttributionCandidate(p.Type));
                Task<Dictionary<string, BlobResult>>? partFilesTask = hasAttributableInclusionaryPart
                    ? _blobStorageRepository.FindPartFilesByRunIdAsync(targetGroupId, request.RunId.ToString())
                    : null;
                Task<Dictionary<string, BlobResult>>? previousPartFilesTask =
                    (hasAttributableInclusionaryPart && previousRun != null && previousRun.RunId != Guid.Empty)
                        ? _blobStorageRepository.FindPartFilesByRunIdAsync(targetGroupId, previousRun.RunId.ToString())
                        : null;

                var (added, removed) = await ReadMembershipDeltaAsync(targetGroupId, request.RunId);
                var (cappedAdded, cappedRemoved) = CapAt150(added, removed);

                // Two EF queries against the same _readContext — MUST run sequentially (DbContext is not thread-safe).
                // Fix #4: use StartTime as the config cutoff whenever available — a run's behavior is governed by the config
                // in effect when it BEGAN, so any changes that happened between StartTime and EndTime must be excluded from
                // "the query for this run". Fall back to EndTime minus a small buffer when StartTime is missing, to avoid
                // capturing near-simultaneous SubmissionApproved / IgnoreThresholdOnce events that happened during finalization.
                var configRunTime = GetRunConfigCutoff(runHistory);
                var recentChanges = await _syncJobChangeRepository.GetRecentConfigChangesBySyncJobIdAsync(
                    request.SyncJobId, configRunTime, count: 10);
                var recentIgnoreThresholdOnce = await _syncJobChangeRepository.GetRecentIgnoreThresholdOnceEventsAsync(
                    request.SyncJobId, configRunTime, count: 2);

                // "Previous parts for names" = the union of every inclusionary source seen across recent past Updates
                // that had a Query different from the current one. Owners frequently drop groups in successive Updates
                // (e.g., 3-source query -> 2-source -> 1-source over multiple submits), and identical resubmits are common
                // after a threshold block. Taking the union ensures we detect ALL removed sources, not just the last one.
                var currentQueryForNames = recentChanges != null && recentChanges.Count > 0
                    ? ExtractQueryFromChangeDetails(recentChanges[0].ChangeDetails)
                    : null;
                var currentQueryNormalized = NormalizeQuery(currentQueryForNames);
                var previousPartsForNames = new List<QueryPartInfo>();
                var seenPrevSourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (recentChanges != null)
                {
                    for (int i = 1; i < recentChanges.Count; i++)
                    {
                        var candidate = ExtractQueryFromChangeDetails(recentChanges[i].ChangeDetails);
                        if (string.Equals(NormalizeQuery(candidate), currentQueryNormalized, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var candidateParts = ParseQueryParts(candidate);
                        if (candidateParts == null) continue;
                        foreach (var p in candidateParts)
                        {
                            var key = $"{p.Type}|{p.Source ?? string.Empty}|{p.ManagerId ?? string.Empty}|{p.Exclusionary}";
                            if (seenPrevSourceKeys.Add(key))
                            {
                                previousPartsForNames.Add(p);
                            }
                        }
                    }
                }

                // Fix #3: reconstruct the PREVIOUS RUN's parsed parts (with their previous-run indices) so per-part removes
                // attribution can look up previous blobs by the SAME source's previous-run index — not by the current-run index.
                // Reordering (or middle-of-list deletions) shift indices, and blob filenames are position-based, so using the
                // current index to key into previousPartFiles produces wrong-source comparisons.
                List<QueryPartInfo>? previousRunParts = null;
                var previousRunIndexByGuid = new Dictionary<Guid, int>();
                if (previousRun != null && recentChanges != null)
                {
                    var prevCutoff = GetRunConfigCutoff(previousRun);
                    var prevRunChange = recentChanges.FirstOrDefault(c => c.ChangeTime <= prevCutoff);
                    var prevRunQuery = prevRunChange != null ? ExtractQueryFromChangeDetails(prevRunChange.ChangeDetails) : null;
                    previousRunParts = ParseQueryParts(prevRunQuery);
                    if (previousRunParts != null)
                    {
                        foreach (var pp in previousRunParts)
                        {
                            if (IsGroupFamilyAttributionType(pp.Type) && Guid.TryParse(pp.Source, out var g))
                            {
                                previousRunIndexByGuid[g] = pp.Index;
                            }
                        }
                    }
                }

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

                var mappingDescriptions = await OwnerFriendlyFilterMappingResolver.ResolveAsync(
                    new[] { parts, previousPartsForNames, previousRunParts }
                        .Where(partList => partList != null)
                        .SelectMany(partList => partList!)
                        .Select(part => part.Filter),
                    runHistory.AdfRunId,
                    _sqlMembershipRepository,
                    _dataFactoryRepository,
                    _logger);
                var membershipRules = DescribeMembershipRulesForOwner(parts, mappingDescriptions, groupNames, managerNames);

                var configDiff = BuildConfigurationDiff(
                    recentChanges,
                    groupNames,
                    managerNames,
                    mappingDescriptions,
                    hasConfigChangeInWindow,
                    itoEvent,
                    previousRun?.Status);
                var hrDiff = await ComputeHrDiffSummaryAsync(
                    parts,
                    cappedAdded,
                    cappedRemoved,
                    runHistory.AdfRunId,
                    previousRun?.AdfRunId,
                    mappingDescriptions);

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

                IReadOnlyDictionary<string, BlobResult> previousPartFiles = new Dictionary<string, BlobResult>(StringComparer.OrdinalIgnoreCase);
                if (previousPartFilesTask != null)
                {
                    try
                    {
                        previousPartFiles = await previousPartFilesTask;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enumerate previous-run per-part membership blobs for group {GroupId} previous run {PreviousRunId}; removes attribution will fall back for group-family parts.", targetGroupId, previousRun?.RunId);
                    }
                }

                // Per-part attribution for added users. Silent skip on any missing data.
                var addsAttribution = await ComputeAddsAttributionAsync(
                    parts, previousPartsForNames, added, runHistory.AdfRunId, managerNames, groupNames, partFiles, previousPartFiles, previousRunIndexByGuid);

                // Per-part attribution for removed users. Requires previous run's data; silent skip on any missing data.
                var removesAttribution = await ComputeRemovesAttributionAsync(
                    parts, previousPartsForNames, removed, runHistory.AdfRunId, previousRun?.AdfRunId, managerNames, groupNames, partFiles, previousPartFiles, previousRunIndexByGuid);

                var userPrompt = BuildRunPrompt(request, runHistory, membershipRules, cappedAdded, cappedRemoved, added.Count, removed.Count, isThresholdBlocked, isInitialRun, prevThresholdViolations, thisThresholdViolations, configDiff, hrDiff, groupNames, managerNames, addsAttribution, removesAttribution);

                string explanation;
                using (_logger.BeginScope(new Dictionary<string, object> { ["AIFeature"] = "RunExplanation" }))
                {
                    explanation = await _openAIService.GetCompletionAsync(SystemPrompt, userPrompt);
                }

                response.Explanation = string.IsNullOrWhiteSpace(explanation) ? FallbackExplanation : explanation.Trim();

                // First-run safety net: replace the generic hedge with an initial-population explanation on a first run that actually moved members (blank/timeout/rate-limited paths keep FallbackExplanation).
                var initialAdded = Math.Max(usersAdded, added.Count);
                var initialRemoved = Math.Max(usersRemoved, removed.Count);
                if (isInitialRun
                    && (initialAdded > 0 || initialRemoved > 0)
                    && !string.IsNullOrWhiteSpace(explanation)
                    && string.Equals(explanation.Trim(), FallbackExplanation, StringComparison.Ordinal))
                {
                    response.Explanation = BuildInitialRunExplanation(initialAdded, initialRemoved, membershipRules);
                }

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
                // Fix #4: use StartTime-preferring cutoff so we return the config that was actually in effect when the run started.
                var asOf = GetRunConfigCutoff(runHistory);
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

        // The config in effect when a run BEGAN — not when it ended — is what governed its behavior.
        // Returns StartTime when available. When null (common for threshold-blocked / disabled runs), returns EndTime
        // minus a 30-second buffer to avoid capturing config updates that landed alongside the run's finalization.
        // Falls back to UpdatedAt only if both StartTime and EndTime are unavailable.
        private static DateTime GetRunConfigCutoff(Models.SyncJobHistory.SyncJobHistory runHistory)
        {
            if (runHistory.StartTime.HasValue) return runHistory.StartTime.Value;
            if (runHistory.EndTime.HasValue) return runHistory.EndTime.Value.AddSeconds(-30);
            return runHistory.UpdatedAt;
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
            Guid? previousAdfRunId,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> mappingDescriptions)
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

                    var friendlyFilter = OwnerFriendlyFilterFormatter.DescribeFilter(part.Filter, mappingDescriptions);
                    var friendlyAttribute = OwnerFriendlyFilterFormatter.HumanizeAttributeName(attrName);
                    var friendlyOldValue = OwnerFriendlyFilterFormatter.DescribeAttributeValue(attrName, sampleOld, mappingDescriptions);
                    var friendlyNewValue = OwnerFriendlyFilterFormatter.DescribeAttributeValue(attrName, sampleNew, mappingDescriptions);

                    sb.Append($"- Part {part.Index + 1} ({role} HR criteria: {friendlyFilter}): ");
                    if (addedChanged > 0)
                    {
                        sb.Append($"{addedChanged} added users had {friendlyAttribute} change");
                    }
                    if (removedChanged > 0)
                    {
                        if (addedChanged > 0) sb.Append(" and ");
                        sb.Append($"{removedChanged} removed users had {friendlyAttribute} change");
                    }
                    if (sampleOld != null && sampleNew != null)
                    {
                        sb.Append($" (sample: {friendlyAttribute} changed from {friendlyOldValue} to {friendlyNewValue})");
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

        public static string DescribeMembershipRulesForOwner(
            IReadOnlyList<QueryPartInfo>? parts,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<int, string>? managerNames)
        {
            if (parts == null || parts.Count == 0)
            {
                return "No membership rules configured.";
            }

            var descriptions = new List<string>(parts.Count);
            foreach (var part in parts)
            {
                var action = part.Exclusionary ? "Excludes" : "Includes";
                var description = part.Type.ToLowerInvariant() switch
                {
                    "sqlmembership" => DescribeSqlMembershipRule(part, action, mappingDescriptions, managerNames),
                    "groupmembership" => $"{action} members of source group {FormatGroupNameForOwner(part.Source, groupNames)}.",
                    "groupownership" => $"{action} owners of source group {FormatGroupNameForOwner(part.Source, groupNames)}.",
                    "teamschannelmembership" => $"{action} members of Teams channel {FormatGroupNameForOwner(part.Source, groupNames)}.",
                    "placemembership" => $"{action} users returned by the configured place criteria.",
                    _ => $"{action} users from a configured {OwnerFriendlyFilterFormatter.HumanizeAttributeName(part.Type)} source."
                };

                descriptions.Add($"- Rule {part.Index + 1}: {description}");
            }

            return string.Join(Environment.NewLine, descriptions);
        }

        private static string DescribeSqlMembershipRule(
            QueryPartInfo part,
            string action,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions,
            IReadOnlyDictionary<int, string>? managerNames)
        {
            var criteria = OwnerFriendlyFilterFormatter.DescribeFilter(part.Filter, mappingDescriptions);
            if (string.IsNullOrWhiteSpace(part.ManagerId))
            {
                return $"{action} employees where {criteria}.";
            }

            return $"{action} employees in the management chain rooted at {FormatManagerScopeWithName(part, managerNames)} where {criteria}.";
        }

        private static string FormatGroupNameForOwner(
            string? rawSource,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            if (groupNames != null
                && Guid.TryParse(rawSource, out var groupId)
                && groupNames.TryGetValue(groupId, out var groupName)
                && !string.IsNullOrWhiteSpace(groupName))
            {
                return $"\"{groupName}\"";
            }

            return string.IsNullOrWhiteSpace(rawSource)
                ? "whose name is unavailable"
                : rawSource;
        }

        // Builds the "Configuration history" prompt section. hasConfigChangeInWindow gates the structural diff;
        // ignoreThresholdOnceInWindow is populated when an ITO event fired between the previous run and this sync.
        private string BuildConfigurationDiff(
            IReadOnlyList<Models.SyncJobChange.SyncJobChange>? recentChanges,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<int, string>? managerNames,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> mappingDescriptions,
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
                    // Walk back past identical resubmits — surface the true prior configuration for the diff.
                    string? previousQuery = null;
                    var currentNormalized = NormalizeQuery(currentQuery);
                    for (int i = 1; i < recentChanges.Count; i++)
                    {
                        var candidate = ExtractQueryFromChangeDetails(recentChanges[i].ChangeDetails);
                        if (!string.Equals(NormalizeQuery(candidate), currentNormalized, StringComparison.OrdinalIgnoreCase))
                        {
                            previousQuery = candidate;
                            break;
                        }
                    }

                    sb.AppendLine($"Last configuration change on {currentChange.ChangeTime:yyyy-MM-dd} by {currentChange.ChangedByDisplayName ?? "system"} ({currentChange.ChangeReason}):");

                    if (!hasConfigChangeInWindow)
                    {
                        sb.AppendLine($"(this change occurred BEFORE the previous run — configuration is unchanged for THIS sync; do not attribute this run's adds/removes to a configuration change)");
                        sb.AppendLine("Configuration as of this run:");
                        sb.AppendLine(DescribeMembershipRulesForOwner(
                            ParseQueryParts(currentQuery),
                            mappingDescriptions,
                            groupNames,
                            managerNames));
                    }
                    else if (previousQuery != null && !string.Equals(NormalizeQuery(previousQuery), NormalizeQuery(currentQuery), StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("Previous configuration:");
                        sb.AppendLine(DescribeMembershipRulesForOwner(
                            ParseQueryParts(previousQuery),
                            mappingDescriptions,
                            groupNames,
                            managerNames));
                        sb.AppendLine("Current configuration:");
                        sb.AppendLine(DescribeMembershipRulesForOwner(
                            ParseQueryParts(currentQuery),
                            mappingDescriptions,
                            groupNames,
                            managerNames));
                        var structuralDiff = DescribeQueryDiff(previousQuery, currentQuery, groupNames, managerNames, mappingDescriptions);
                        if (!string.IsNullOrWhiteSpace(structuralDiff))
                        {
                            sb.AppendLine();
                            sb.AppendLine("What changed:");
                            sb.Append(structuralDiff);
                        }
                    }
                    else if (previousQuery != null)
                    {
                        sb.AppendLine("Configuration (unchanged):");
                        sb.AppendLine(DescribeMembershipRulesForOwner(
                            ParseQueryParts(currentQuery),
                            mappingDescriptions,
                            groupNames,
                            managerNames));
                    }
                    else
                    {
                        sb.AppendLine("Initial configuration:");
                        sb.AppendLine(DescribeMembershipRulesForOwner(
                            ParseQueryParts(currentQuery),
                            mappingDescriptions,
                            groupNames,
                            managerNames));
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
            string membershipRules,
            IReadOnlyList<Guid> sampleAdded,
            IReadOnlyList<Guid> sampleRemoved,
            int fullAddedFromBlob,
            int fullRemovedFromBlob,
            bool isThresholdBlocked,
            bool isInitialRun,
            int prevThresholdViolations,
            int thisThresholdViolations,
            string configDiff,
            string hrDiffSummary,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<int, string>? managerNames,
            string addsAttribution,
            string removesAttribution)
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

            // First-run marker: signals pattern 13 so the model narrates the initial population from the rules instead of hedging.
            var runSequenceNote = isInitialRun
                ? $"{Environment.NewLine}Run sequence: This is the FIRST recorded run for this job (initial population; there is no previous run to compare against, so per-user HR-attribute deltas and prior-membership comparisons are unavailable by design)."
                : string.Empty;

            return $@"Sync Run: {request.RunId}
Date: {endTime:u}
Status: {status}
Before sync: {runHistory.BeforeSyncUserCount ?? 0} members | After sync: {runHistory.AfterSyncUserCount ?? 0} members
{countsLine}{runSequenceNote}

Membership rules as of this run:
{membershipRules}

Sampled adds: {sampleAdded.Count} of {addedCount} included
Sampled removes: {sampleRemoved.Count} of {removedCount} included

HR attribute changes (between previous-run and this-run snapshots, grouped by part):
{hrSection}

Configuration history:
{configDiff}{addsAttribution}{removesAttribution}{groupNamesSection}{managerNamesSection}";
        }

        // Deterministic owner-friendly explanation for a job's first run; used only as a success-path safety net when the model returns the generic hedge.
        private static string BuildInitialRunExplanation(int added, int removed, string membershipRules)
        {
            var rulesInline = SummarizeRulesInline(membershipRules);
            var rulesClause = string.IsNullOrEmpty(rulesInline)
                ? "its configured membership rules"
                : $"its configured rules ({rulesInline})";

            if (added > 0 && removed > 0)
            {
                return $"This was the first sync for this job, so GMM established the destination group's membership from {rulesClause}: members matching those rules were added, and any pre-existing members of the group that did not match were removed.";
            }
            if (removed > 0)
            {
                return $"This was the first sync for this job, so GMM aligned the destination group to {rulesClause} by removing pre-existing members that did not match.";
            }
            return $"This was the first sync for this job, so GMM populated the destination group for the first time by adding the members that match {rulesClause}.";
        }

        // Compact one-line summary of the configured rules for inlining into the first-run explanation; returns empty when there are no rules or too many parts to fit one sentence.
        private static string SummarizeRulesInline(string membershipRules)
        {
            if (string.IsNullOrWhiteSpace(membershipRules))
            {
                return string.Empty;
            }

            var clauses = membershipRules
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => Regex.Replace(line, @"^-\s*Rule\s*\d+:\s*", string.Empty).Trim().TrimEnd('.'))
                .Where(clause => clause.Length > 0
                    && !clause.StartsWith("No membership rules", StringComparison.OrdinalIgnoreCase))
                .Select(clause => char.ToLowerInvariant(clause[0]) + clause.Substring(1))
                .ToList();

            // More than three parts reads as a wall of text in one sentence — fall back to the generic phrasing.
            if (clauses.Count == 0 || clauses.Count > 3)
            {
                return string.Empty;
            }

            return string.Join("; ", clauses);
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
        public static string DescribeQueryDiff(
            string? previousQuery,
            string? currentQuery,
            IReadOnlyDictionary<Guid, string>? groupNames = null,
            IReadOnlyDictionary<int, string>? managerNames = null,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions = null)
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
                    sb.AppendLine($"- New {role} HR membership rule added: {OwnerFriendlyFilterFormatter.DescribeFilter(part.Filter, mappingDescriptions)}");
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
                    sb.AppendLine($"- {roleLabel} HR membership rule removed: {OwnerFriendlyFilterFormatter.DescribeFilter(part.Filter, mappingDescriptions)}");
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
                    var friendlySource = formattedSource
                        ?? OwnerFriendlyFilterFormatter.DescribeFilter(currPart.Filter, mappingDescriptions);
                    sb.AppendLine($"- Source {friendlySource} changed from {oldRole} to {newRole}");
                }

                if (!string.Equals(prevPart.Filter?.Trim(), currPart.Filter?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var previousCriteria = OwnerFriendlyFilterFormatter.DescribeFilter(prevPart.Filter, mappingDescriptions);
                    var currentCriteria = OwnerFriendlyFilterFormatter.DescribeFilter(currPart.Filter, mappingDescriptions);
                    sb.AppendLine($"- Membership criteria changed from {previousCriteria} to {currentCriteria}");
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
            IReadOnlyList<QueryPartInfo> previousParts,
            IReadOnlyCollection<Guid> addedUsers,
            Guid? runAdfRunId,
            IReadOnlyDictionary<int, string>? managerNames,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, int> previousRunIndexByGuid)
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

            // Fix #2: deleted exclusionary group-family parts. When a source was previously excluded and is now removed
            // from the query, the users it was blocking are newly admitted → they show up as adds.
            var deletedExclLines = await AttributeDeletedExclusionaryGroupFamilyPartsForAddsAsync(parts, previousParts, addedSet, totalSampled, previousPartFiles, groupNames, previousRunIndexByGuid);
            lines.AddRange(deletedExclLines);

            // Fix #5: exclusionary → inclusionary flips. A source that was blocking users but is now including them
            // contributes adds equal to its current members that appear in the added set.
            var flippedExclToInclLines = await AttributeFlippedGroupFamilyPartsForAddsAsync(parts, previousParts, addedSet, totalSampled, partFiles, groupNames);
            lines.AddRange(flippedExclToInclLines);

            if (lines.Count == 0)
            {
                // Mirror of the removes no-attribution marker: when adds happen but no source is attributable, forbid the AI from
                // process-of-elimination guessing based on the removes attribution or the sources listed in the current query.
                var sbEmpty = new StringBuilder();
                sbEmpty.AppendLine();
                sbEmpty.AppendLine();
                sbEmpty.AppendLine("Per-part attribution for added users:");
                sbEmpty.AppendLine("- (no attributable source found — none of the sampled added users appear in the current-run per-part membership blobs, or per-part data was unavailable)");
                sbEmpty.AppendLine("- HARD RULE: do NOT name any specific source group, HR rule, or inclusionary source as the cause of adds. Do NOT infer it by process of elimination from the removes attribution or from sources listed under 'Job filter' or 'Configuration as of this run'. Use pattern 5 (upstream source changes) without naming a specific source for the adds.");
                return sbEmpty.ToString().TrimEnd();
            }

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

        // Per-part attribution for removed users. Requires previous-run data (blobs + ADF snapshot); silent skip when missing.
        private async Task<string> ComputeRemovesAttributionAsync(
            IReadOnlyList<QueryPartInfo> parts,
            IReadOnlyList<QueryPartInfo> previousParts,
            IReadOnlyCollection<Guid> removedUsers,
            Guid? runAdfRunId,
            Guid? previousAdfRunId,
            IReadOnlyDictionary<int, string>? managerNames,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, int> previousRunIndexByGuid)
        {
            if (removedUsers == null || removedUsers.Count == 0) return string.Empty;

            var candidates = (parts ?? new List<QueryPartInfo>())
                .Where(p => IsAttributionCandidate(p.Type))
                .ToList();
            if (candidates.Count == 0) return string.Empty;

            var sqlParts = candidates
                .Where(p => string.Equals(p.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase))
                .Take(MaxSqlRulesToIntersect)
                .ToList();
            var groupFamilyParts = candidates
                .Where(p => IsGroupFamilyAttributionType(p.Type))
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (sqlParts.Count == 0 && groupFamilyParts.Count == 0) return string.Empty;

            var removedSet = new HashSet<string>(
                removedUsers.Take(MaxAddsForRuleAttribution).Select(g => g.ToString()),
                StringComparer.OrdinalIgnoreCase);
            var totalSampled = removedSet.Count;
            if (totalSampled == 0) return string.Empty;

            // SQL removes need two different ADF snapshots; skip when either is missing or they're the same.
            string? currentTable = null;
            string? previousTable = null;
            var sqlAvailable = false;
            if (sqlParts.Count > 0
                && runAdfRunId.HasValue && runAdfRunId.Value != Guid.Empty
                && previousAdfRunId.HasValue && previousAdfRunId.Value != Guid.Empty
                && runAdfRunId.Value != previousAdfRunId.Value)
            {
                currentTable = runAdfRunId.Value.ToString().Replace("-", string.Empty);
                previousTable = previousAdfRunId.Value.ToString().Replace("-", string.Empty);
                try
                {
                    var currentExists = await _sqlMembershipRepository.CheckIfTableExistsAsync(currentTable);
                    var previousExists = await _sqlMembershipRepository.CheckIfTableExistsAsync(previousTable);
                    sqlAvailable = currentExists && previousExists;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to probe ADF tables ({Current} / {Previous}) for removes attribution; SQL parts will be skipped.", currentTable, previousTable);
                    sqlAvailable = false;
                }
            }

            var orderedParts = sqlParts.Concat(groupFamilyParts).ToList();
            var tasks = orderedParts.Select(part =>
            {
                if (string.Equals(part.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase))
                {
                    return sqlAvailable && currentTable != null && previousTable != null
                        ? AttributeSqlPartForRemovesAsync(part, removedSet, totalSampled, currentTable, previousTable, managerNames)
                        : Task.FromResult<string?>(null);
                }
                return AttributeGroupFamilyPartForRemovesAsync(part, removedSet, totalSampled, partFiles, previousPartFiles, groupNames, previousRunIndexByGuid);
            });
            var results = await Task.WhenAll(tasks);
            var lines = results.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();

            // Deleted inclusionary group-family parts: source dropped by a recent config update, so its previous members appear as removes.
            var deletedLines = await AttributeDeletedGroupFamilyPartsForRemovesAsync(parts, previousParts, removedSet, totalSampled, previousPartFiles, groupNames);
            lines.AddRange(deletedLines);

            // Flipped group-family parts: source was inclusionary in the previous config but is now exclusionary (or vice versa).
            // A flip from inclusionary → exclusionary causes removals of the users who were being included from that source.
            var flippedLines = await AttributeFlippedGroupFamilyPartsForRemovesAsync(parts, previousParts, removedSet, totalSampled, previousPartFiles, partFiles, groupNames, previousRunIndexByGuid);
            lines.AddRange(flippedLines);

            // Fix #1: NEW exclusionary group-family parts added to the query in the recent config update.
            // A newly-added exclusionary source causes removals for its current members that overlap with the destination.
            var newExclLines = await AttributeNewExclusionaryGroupFamilyPartsForRemovesAsync(parts, previousParts, removedSet, totalSampled, partFiles, groupNames);
            lines.AddRange(newExclLines);

            // When we sampled removed users but couldn't attribute any of them, emit an explicit no-signal marker so the AI does NOT
            // process-of-elimination guess a source from the adds attribution or from the sources listed in the current query.
            if (lines.Count == 0)
            {
                var sbEmpty = new StringBuilder();
                sbEmpty.AppendLine();
                sbEmpty.AppendLine();
                sbEmpty.AppendLine("Per-part attribution for removed users:");
                sbEmpty.AppendLine("- (no attributable source found — the sources in this run's query all show unchanged membership between the previous run and this run, and no source was recently removed from the query)");
                sbEmpty.AppendLine("- HARD RULE: do NOT name any specific source group, HR rule, or exclusionary source as the cause of removals. Do NOT infer it by process of elimination from the adds attribution or from sources listed under 'Job filter' or 'Configuration as of this run'. Use pattern 5 (upstream source changes) or pattern 4 (threshold blocked) without naming a specific source for the removals.");
                return sbEmpty.ToString().TrimEnd();
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Per-part attribution for removed users (qualitative buckets — use these terms verbatim in your output; DO NOT translate them to exact counts or percentages):");
            foreach (var line in lines)
            {
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        // SqlMembership removes: inclusionary parts flag users who dropped out; exclusionary flag users newly caught.
        private async Task<string?> AttributeSqlPartForRemovesAsync(
            QueryPartInfo part,
            HashSet<string> removedSet,
            int totalSampled,
            string currentTable,
            string previousTable,
            IReadOnlyDictionary<int, string>? managerNames)
        {
            try
            {
                var currentMembers = await FetchSqlPartMembersAsync(part, currentTable);
                var previousMembers = await FetchSqlPartMembersAsync(part, previousTable);
                if (currentMembers == null || previousMembers == null) return null;

                var currentSet = new HashSet<string>(
                    currentMembers.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!),
                    StringComparer.OrdinalIgnoreCase);
                var previousSet = new HashSet<string>(
                    previousMembers.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!),
                    StringComparer.OrdinalIgnoreCase);

                var label = FormatSqlPartLabel(part, managerNames);
                if (part.Exclusionary)
                {
                    // Newly excluded: in current, not in previous, intersected with removed.
                    var newlyExcluded = currentSet.Count(m => !previousSet.Contains(m) && removedSet.Contains(m));
                    if (newlyExcluded == 0) return null;
                    var bucket = BucketAttribution(newlyExcluded, totalSampled);
                    return $"- Exclusionary rule at part #{part.Index + 1} ({label}): {bucket} of the removed users are newly excluded by this rule";
                }
                else
                {
                    // Dropped out: in previous, not in current, intersected with removed.
                    var droppedOut = previousSet.Count(m => !currentSet.Contains(m) && removedSet.Contains(m));
                    if (droppedOut == 0) return null;
                    var bucket = BucketAttribution(droppedOut, totalSampled);
                    return $"- {label}: {bucket} of the removed users no longer match this rule";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute SQL removes attribution for part #{Index}; skipping that part.", part.Index);
                return null;
            }
        }

        private async Task<List<string?>?> FetchSqlPartMembersAsync(QueryPartInfo part, string tableName)
        {
            List<SqlMembershipObtainer.Entities.PersonEntity>? members = null;
            if (int.TryParse(part.ManagerId, out var mgrId) && mgrId > 0)
            {
                members = await _sqlMembershipRepository.GetChildEntitiesAsync(
                    part.Filter, mgrId, tableName, part.ManagerDepth ?? 0);
            }
            else if (!string.IsNullOrWhiteSpace(part.Filter))
            {
                members = await _sqlMembershipRepository.FilterChildEntitiesAsync(part.Filter, tableName);
            }
            return members?.Select(m => m.AzureObjectId).ToList();
        }

        // Group-family removes: inclusionary parts flag users who left the source; exclusionary flag users newly in the excluded source.
        private async Task<string?> AttributeGroupFamilyPartForRemovesAsync(
            QueryPartInfo part,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<Guid, int> previousRunIndexByGuid)
        {
            var currentTag = $"{part.Type}_{part.Index + 1}";
            if (!partFiles.TryGetValue(currentTag, out var currentBlob) || currentBlob.BlobStatus != BlobStatus.Found) return null;

            // Fix #3: look up the previous blob by the SOURCE GUID's index in the PREVIOUS run's query, not by the current-run index.
            // Reordering (or middle-of-list deletions) shift indices, and blob filenames are position-based; using the current
            // index against previousPartFiles would compare against the wrong source's previous membership.
            string? previousTag = null;
            if (Guid.TryParse(part.Source, out var partGuid) && previousRunIndexByGuid.TryGetValue(partGuid, out var prevIndex))
            {
                previousTag = $"{part.Type}_{prevIndex + 1}";
            }
            else
            {
                // Fall back to same-index lookup only when we lack a previous-index map for this GUID (e.g., previous query missing).
                previousTag = currentTag;
            }
            if (!previousPartFiles.TryGetValue(previousTag, out var previousBlob) || previousBlob.BlobStatus != BlobStatus.Found) return null;

            HashSet<Guid> currentMembers;
            HashSet<Guid> previousMembers;
            try
            {
                currentMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(currentBlob.Path);
                previousMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(previousBlob.Path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read per-part blobs for removes attribution on part #{Index} ({Type}); skipping that part.", part.Index, part.Type);
                return null;
            }
            if (currentMembers == null || previousMembers == null) return null;

            var label = FormatGroupFamilyPartLabel(part, groupNames);
            if (part.Exclusionary)
            {
                // Newly in the excluded source: in current, not in previous, intersected with removed.
                var newlyExcluded = 0;
                foreach (var g in currentMembers)
                {
                    if (!previousMembers.Contains(g) && removedSet.Contains(g.ToString())) newlyExcluded++;
                }
                if (newlyExcluded == 0) return null;
                var bucket = BucketAttribution(newlyExcluded, totalSampled);
                return $"- Exclusionary {part.Type} part #{part.Index + 1} ({label}): {bucket} of the removed users newly appear in this excluded source";
            }
            else
            {
                // Left the source: in previous, not in current, intersected with removed.
                var leftSource = 0;
                foreach (var g in previousMembers)
                {
                    if (!currentMembers.Contains(g) && removedSet.Contains(g.ToString())) leftSource++;
                }
                if (leftSource == 0) return null;
                var bucket = BucketAttribution(leftSource, totalSampled);
                return $"- {label}: {bucket} of the removed users left this source";
            }
        }

        // Detects group-family inclusionary parts present in the previous config but missing from the current config, and attributes their previous members that are now being removed.
        // A source that FLIPPED from inclusionary to exclusionary (still present in the query, just with the opposite role) is NOT a deletion —
        // it's a role flip. We match on SOURCE GUID (regardless of the exclusionary flag) so flipped sources are excluded from the deleted set.
        private async Task<List<string>> AttributeDeletedGroupFamilyPartsForRemovesAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartInfo> previousParts,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            if (previousParts == null || previousParts.Count == 0) return lines;

            // ALL current group-family source GUIDs (inclusionary OR exclusionary). A flipped source counts as still-present.
            var currentSources = new HashSet<Guid>(
                (currentParts ?? new List<QueryPartInfo>())
                    .Where(p => IsGroupFamilyAttributionType(p.Type) && Guid.TryParse(p.Source, out _))
                    .Select(p => Guid.Parse(p.Source!)));

            var deletedParts = previousParts
                .Where(pp => IsGroupFamilyAttributionType(pp.Type) && !pp.Exclusionary && Guid.TryParse(pp.Source, out var g) && !currentSources.Contains(g))
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (deletedParts.Count == 0) return lines;

            foreach (var prevPart in deletedParts)
            {
                var label = FormatGroupFamilyPartLabel(prevPart, groupNames);

                // Try the previous per-part blob for a count; fall back to a signal-only line when unavailable.
                var tag = $"{prevPart.Type}_{prevPart.Index + 1}";
                HashSet<Guid>? prevMembers = null;
                if (previousPartFiles.TryGetValue(tag, out var blob) && blob.BlobStatus == BlobStatus.Found)
                {
                    try
                    {
                        prevMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(blob.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read previous per-part blob for deleted-part removes attribution on part #{Index} ({Type}); emitting signal-only line.", prevPart.Index, prevPart.Type);
                    }
                }

                if (prevMembers != null && prevMembers.Count > 0)
                {
                    var matched = 0;
                    foreach (var g in prevMembers)
                    {
                        if (removedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Source removed from query in the recent config update (previously {label}): {bucket} of the removed users were sourced from this now-deleted part");
                        continue;
                    }
                }

                // No previous blob (or no per-part matches) — still emit a signal so the AI can cite the deleted source as the cause without inventing one that is still in the query.
                lines.Add($"- Source removed from query in the recent config update (previously {label}): per-part membership counts are unavailable, but this deleted source is the likely cause of this run's removals");
            }
            return lines;
        }

        // Detects group-family sources whose inclusionary/exclusionary role flipped between the previous config and the current config.
        // A flip from inclusionary → exclusionary causes removals for users who were being included from that source.
        // A flip from exclusionary → inclusionary is not a cause of removals, so we only attribute the incl → excl direction here.
        private async Task<List<string>> AttributeFlippedGroupFamilyPartsForRemovesAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartInfo> previousParts,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<Guid, int> previousRunIndexByGuid)
        {
            var lines = new List<string>();
            if (previousParts == null || previousParts.Count == 0) return lines;
            if (currentParts == null || currentParts.Count == 0) return lines;

            // Build lookup of current group-family parts by source GUID, keyed to their current Exclusionary flag.
            var currentByGuid = new Dictionary<Guid, QueryPartInfo>();
            foreach (var cp in currentParts)
            {
                if (!IsGroupFamilyAttributionType(cp.Type)) continue;
                if (!Guid.TryParse(cp.Source, out var g)) continue;
                currentByGuid[g] = cp;
            }

            // Find previous inclusionary parts whose source is now exclusionary in the current config.
            var flippedParts = new List<(QueryPartInfo previous, QueryPartInfo current)>();
            foreach (var pp in previousParts)
            {
                if (!IsGroupFamilyAttributionType(pp.Type)) continue;
                if (pp.Exclusionary) continue;
                if (!Guid.TryParse(pp.Source, out var g)) continue;
                if (!currentByGuid.TryGetValue(g, out var cp)) continue;
                if (!cp.Exclusionary) continue;
                flippedParts.Add((pp, cp));
            }
            if (flippedParts.Count == 0) return lines;

            foreach (var (prevPart, currentPart) in flippedParts.Take(MaxGroupPartsToAttribute))
            {
                var label = FormatGroupFamilyPartLabel(currentPart, groupNames);

                // Prefer the CURRENT per-part blob (the source still exists in the query at its new index) to count how many
                // members of the now-exclusionary source appear in the removed set. Fall back to the previous blob when needed.
                HashSet<Guid>? sourceMembers = null;
                var currentTag = $"{currentPart.Type}_{currentPart.Index + 1}";
                if (partFiles.TryGetValue(currentTag, out var currentBlob) && currentBlob.BlobStatus == BlobStatus.Found)
                {
                    try
                    {
                        sourceMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(currentBlob.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read current per-part blob for flipped-role removes attribution on part #{Index} ({Type}); trying previous blob.", currentPart.Index, currentPart.Type);
                    }
                }
                if (sourceMembers == null)
                {
                    var previousTag = $"{prevPart.Type}_{prevPart.Index + 1}";
                    if (previousPartFiles.TryGetValue(previousTag, out var previousBlob) && previousBlob.BlobStatus == BlobStatus.Found)
                    {
                        try
                        {
                            sourceMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(previousBlob.Path);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to read previous per-part blob for flipped-role removes attribution on part #{Index} ({Type}); emitting signal-only line.", prevPart.Index, prevPart.Type);
                        }
                    }
                }

                if (sourceMembers != null && sourceMembers.Count > 0)
                {
                    var matched = 0;
                    foreach (var g in sourceMembers)
                    {
                        if (removedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Source flipped from inclusionary to exclusionary in the recent config update ({label}): {bucket} of the removed users are members of this now-exclusionary source");
                        continue;
                    }
                }

                // No blob or no per-part matches — still emit a signal-only line so the AI can cite the flip as the cause without inventing another source.
                lines.Add($"- Source flipped from inclusionary to exclusionary in the recent config update ({label}): per-part membership counts are unavailable, but this role flip is the likely cause of this run's removals");
            }
            return lines;
        }

        // Fix #1: attribute removals to NEWLY-ADDED exclusionary sources.
        // When a new exclusionary source is added in the recent config update, users who were in the destination and are also
        // in the new excluded source are now filtered out → they show up as removes.
        private async Task<List<string>> AttributeNewExclusionaryGroupFamilyPartsForRemovesAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartInfo> previousParts,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            if (currentParts == null || currentParts.Count == 0) return lines;

            // Every source GUID seen in ANY prior config (across the walkback union) counts as "present before".
            // A new exclusionary source is one whose GUID is exclusionary now AND was absent from every previous config in the union.
            var previousSourceGuids = new HashSet<Guid>();
            if (previousParts != null)
            {
                foreach (var pp in previousParts)
                {
                    if (!IsGroupFamilyAttributionType(pp.Type)) continue;
                    if (!Guid.TryParse(pp.Source, out var g)) continue;
                    previousSourceGuids.Add(g);
                }
            }

            var newExclParts = currentParts
                .Where(cp => IsGroupFamilyAttributionType(cp.Type)
                             && cp.Exclusionary
                             && Guid.TryParse(cp.Source, out var g)
                             && !previousSourceGuids.Contains(g))
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (newExclParts.Count == 0) return lines;

            foreach (var part in newExclParts)
            {
                var label = FormatGroupFamilyPartLabel(part, groupNames);
                var currentTag = $"{part.Type}_{part.Index + 1}";
                HashSet<Guid>? sourceMembers = null;
                if (partFiles.TryGetValue(currentTag, out var blob) && blob.BlobStatus == BlobStatus.Found)
                {
                    try
                    {
                        sourceMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(blob.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read current per-part blob for new-exclusionary removes attribution on part #{Index} ({Type}); emitting signal-only line.", part.Index, part.Type);
                    }
                }

                if (sourceMembers != null && sourceMembers.Count > 0)
                {
                    var matched = 0;
                    foreach (var g in sourceMembers)
                    {
                        if (removedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- New exclusionary source added in the recent config update ({label}): {bucket} of the removed users are members of this newly-excluded source");
                        continue;
                    }
                }

                lines.Add($"- New exclusionary source added in the recent config update ({label}): per-part membership counts are unavailable, but this newly-added exclusionary source is the likely cause of this run's removals");
            }
            return lines;
        }

        // Fix #2: attribute additions to DELETED exclusionary sources.
        // When an exclusionary source is removed from the query, users it was blocking are now admitted → they show up as adds.
        private async Task<List<string>> AttributeDeletedExclusionaryGroupFamilyPartsForAddsAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartInfo> previousParts,
            HashSet<string> addedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<Guid, int> previousRunIndexByGuid)
        {
            var lines = new List<string>();
            if (previousParts == null || previousParts.Count == 0) return lines;

            // ALL current group-family source GUIDs (inclusionary OR exclusionary) — a flipped source counts as still-present.
            var currentSources = new HashSet<Guid>(
                (currentParts ?? new List<QueryPartInfo>())
                    .Where(p => IsGroupFamilyAttributionType(p.Type) && Guid.TryParse(p.Source, out _))
                    .Select(p => Guid.Parse(p.Source!)));

            var deletedExclParts = previousParts
                .Where(pp => IsGroupFamilyAttributionType(pp.Type) && pp.Exclusionary && Guid.TryParse(pp.Source, out var g) && !currentSources.Contains(g))
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (deletedExclParts.Count == 0) return lines;

            foreach (var prevPart in deletedExclParts)
            {
                var label = FormatGroupFamilyPartLabel(prevPart, groupNames);

                // Look up the previous blob by the SOURCE GUID's previous-run index (Fix #3 pattern).
                string? previousTag = null;
                if (Guid.TryParse(prevPart.Source, out var partGuid) && previousRunIndexByGuid.TryGetValue(partGuid, out var prevIndex))
                {
                    previousTag = $"{prevPart.Type}_{prevIndex + 1}";
                }
                else
                {
                    previousTag = $"{prevPart.Type}_{prevPart.Index + 1}";
                }

                HashSet<Guid>? prevMembers = null;
                if (previousPartFiles.TryGetValue(previousTag, out var blob) && blob.BlobStatus == BlobStatus.Found)
                {
                    try
                    {
                        prevMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(blob.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read previous per-part blob for deleted-exclusionary adds attribution on part #{Index} ({Type}); emitting signal-only line.", prevPart.Index, prevPart.Type);
                    }
                }

                if (prevMembers != null && prevMembers.Count > 0)
                {
                    var matched = 0;
                    foreach (var g in prevMembers)
                    {
                        if (addedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Exclusionary source removed from query in the recent config update (previously {label}): {bucket} of the added users were previously excluded by this now-removed exclusion");
                        continue;
                    }
                }

                lines.Add($"- Exclusionary source removed from query in the recent config update (previously {label}): per-part membership counts are unavailable, but this deleted exclusionary source is the likely cause of this run's additions");
            }
            return lines;
        }

        // Fix #5: attribute additions to sources that flipped from exclusionary to inclusionary.
        // A source that was blocking users but is now including them contributes adds equal to its current members that appear in the added set.
        private async Task<List<string>> AttributeFlippedGroupFamilyPartsForAddsAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartInfo> previousParts,
            HashSet<string> addedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            if (previousParts == null || previousParts.Count == 0) return lines;
            if (currentParts == null || currentParts.Count == 0) return lines;

            var currentByGuid = new Dictionary<Guid, QueryPartInfo>();
            foreach (var cp in currentParts)
            {
                if (!IsGroupFamilyAttributionType(cp.Type)) continue;
                if (!Guid.TryParse(cp.Source, out var g)) continue;
                currentByGuid[g] = cp;
            }

            // Previously exclusionary, now inclusionary.
            var flippedParts = new List<QueryPartInfo>();
            foreach (var pp in previousParts)
            {
                if (!IsGroupFamilyAttributionType(pp.Type)) continue;
                if (!pp.Exclusionary) continue;
                if (!Guid.TryParse(pp.Source, out var g)) continue;
                if (!currentByGuid.TryGetValue(g, out var cp)) continue;
                if (cp.Exclusionary) continue;
                flippedParts.Add(cp);
            }
            if (flippedParts.Count == 0) return lines;

            foreach (var currentPart in flippedParts.Take(MaxGroupPartsToAttribute))
            {
                var label = FormatGroupFamilyPartLabel(currentPart, groupNames);
                var currentTag = $"{currentPart.Type}_{currentPart.Index + 1}";
                HashSet<Guid>? sourceMembers = null;
                if (partFiles.TryGetValue(currentTag, out var blob) && blob.BlobStatus == BlobStatus.Found)
                {
                    try
                    {
                        sourceMembers = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(blob.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read current per-part blob for flipped exclusionary→inclusionary adds attribution on part #{Index} ({Type}); emitting signal-only line.", currentPart.Index, currentPart.Type);
                    }
                }

                if (sourceMembers != null && sourceMembers.Count > 0)
                {
                    var matched = 0;
                    foreach (var g in sourceMembers)
                    {
                        if (addedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Source flipped from exclusionary to inclusionary in the recent config update ({label}): {bucket} of the added users are members of this newly-inclusionary source");
                        continue;
                    }
                }

                lines.Add($"- Source flipped from exclusionary to inclusionary in the recent config update ({label}): per-part membership counts are unavailable, but this role flip is the likely cause of this run's additions");
            }
            return lines;
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

        private static class OwnerFriendlyFilterFormatter
        {
            private const string GenericCriteriaDescription = "the configured HR criteria";

            private static readonly Regex _codeAttributeRegex = new(
                @"\b(?<attribute>[A-Za-z_][A-Za-z0-9_]*_Code)\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            private static readonly Regex _predicateRegex = new(
                @"(?<attribute>\[?[A-Za-z_][A-Za-z0-9_]*\]?)\s*(?<operator>IS\s+NOT\s+NULL|IS\s+NULL|NOT\s+IN|NOT\s+LIKE|IN|LIKE|>=|<=|<>|!=|=|>|<)(?:\s*(?<value>\((?:[^()']|'(?:''|[^'])*')*\)|N?'(?:''|[^'])*'|[-+]?\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_.-]*))?",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            private static readonly Regex _valueRegex = new(
                @"N?'(?:''|[^'])*'|[^,]+",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            private static readonly IReadOnlyDictionary<string, string> _wordReplacements =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cnt"] = "Count",
                    ["Dept"] = "Department",
                    ["Desc"] = "Description",
                    ["Id"] = "ID",
                    ["Ind"] = "Indicator",
                    ["Mgr"] = "Manager",
                    ["Nbr"] = "Number",
                    ["Num"] = "Number",
                    ["Org"] = "Organization"
                };

            public static HashSet<string> CollectCodeAttributes(IEnumerable<string?> filters)
            {
                var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var filter in filters)
                {
                    if (string.IsNullOrWhiteSpace(filter))
                    {
                        continue;
                    }

                    foreach (Match match in _codeAttributeRegex.Matches(filter))
                    {
                        attributes.Add(match.Groups["attribute"].Value);
                    }
                }

                return attributes;
            }

            public static string DescribeFilter(
                string? filter,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions = null)
            {
                if (string.IsNullOrWhiteSpace(filter))
                {
                    return GenericCriteriaDescription;
                }

                var matches = _predicateRegex.Matches(filter);
                if (matches.Count == 0)
                {
                    return GenericCriteriaDescription;
                }

                var description = new StringBuilder();
                var previousEnd = 0;

                foreach (Match match in matches)
                {
                    if (!TryDescribeConnector(filter[previousEnd..match.Index], out var connector))
                    {
                        return GenericCriteriaDescription;
                    }

                    description.Append(connector);
                    description.Append(DescribePredicate(match, mappingDescriptions));
                    previousEnd = match.Index + match.Length;
                }

                if (!TryDescribeConnector(filter[previousEnd..], out var trailingConnector))
                {
                    return GenericCriteriaDescription;
                }

                description.Append(trailingConnector);

                var result = Regex.Replace(description.ToString(), @"\s+", " ").Trim();
                result = result.Replace("( ", "(", StringComparison.Ordinal)
                    .Replace(" )", ")", StringComparison.Ordinal);

                return string.IsNullOrWhiteSpace(result) ? GenericCriteriaDescription : result;
            }

            public static string HumanizeAttributeName(string attribute)
            {
                if (string.IsNullOrWhiteSpace(attribute))
                {
                    return "Attribute";
                }

                var name = attribute.Trim().Trim('[', ']');
                if (name.EndsWith("_Code", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^"_Code".Length];
                }

                name = name.Replace('_', ' ');
                name = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2");
                name = Regex.Replace(name, @"([A-Z]+)([A-Z][a-z])", "$1 $2");

                var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word =>
                    {
                        if (_wordReplacements.TryGetValue(word, out var replacement))
                        {
                            return replacement;
                        }

                        if (word.All(char.IsUpper))
                        {
                            return word;
                        }

                        return char.ToUpperInvariant(word[0]) + word[1..];
                    });

                return string.Join(" ", words);
            }

            public static string DescribeAttributeValue(
                string attribute,
                string? value,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions = null)
            {
                return DescribeValue(attribute, value ?? string.Empty, mappingDescriptions);
            }

            private static string DescribePredicate(
                Match match,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions)
            {
                var attribute = match.Groups["attribute"].Value.Trim('[', ']');
                var displayName = HumanizeAttributeName(attribute);
                var normalizedOperator = Regex.Replace(match.Groups["operator"].Value, @"\s+", " ")
                    .ToUpperInvariant();

                if (normalizedOperator == "IS NULL")
                {
                    return $"{displayName} has no value";
                }

                if (normalizedOperator == "IS NOT NULL")
                {
                    return $"{displayName} has a value";
                }

                var rawValue = match.Groups["value"].Success
                    ? match.Groups["value"].Value
                    : string.Empty;

                if (normalizedOperator is "LIKE" or "NOT LIKE")
                {
                    return DescribeLikePredicate(displayName, normalizedOperator, rawValue);
                }

                var values = ParseValues(rawValue)
                    .Select(value => DescribeValue(attribute, value, mappingDescriptions))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (values.Count == 0)
                {
                    values.Add(attribute.EndsWith("_Code", StringComparison.OrdinalIgnoreCase)
                        ? "a configured value whose description is unavailable"
                        : "an unspecified value");
                }

                var formattedValue = JoinValues(values);
                var operatorText = normalizedOperator switch
                {
                    "=" => "is",
                    "<>" or "!=" => "is not",
                    ">=" => "is at least",
                    "<=" => "is at most",
                    ">" => "is greater than",
                    "<" => "is less than",
                    "IN" => "is one of",
                    "NOT IN" => "is not one of",
                    _ => "matches"
                };

                return $"{displayName} {operatorText} {formattedValue}";
            }

            private static string DescribeLikePredicate(string displayName, string normalizedOperator, string rawValue)
            {
                var value = Unquote(rawValue);
                var startsWithWildcard = value.StartsWith('%');
                var endsWithWildcard = value.EndsWith('%');
                var literal = value.Trim('%');
                var quoted = Quote(literal);
                var negated = normalizedOperator == "NOT LIKE";

                if (startsWithWildcard && endsWithWildcard)
                {
                    return $"{displayName} {(negated ? "does not contain" : "contains")} {quoted}";
                }

                if (startsWithWildcard)
                {
                    return $"{displayName} {(negated ? "does not end with" : "ends with")} {quoted}";
                }

                if (endsWithWildcard)
                {
                    return $"{displayName} {(negated ? "does not start with" : "starts with")} {quoted}";
                }

                return $"{displayName} {(negated ? "does not match" : "matches")} {quoted}";
            }

            private static List<string> ParseValues(string rawValue)
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return new List<string>();
                }

                var valueList = rawValue.Trim();
                if (valueList.StartsWith('(') && valueList.EndsWith(')'))
                {
                    valueList = valueList[1..^1];
                }

                return _valueRegex.Matches(valueList)
                    .Select(match => match.Value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }

            private static string DescribeValue(
                string attribute,
                string rawValue,
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions)
            {
                var value = Unquote(rawValue);
                var isCode = attribute.EndsWith("_Code", StringComparison.OrdinalIgnoreCase);

                if (isCode)
                {
                    var baseAttribute = attribute[..^"_Code".Length];
                    if (TryGetMapping(mappingDescriptions, attribute, baseAttribute, out var mappings)
                        && mappings.TryGetValue(value, out var description)
                        && !string.IsNullOrWhiteSpace(description))
                    {
                        return Quote(description);
                    }

                    return "a configured value whose description is unavailable";
                }

                if (IsBooleanAttribute(attribute))
                {
                    if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Yes";
                    }

                    if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        return "No";
                    }
                }

                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    return value;
                }

                return Quote(value);
            }

            private static bool TryGetMapping(
                IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? mappingDescriptions,
                string attribute,
                string baseAttribute,
                out IReadOnlyDictionary<string, string> mappings)
            {
                if (mappingDescriptions != null
                    && mappingDescriptions.TryGetValue(attribute, out var attributeMappings)
                    && attributeMappings != null)
                {
                    mappings = attributeMappings;
                    return true;
                }

                if (mappingDescriptions != null
                    && mappingDescriptions.TryGetValue(baseAttribute, out var baseAttributeMappings)
                    && baseAttributeMappings != null)
                {
                    mappings = baseAttributeMappings;
                    return true;
                }

                mappings = new Dictionary<string, string>();
                return false;
            }

            private static bool IsBooleanAttribute(string attribute)
            {
                return attribute.EndsWith("Ind", StringComparison.OrdinalIgnoreCase)
                    || attribute.EndsWith("Indicator", StringComparison.OrdinalIgnoreCase)
                    || attribute.EndsWith("Flag", StringComparison.OrdinalIgnoreCase);
            }

            private static string Unquote(string value)
            {
                var result = value.Trim();
                if (result.StartsWith("N'", StringComparison.OrdinalIgnoreCase) && result.EndsWith('\''))
                {
                    result = result[2..^1];
                }
                else if (result.StartsWith('\'') && result.EndsWith('\''))
                {
                    result = result[1..^1];
                }

                return result.Replace("''", "'", StringComparison.Ordinal);
            }

            private static string Quote(string value)
            {
                return $"\"{value.Replace("\"", "'", StringComparison.Ordinal)}\"";
            }

            private static string JoinValues(IReadOnlyList<string> values)
            {
                return values.Count switch
                {
                    0 => string.Empty,
                    1 => values[0],
                    2 => $"{values[0]} or {values[1]}",
                    _ => $"{string.Join(", ", values.Take(values.Count - 1))}, or {values[^1]}"
                };
            }

            private static bool TryDescribeConnector(string connector, out string description)
            {
                var withoutLogicalOperators = Regex.Replace(connector, @"\bAND\b|\bOR\b", string.Empty, RegexOptions.IgnoreCase);
                withoutLogicalOperators = withoutLogicalOperators
                    .Replace("(", string.Empty, StringComparison.Ordinal)
                    .Replace(")", string.Empty, StringComparison.Ordinal);

                if (!string.IsNullOrWhiteSpace(withoutLogicalOperators))
                {
                    description = string.Empty;
                    return false;
                }

                description = Regex.Replace(connector, @"\bAND\b", " and ", RegexOptions.IgnoreCase);
                description = Regex.Replace(description, @"\bOR\b", " or ", RegexOptions.IgnoreCase);
                return true;
            }
        }

        private static class OwnerFriendlyFilterMappingResolver
        {
            public static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ResolveAsync(
                IEnumerable<string?> filters,
                Guid? runAdfRunId,
                ISqlMembershipRepository sqlMembershipRepository,
                IDataFactoryRepository dataFactoryRepository,
                ILogger logger)
            {
                var unresolvedAttributes = OwnerFriendlyFilterFormatter.CollectCodeAttributes(filters);
                var resolved = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                if (unresolvedAttributes.Count == 0)
                {
                    return resolved;
                }

                var historicalTable = runAdfRunId.HasValue && runAdfRunId.Value != Guid.Empty
                    ? runAdfRunId.Value.ToString().Replace("-", string.Empty)
                    : null;

                if (!string.IsNullOrWhiteSpace(historicalTable))
                {
                    await LoadMappingsAsync(
                        historicalTable,
                        unresolvedAttributes,
                        resolved,
                        sqlMembershipRepository,
                        logger);
                }

                if (unresolvedAttributes.Count > 0)
                {
                    try
                    {
                        var latestAdfRunId = await dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                        if (!string.IsNullOrWhiteSpace(latestAdfRunId))
                        {
                            var latestTable = latestAdfRunId.Replace("-", string.Empty);
                            if (!string.Equals(latestTable, historicalTable, StringComparison.OrdinalIgnoreCase))
                            {
                                await LoadMappingsAsync(
                                    latestTable,
                                    unresolvedAttributes,
                                    resolved,
                                    sqlMembershipRepository,
                                    logger);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to resolve the latest ADF mappings table for owner-friendly AI criteria.");
                    }
                }

                return resolved;
            }

            private static async Task LoadMappingsAsync(
                string tableName,
                HashSet<string> unresolvedAttributes,
                Dictionary<string, IReadOnlyDictionary<string, string>> resolved,
                ISqlMembershipRepository sqlMembershipRepository,
                ILogger logger)
            {
                bool tableExists;
                try
                {
                    tableExists = await sqlMembershipRepository.CheckIfMappingsTableExistsAsync(tableName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to verify ADF mappings table {TableName}; mapped filter descriptions will use the next available table.", tableName);
                    return;
                }

                if (!tableExists)
                {
                    return;
                }

                foreach (var codeAttribute in unresolvedAttributes.ToList())
                {
                    var baseAttribute = codeAttribute[..^"_Code".Length];
                    try
                    {
                        var mappings = await sqlMembershipRepository.GetAttributeMappingsAsync(baseAttribute, tableName);
                        var descriptionsByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var mapping in mappings)
                        {
                            if (string.IsNullOrWhiteSpace(mapping.Code)
                                || string.IsNullOrWhiteSpace(mapping.Description))
                            {
                                continue;
                            }

                            descriptionsByCode.TryAdd(mapping.Code.Trim(), mapping.Description.Trim());
                        }

                        if (descriptionsByCode.Count > 0)
                        {
                            resolved[codeAttribute] = descriptionsByCode;
                            unresolvedAttributes.Remove(codeAttribute);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Failed to load mapping descriptions for {Attribute} from {TableName}; owner-friendly criteria will not expose the raw code.",
                            baseAttribute,
                            tableName);
                    }
                }
            }
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
