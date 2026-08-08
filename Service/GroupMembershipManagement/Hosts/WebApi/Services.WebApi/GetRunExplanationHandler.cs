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
    public partial class GetRunExplanationHandler : RequestHandlerBase<GetRunExplanationRequest, GetRunExplanationResponse>
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

        // Phase 2 removes walk-back: max prior runs to scan backward through per-part source blobs when single-step attribution finds nothing (bounds the on-demand blob reads).
        public const int WalkBackMaxRuns = 12;

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
5. **Upstream sources changed (no in-GMM signal)**: ""This sync reflects changes in [upstream source]."" Fill in [upstream source] using the exact phrase from the ""Job source kind"" line in the prompt — ""upstream HR data"" for an HrData job, ""upstream source group memberships"" for a Group job, or ""upstream membership sources"" for a Mixed/Other job. NEVER say ""source group"" for an HrData job.
6. **Insufficient signal (LAST resort only)**: ""The specific reason could not be determined from the available data."" Use this ONLY when there are NO added or removed users to explain, OR when the ""Membership rules as of this run:"" glossary is empty/absent so the group's rule cannot be described. When there ARE added or removed users AND that glossary describes the rule, you MUST use the Descriptive membership-rule fallback below instead of this hedge.
**Descriptive membership-rule fallback (never hedge when the rule is known)**: When there ARE added or removed users but no ""Per-part attribution"" section names a specific source (the section is absent or shows a ""(no attributable source found …)"" marker) AND no applicable configuration change explains the delta, do NOT default to pattern 6. Build a descriptive explanation from the always-available ""Membership rules as of this run:"" glossary — for additions say the users were added because they match the group's membership rule, describing that rule in plain English; for removals say the users were in the group but no longer match its membership rule, describing that rule in plain English. Restating the group's OWN rule this way is an always-available fact, NOT naming a specific source as the mechanism, so it does NOT violate the anti-hallucination rule — you still MUST NOT claim the users joined or left a specific named source by process of elimination. For removals whose exact drop-out point is unknown, you MAY add that the exact date isn't available in the retained history, but keep the descriptive rule framing and NEVER say the reason could not be determined. Combine with pattern 4 for threshold-blocked runs.
7. **Manager scope changed**: ""This sync's candidate population changed because the membership rule's scope was updated on [date] from [old scope] to [new scope]"" — where ""scope"" comes from the configDiff line and looks like ""id=N (depth<=M)"", ""id=N (unbounded depth)"", or ""none (filter-only, no hierarchy scope)"". Only usable when the ""Configuration history"" section shows an explicit ""What changed:"" line naming the scope change — never when the stale marker is present.
8. **Empty membership rule result**: ""This sync made no membership changes because the inclusionary membership rule matched no employees in the HR snapshot for this run."" Tailor the sentence to the variant: for filter-only, mention only the owner-friendly criteria; for manager+unbounded, mention the manager root and the criteria; for manager+depth-cap, mention the manager root, the depth cap, and the criteria. Never reconstruct the raw filter.
9. **Per-rule attribution available**: When the prompt contains a ""Per-rule attribution for added users"" section, use those qualitative terms verbatim (e.g., ""most added users match the inclusionary HR rule scoped to id=100 (unbounded depth)""). Pair this with whichever change pattern (1, 7, etc.) is appropriate. NEVER translate ""most"" / ""almost all"" / ""a few"" into specific counts or percentages — the buckets are qualitative on purpose to avoid fabricated precision.
10. **IgnoreThresholdOnce applied**: When the Configuration history section contains an explicit ""IgnoreThresholdOnce activated on [date]"" line, that's the direct cause of this sync's delta: the previous run was blocked by the configured threshold, an owner (or automation) activated IgnoreThresholdOnce, and this sync applied the previously-pending changes. Use pattern: ""This sync applied the [adds|removes|adds and removes] that were previously blocked by the threshold, because IgnoreThresholdOnce was activated on [date]."" NEVER use this pattern unless the explicit ""IgnoreThresholdOnce activated on [date]"" line is present in the prompt — the marker is emitted only when the event was activated in THIS sync's window; otherwise, the ITO event is stale and MUST NOT be cited (even if the historical event is technically still visible elsewhere). Combine with pattern 1 / 7 phrasing when a rule change also drove the previously-pending delta, using the owner-friendly old and new criteria from the prompt.
11. **Per-part attribution for removed users available**: When the prompt contains a ""Per-part attribution for removed users"" section, use those qualitative terms verbatim to explain the removals (e.g., ""most removed users left the source group `TestGroupMember`"", or ""a few removed users no longer match the inclusionary HR rule"", or — when a source was dropped by a recent config update — ""all of the removed users were previously sourced from the group `X` which was removed from the query""). When the attribution line for a deleted source says ""per-part membership counts are unavailable, but this deleted source is the likely cause"", phrase it as: ""This sync's removals likely came from users who were previously sourced from the group `X`, which was removed from the query in a recent config update."" When the attribution line indicates a source ""flipped from inclusionary to exclusionary"", phrase it as: ""This sync's removals came from users who are members of `X`, which was recently flipped from an inclusionary source to an exclusionary source in the query."" When the attribution line indicates a ""New exclusionary source added"", phrase it as: ""This sync's removals came from users who are members of `X`, which was recently added to the query as an exclusionary source."" **When multiple ""Source removed from query"" attribution lines are present, you MUST name EVERY deleted source in your output — not just the first one. Combine them naturally: ""...from the groups `X` and `Y`"" for two, or ""...from the groups `X`, `Y`, and `Z`"" for three or more. Under no circumstances omit any deleted source that is cited in the attribution section.** Same rule applies to ""Source flipped"" and ""New exclusionary source added"" attribution lines — name EVERY cited source. **Under no circumstances name a source that is NOT cited in the attribution section.** If exactly one source is cited, name exactly that one source and do not add a second name to make the sentence plural. Prefer specific attribution over generic phrasing like ""the specific reason could not be determined"". Pair with pattern 4 for threshold-blocked runs (e.g., ""...blocked by the threshold. All of the proposed removals were previously sourced from the groups `X` and `Y`, both of which were removed from the query.""). If the section is absent and there are removed users, either omit any per-removal explanation or fall back to pattern 5 (upstream source changes — use the ""Job source kind"" phrasing) — NEVER invent an attribution. **CRITICAL anti-hallucination rule**: NEVER name a specific source group, HR rule, or exclusionary source as the cause of removals unless it is cited in the ""Per-part attribution for removed users"" section OR the ""Configuration history"" section shows an explicit ""What changed"" line involving that source in this window. The current query listed under ""Configuration as of this run"" is NOT proof of attribution — a source being listed as CURRENT does not mean users were removed FROM it. Removals typically come from sources that WERE in the query previously but are NO LONGER in the query, from users who no longer match the current sources' criteria, or from sources whose role flipped from inclusionary to exclusionary, or from newly-added exclusionary sources — do not conflate these cases. **BAD example — process-of-elimination hallucination**: given ""Per-part attribution for added users: all of the added users match source group `X`"" and NO removes attribution section (or a ""(no attributable source found)"" removes marker), it is FORBIDDEN to output ""all removals came from users leaving `Y`"" just because `Y` is the OTHER source in the current query. That reasoning is process-of-elimination guessing, not evidence — the correct output is to describe removals without naming any specific source (pattern 4 without a per-removal source, or pattern 5, or pattern 6). Same rule applies in reverse for adds — never mirror the removes attribution shape onto adds when the adds attribution section is empty or marked ""(no attributable source found)"". **BAD example — inventing a second name to make a sentence plural**: given a single attribution line like ""- Source removed from query (previously group `X`): all of the removed users were sourced from this now-deleted part"", it is FORBIDDEN to output ""...came from the groups `X` and `Y`"" — you must output ""...came from the group `X`"" (singular) since only ONE source is cited. Never pluralize by inventing a second source name from the current query or from prior conversations.
**Current-state color (latest HR data):** When a removes attribution line is qualified with ""in the latest HR data (current state, not the removal-time snapshot)"", it was evaluated against the CURRENT HR data because the exact removal-time snapshot is no longer retained. Phrase it in present tense (e.g., ""those removed users currently no longer match the group's rule in the latest HR data"") and NEVER assert it as the definitive reason at the moment of removal. This line is emitted only for removed users who still fail the rule today, so do not extrapolate it to users who may still match.
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

                // Phase 2: when single-step attribution found nothing and the query was stable across the window, fall back to a durable walk-back over prior runs' per-part SOURCE blobs (persist ~30 days, independent of the ~1-day ADF tables) to recover attribution when the previous ADF table was pruned or users dropped out several runs back; floored at the last config change that actually CHANGED the query so identical resubmits don't truncate the same-query stretch.
                if (removed.Count > 0 && !hasConfigChangeInWindow && HasNoRemovesAttribution(removesAttribution))
                {
                    // Floor at the most recent change that actually changed the query (an identical resubmit does not); see ComputeWalkBackFloor.
                    var walkBackFloor = ComputeWalkBackFloor(recentChanges, currentQueryNormalized);
                    var priorRuns = (await GetPreviousRunHistoriesAsync(request.SyncJobId, runHistory, WalkBackMaxRuns))
                        .Where(r => r.UpdatedAt >= walkBackFloor)
                        .ToList();
                    var walkBackAttribution = await ComputeRemovesWalkBackAttributionAsync(
                        parts, removed, targetGroupId, priorRuns, managerNames, groupNames);
                    if (!string.IsNullOrEmpty(walkBackAttribution))
                    {
                        removesAttribution = walkBackAttribution;
                    }
                }

                // Phase 3: guarded current-state color. When the exact-run ADF snapshot is gone, re-evaluate the inclusionary SQL rule against the latest ADF table and add present-tense "currently no longer match" color, suppressing any removed user who still matches today (contradiction guard); additive to the walk-back's removal-time attribution and never runs during a config-change window.
                if (removed.Count > 0 && !hasConfigChangeInWindow)
                {
                    var currentStateLines = await ComputeRemovesCurrentStateColorLinesAsync(parts, removed, runHistory.AdfRunId, managerNames);
                    removesAttribution = ComposeRemovesAttributionWithCurrentState(removesAttribution, currentStateLines);
                }

                // Phase 4 telemetry: record which attribution layer answered (descriptive == never-hedge rule fallback) so the hedge-rate drop is measurable in App Insights.
                _logger.LogInformation("RunExplanation attribution layers for job {JobId} run {RunId}: adds={AddsLayer}, removes={RemovesLayer}.", request.SyncJobId, request.RunId, ClassifyAttributionLayer(addsAttribution, added.Count > 0, hasConfigChangeInWindow), ClassifyAttributionLayer(removesAttribution, removed.Count > 0, hasConfigChangeInWindow));

                var userPrompt = BuildRunPrompt(request, runHistory, membershipRules, cappedAdded, cappedRemoved, added.Count, removed.Count, isThresholdBlocked, isInitialRun, prevThresholdViolations, thisThresholdViolations, configDiff, hrDiff, groupNames, managerNames, addsAttribution, removesAttribution, ClassifyJobSourceKind(parts));

                string explanation;
                using (_logger.BeginScope(new Dictionary<string, object> { ["AIFeature"] = "RunExplanation" }))
                {
                    explanation = await _openAIService.GetCompletionAsync(SystemPrompt, userPrompt);
                }

                response.Explanation = string.IsNullOrWhiteSpace(explanation) ? FallbackExplanation : explanation.Trim();

                // Never-hedge safety net: the never-hedge behavior is otherwise prompt-only, so a non-deterministic model can still ship the generic "reason could not be determined" hedge (or a per-add/remove variant of it) even when the run moved members and the membership rules are known. When the model answers with a hedge on a run that actually moved (or, when threshold-blocked, proposed to move) members, replace it with a deterministic, rule-based explanation. A blank/timeout/rate-limited completion is intentionally left as FallbackExplanation (IsHedgeExplanation returns false for blank). Explanation text only — this never affects any membership calculation.
                var effectiveAdded = Math.Max(usersAdded, added.Count);
                var effectiveRemoved = Math.Max(usersRemoved, removed.Count);
                if ((effectiveAdded > 0 || effectiveRemoved > 0) && IsHedgeExplanation(explanation))
                {
                    response.Explanation = isInitialRun
                        ? BuildInitialRunExplanation(effectiveAdded, effectiveRemoved, membershipRules)
                        : BuildNonInitialRuleExplanation(effectiveAdded, effectiveRemoved, isThresholdBlocked, membershipRules);
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

        // Floor the removes walk-back at the most recent config change that actually CHANGED the query. An identical resubmit (a no-op config update, common right after a threshold block) records a SyncJobChange but keeps the same normalized query, so flooring at the latest change unconditionally would truncate the same-query stretch and leave older removals unattributed. Changes without a query snapshot don't define a query boundary and are skipped; DateTime.MinValue means walk back as far as retained runs allow.
        private static DateTime ComputeWalkBackFloor(IReadOnlyList<Models.SyncJobChange.SyncJobChange>? recentChanges, string? currentQueryNormalized)
        {
            if (recentChanges == null) return DateTime.MinValue;
            foreach (var change in recentChanges)
            {
                var changeQuery = ExtractQueryFromChangeDetails(change.ChangeDetails);
                if (changeQuery == null) continue;
                if (!string.Equals(NormalizeQuery(changeQuery), currentQueryNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    return change.ChangeTime;
                }
            }
            return DateTime.MinValue;
        }

        // Phase 2 walk-back support: returns up to maxRuns history rows strictly before this run's UpdatedAt, most-recent-first (100/page, up to 20 pages, stops once enough are collected); generalizes GetPreviousRunHistoryAsync for the removes blob walk-back.
        private async Task<List<Models.SyncJobHistory.SyncJobHistory>> GetPreviousRunHistoriesAsync(
            Guid syncJobId, Models.SyncJobHistory.SyncJobHistory runHistory, int maxRuns)
        {
            if (maxRuns <= 0) return new List<Models.SyncJobHistory.SyncJobHistory>();

            const int pageSize = 100;
            const int maxPages = 20;
            var collected = new List<Models.SyncJobHistory.SyncJobHistory>();
            var seen = new HashSet<Guid>();

            for (int page = 1; page <= maxPages && collected.Count < maxRuns; page++)
            {
                var historyPage = await _syncJobHistoryRepository.GetBySyncJobIdAsync(syncJobId, pageSize: pageSize, pageNumber: page);
                if (historyPage == null || historyPage.Count == 0) break;

                foreach (var h in historyPage
                    .Where(h => h.RunId != runHistory.RunId && h.RunId != Guid.Empty && h.UpdatedAt < runHistory.UpdatedAt)
                    .OrderByDescending(h => h.UpdatedAt))
                {
                    if (seen.Add(h.RunId))
                    {
                        collected.Add(h);
                        if (collected.Count >= maxRuns) break;
                    }
                }

                if (historyPage.Count < pageSize) break; // last (partial) page reached — nothing older remains
            }

            return collected
                .OrderByDescending(h => h.UpdatedAt)
                .Take(maxRuns)
                .ToList();
        }
    }
}
