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
using SqlMembershipObtainer.Entities;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Models.AdfRunHistory;

namespace Services
{
    public partial class GetRunExplanationHandler
    {
        // Per-part attribution for added users: SqlMembership parts re-run the recursive CTE; group-family parts read the per-part blob.
        // Missing data silently skips that line — never guesses. Caller pre-fetches partFiles to overlap the two blob enumerations.
        private async Task<string> ComputeAddsAttributionAsync(
            IReadOnlyList<QueryPartInfo> parts,
            IReadOnlyList<QueryPartTransition> historicalTransitions,
            IReadOnlyList<QueryPartInfo> previousRunParts,
            IReadOnlyCollection<Guid> addedUsers,
            Guid? runAdfRunId,
            IReadOnlyDictionary<int, string>? managerNames,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles)
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

            // A removed exclusionary rule can admit users that it previously blocked.
            var deletedExclLines = await AttributeDeletedExclusionaryGroupFamilyPartsForAddsAsync(parts, historicalTransitions, previousRunParts, addedSet, totalSampled, previousPartFiles, groupNames);
            lines.AddRange(deletedExclLines);

            // A rule flipped from exclusionary to inclusionary can contribute additions.
            var flippedExclToInclLines = await AttributeFlippedGroupFamilyPartsForAddsAsync(parts, historicalTransitions, addedSet, totalSampled, partFiles, groupNames);
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
                sbEmpty.AppendLine("- HARD RULE: do NOT name any specific source group, HR rule, or inclusionary source as the cause of adds, and do NOT infer one by process of elimination from the removes attribution or from sources listed under 'Job filter' or 'Configuration as of this run'. Instead use the descriptive membership-rule fallback: say these users were added because they match the group's membership rule, describing that rule in plain English from the 'Membership rules as of this run' glossary (this restates the group's own definition, it does not name a specific source). NEVER say the reason could not be determined.");
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
                var ruleMembers = await FetchSqlPartMembersAsync(part, tableName);

                if (ruleMembers == null || ruleMembers.Count == 0)
                {
                    return null;
                }

                var matched = ruleMembers.Count(m =>
                    !string.IsNullOrWhiteSpace(m) &&
                    addedSet.Contains(m));
                if (matched == 0)
                {
                    return null;
                }

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
            if (matched == 0)
            {
                return null;
            }

            var bucket = BucketAttribution(matched, totalSampled);
            var label = FormatGroupFamilyPartLabel(part, groupNames);
            return $"- {label}: {bucket} of the added users matched this rule";
        }

        // Per-part attribution for removed users. Requires previous-run data (blobs + ADF snapshot); silent skip when missing.
        private async Task<string> ComputeRemovesAttributionAsync(
            IReadOnlyList<QueryPartInfo> parts,
            IReadOnlyList<QueryPartTransition> historicalTransitions,
            IReadOnlyList<QueryPartInfo> previousRunParts,
            IReadOnlyCollection<Guid> removedUsers,
            Guid? runAdfRunId,
            Guid? previousAdfRunId,
            IReadOnlyDictionary<int, string>? managerNames,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<int, int> previousRunIndexByCurrentIndex)
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
                return AttributeGroupFamilyPartForRemovesAsync(part, removedSet, totalSampled, partFiles, previousPartFiles, groupNames, previousRunIndexByCurrentIndex);
            });
            var results = await Task.WhenAll(tasks);
            var lines = results.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();

            // Deleted inclusionary group-family parts: source dropped by a recent config update, so its previous members appear as removes.
            var deletedLines = await AttributeDeletedGroupFamilyPartsForRemovesAsync(parts, historicalTransitions, previousRunParts, removedSet, totalSampled, previousPartFiles, groupNames);
            lines.AddRange(deletedLines);

            // Flipped group-family parts: source was inclusionary in the previous config but is now exclusionary (or vice versa).
            // A flip from inclusionary → exclusionary causes removals of the users who were being included from that source.
            var flippedLines = await AttributeFlippedGroupFamilyPartsForRemovesAsync(parts, historicalTransitions, previousRunParts, removedSet, totalSampled, previousPartFiles, partFiles, groupNames);
            lines.AddRange(flippedLines);

            // A newly added exclusionary rule can remove matching destination members.
            var newExclLines = await AttributeNewExclusionaryGroupFamilyPartsForRemovesAsync(parts, historicalTransitions, removedSet, totalSampled, partFiles, groupNames);
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
                sbEmpty.AppendLine("- HARD RULE: do NOT name any specific source group, HR rule, or exclusionary source as the cause of removals, and do NOT infer one by process of elimination from the adds attribution or from sources listed under 'Job filter' or 'Configuration as of this run'. Instead use the descriptive membership-rule fallback: say these users were in the group but no longer match its membership rule, describing that rule in plain English from the 'Membership rules as of this run' glossary (this restates the group's own definition, it does not name a specific source). NEVER say the reason could not be determined. Pair with pattern 4 for threshold-blocked runs.");
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

        // Phase 2 removes walk-back: when single-step attribution hedges (class B: previous ADF table pruned so the SQL diff can't run; class D: removed users dropped out before the immediately-previous run), scan prior runs' inclusionary per-part SOURCE blobs (SqlMembership_i and GroupMembership_i, persist ~30 days independent of ADF) most-recent-first to find each removed user's last presence and attribute the removal to that rule; explanation-only, bounded by WalkBackMaxRuns and the per-part caps, gated by the caller to the same-query stretch so blob tags stay valid.
        private async Task<string> ComputeRemovesWalkBackAttributionAsync(
            IReadOnlyList<QueryPartInfo> parts,
            IReadOnlyCollection<Guid> removedUsers,
            string targetGroupId,
            IReadOnlyList<Models.SyncJobHistory.SyncJobHistory> priorRuns,
            IReadOnlyDictionary<int, string>? managerNames,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            if (removedUsers == null || removedUsers.Count == 0) return string.Empty;
            if (priorRuns == null || priorRuns.Count == 0) return string.Empty;
            if (string.IsNullOrEmpty(targetGroupId)) return string.Empty;

            // Only inclusionary source parts can explain an organic drop-out; exclusionary and config-driven removals are already handled by the single-step passes.
            var sqlParts = (parts ?? new List<QueryPartInfo>())
                .Where(p => !p.Exclusionary && string.Equals(p.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase))
                .Take(MaxSqlRulesToIntersect);
            var groupParts = (parts ?? new List<QueryPartInfo>())
                .Where(p => !p.Exclusionary && IsGroupFamilyAttributionType(p.Type))
                .Take(MaxGroupPartsToAttribute);
            var sourceParts = sqlParts.Concat(groupParts).ToList();
            if (sourceParts.Count == 0) return string.Empty;

            var remaining = new HashSet<Guid>(removedUsers.Take(MaxAddsForRuleAttribution));
            var totalSampled = remaining.Count;
            if (totalSampled == 0) return string.Empty;

            var perPart = new Dictionary<int, WalkBackPartResult>();

            foreach (var run in priorRuns)
            {
                if (remaining.Count == 0) break;
                if (run == null || run.RunId == Guid.Empty) continue;

                Dictionary<string, BlobResult> catalog;
                try
                {
                    catalog = await _blobStorageRepository.FindPartFilesByRunIdAsync(targetGroupId, run.RunId.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Removes walk-back: failed to enumerate part blobs for prior run {RunId}; skipping it.", run.RunId);
                    continue;
                }
                if (catalog == null || catalog.Count == 0) continue;

                // Find which still-unexplained removed users were present in this run's inclusionary sources; collect first then attribute, so a user in two parts of the same run is attributed once (first part in order).
                var foundThisRun = new Dictionary<Guid, int>();
                foreach (var part in sourceParts)
                {
                    if (foundThisRun.Count == remaining.Count) break; // every remaining user already located in this run
                    var tag = $"{part.Type}_{part.Index + 1}";
                    if (!catalog.TryGetValue(tag, out var blob) || blob.BlobStatus != BlobStatus.Found) continue;

                    HashSet<Guid> members;
                    try
                    {
                        members = await _blobStorageRepository.ExtractGroupMembershipSourceMembersAsync(blob.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Removes walk-back: failed to read source blob {Path} for prior run {RunId}; skipping that part.", blob.Path, run.RunId);
                        continue;
                    }
                    if (members == null || members.Count == 0) continue;

                    foreach (var g in remaining)
                    {
                        if (!foundThisRun.ContainsKey(g) && members.Contains(g))
                        {
                            foundThisRun[g] = part.Index;
                        }
                    }
                }

                // priorRuns is most-recent-first, so the FIRST run in which a user is found is their last presence.
                foreach (var kvp in foundThisRun)
                {
                    remaining.Remove(kvp.Key);
                    if (!perPart.TryGetValue(kvp.Value, out var res))
                    {
                        res = new WalkBackPartResult
                        {
                            Part = sourceParts.First(p => p.Index == kvp.Value),
                            LastSeen = run.UpdatedAt
                        };
                        perPart[kvp.Value] = res;
                    }
                    res.Count++;
                    if (run.UpdatedAt > res.LastSeen) res.LastSeen = run.UpdatedAt;
                }
            }

            if (perPart.Count == 0) return string.Empty;

            // Reuse the rule-contribution phrasing from the single-step attribution and add the last observed contribution time.
            var lines = new List<string>();
            foreach (var res in perPart.Values.OrderBy(r => r.Part.Index))
            {
                var bucket = BucketAttribution(res.Count, totalSampled);
                var lastSeen = res.LastSeen.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
                if (string.Equals(res.Part.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase))
                {
                    var label = FormatSqlPartLabel(res.Part, managerNames);
                    lines.Add($"- {label}: {bucket} of the removed users no longer match this rule (they were last present in it around {lastSeen}, per prior-run source history)");
                }
                else
                {
                    var label = FormatGroupFamilyPartLabel(res.Part, groupNames);
                    lines.Add($"- {label}: {bucket} of the removed users stopped contributing through this rule (their selected contribution was last present around {lastSeen}, per prior-run source history)");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Per-part attribution for removed users (qualitative buckets — use these terms verbatim in your output; DO NOT translate them to exact counts or percentages):");
            foreach (var line in lines) sb.AppendLine(line);
            return sb.ToString().TrimEnd();
        }

        // True when the single-step removes attribution produced no usable signal (empty or the explicit "(no attributable source found …)" marker); gates the fallback to the durable walk-back.
        private static bool HasNoRemovesAttribution(string? attribution)
        {
            return string.IsNullOrEmpty(attribution)
                || attribution.Contains("(no attributable source found", StringComparison.Ordinal);
        }

        // Phase 4 telemetry: name the attribution layer that answered (descriptive == the never-hedge rule-based fallback) so the hedge-rate drop is measurable without logging prompt text.
        private static string ClassifyAttributionLayer(string? attribution, bool hasChanges, bool hasConfigChange)
        {
            if (!hasChanges) return "none";
            if (hasConfigChange) return "config";
            if (string.IsNullOrEmpty(attribution)) return "none";
            if (attribution.Contains("no attributable source found", StringComparison.Ordinal)) return "descriptive";
            if (attribution.Contains("per prior-run source history", StringComparison.Ordinal)) return "blob-walkback";
            if (attribution.Contains("in the latest HR data (current state", StringComparison.Ordinal)) return "latest-adf";
            return "adf-exact";
        }

        // Accumulator for one inclusionary source part across the removes walk-back.
        private sealed class WalkBackPartResult
        {
            public QueryPartInfo Part { get; set; } = null!;
            public int Count { get; set; }
            public DateTime LastSeen { get; set; }
        }

        // Phase 3 current-state color: when the exact-run ADF snapshot is gone (pruned or null AdfRunId) the single-step SQL diff can't say why a user was removed, so re-evaluate each inclusionary SQL part against the LATEST ADF table as present-tense color; the contradiction guard suppresses any removed user who still matches the rule today (never assert a reason current data contradicts, per plan Scenario 5), so only users who currently fail every inclusionary SQL part are colored; skipped entirely when the exact-run snapshot still exists (that snapshot is the removal-time truth the single-step pass already owns).
        private async Task<List<string>> ComputeRemovesCurrentStateColorLinesAsync(
            IReadOnlyList<QueryPartInfo> parts,
            IReadOnlyCollection<Guid> removedUsers,
            Guid? runAdfRunId,
            IReadOnlyDictionary<int, string>? managerNames)
        {
            var empty = new List<string>();
            if (removedUsers == null || removedUsers.Count == 0) return empty;

            var sqlParts = (parts ?? new List<QueryPartInfo>())
                .Where(p => !p.Exclusionary && string.Equals(p.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase))
                .Take(MaxSqlRulesToIntersect)
                .ToList();
            if (sqlParts.Count == 0) return empty;

            // Guard 1: only use the latest table when the exact-run snapshot is unavailable; if it still exists it is the removal-time truth and the single-step pass already owns the reason.
            if (runAdfRunId.HasValue && runAdfRunId.Value != Guid.Empty)
            {
                var exactTable = runAdfRunId.Value.ToString().Replace("-", string.Empty);
                try
                {
                    if (await _sqlMembershipRepository.CheckIfTableExistsAsync(exactTable)) return empty;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Current-state color: failed to probe exact-run ADF table {Table}; skipping.", exactTable);
                    return empty;
                }
            }

            string latestTable;
            try
            {
                latestTable = (await _adfRunHistoryRepository.GetLatestAdfRunAsync() ?? throw new InvalidOperationException(AdfRunHistory.NoEligibleRunMessage)).GetTableName();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Current-state color: failed to fetch latest ADF run id; skipping.");
                return empty;
            }

            // Union of members currently matching each inclusionary SQL part (whole part: manager root + filter) in the latest table.
            // Emit combined current-state color ONLY when EVERY inclusionary SQL part was read successfully. A partial union (one part
            // failed to read) could mark a removed user as "no longer matches" even though they still match a part we simply failed to
            // evaluate, so any read failure aborts the whole current-state signal rather than risk a false "not matching" claim on a
            // multi-rule job; the walk-back and single-step layers still carry whatever attribution they already found.
            var inclusionUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allPartsRead = true;
            foreach (var part in sqlParts)
            {
                List<string?>? members;
                try
                {
                    members = await FetchSqlPartMembersAsync(part, latestTable);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Current-state color: failed to read latest-table members for part #{Index}; skipping current-state color to avoid partial attribution.", part.Index);
                    allPartsRead = false;
                    break;
                }
                if (members == null)
                {
                    allPartsRead = false;
                    break;
                }
                foreach (var m in members)
                {
                    if (!string.IsNullOrWhiteSpace(m)) inclusionUnion.Add(m!);
                }
            }
            if (!allPartsRead) return empty; // partial evaluation is unsafe for "no longer matches any rule" claims

            // Contradiction guard: a removed user still in ANY inclusionary part matches the rule today -> suppress; only users absent from every inclusionary part are safe to color as currently-not-matching.
            var sampled = removedUsers.Take(MaxAddsForRuleAttribution).Select(g => g.ToString()).ToList();
            var totalSampled = sampled.Count;
            if (totalSampled == 0) return empty;
            var currentlyNotMatching = sampled.Count(id => !inclusionUnion.Contains(id));
            if (currentlyNotMatching == 0) return empty; // every removed user still matches -> pure contradiction, stay silent (walk-back carries the run)

            var bucket = BucketAttribution(currentlyNotMatching, totalSampled);
            var label = sqlParts.Count == 1
                ? FormatSqlPartLabel(sqlParts[0], managerNames)
                : "the group's inclusionary HR rule(s)";
            return new List<string>
            {
                $"- {label}: {bucket} of the removed users no longer match this rule in the latest HR data (current state, not the removal-time snapshot)"
            };
        }

        // Merge Phase 3 current-state color into the removes attribution: replace the no-signal marker outright, else append the color lines under the existing per-part section.
        private static string ComposeRemovesAttributionWithCurrentState(string existing, List<string> currentStateLines)
        {
            if (currentStateLines == null || currentStateLines.Count == 0) return existing;

            if (HasNoRemovesAttribution(existing))
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("Per-part attribution for removed users (qualitative buckets — use these terms verbatim in your output; DO NOT translate them to exact counts or percentages):");
                foreach (var line in currentStateLines) sb.AppendLine(line);
                return sb.ToString().TrimEnd();
            }

            var appended = new StringBuilder(existing);
            appended.AppendLine();
            foreach (var line in currentStateLines) appended.AppendLine(line);
            return appended.ToString().TrimEnd();
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
            if (part.IdentityTypes == null)
            {
                return null;
            }

            int? managerEmployeeId = null;
            if (int.TryParse(part.ManagerId, out var managerId) &&
                managerId > 0)
            {
                managerEmployeeId = managerId;
            }

            if (!managerEmployeeId.HasValue &&
                string.IsNullOrWhiteSpace(part.Filter))
            {
                return null;
            }

            var result =
                await _sqlMembershipRepository.EvaluateMembershipAsync(
                    new SqlMembershipEvaluationRequest(
                        managerEmployeeId,
                        part.ManagerDepth ?? 0,
                        part.Filter ?? string.Empty,
                        part.IdentityTypes.Value,
                        tableName));
            // An ID returned as both a user and an agent is one member, as in the uploaded membership file; counting it
            // twice would overstate the attribution bucket.
            return result.UserObjectIds
                .Concat(result.AgentObjectIds)
                .Distinct()
                .Select(identifier => (string?)identifier.ToString("D"))
                .ToList();
        }

        // Group-family removals compare each rule's selected contribution between runs.
        private async Task<string?> AttributeGroupFamilyPartForRemovesAsync(
            QueryPartInfo part,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, string>? groupNames,
            IReadOnlyDictionary<int, int> previousRunIndexByCurrentIndex)
        {
            var currentTag = $"{part.Type}_{part.Index + 1}";
            if (!partFiles.TryGetValue(currentTag, out var currentBlob) || currentBlob.BlobStatus != BlobStatus.Found) return null;

            // Blob filenames are position-based. An unmatched inserted rule has no previous blob to compare.
            if (!previousRunIndexByCurrentIndex.TryGetValue(part.Index, out var previousIndex))
            {
                return null;
            }
            var previousTag = $"{part.Type}_{previousIndex + 1}";
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
                return $"- {label}: {bucket} of the removed users now match this exclusionary rule";
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
                return $"- {label}: {bucket} of the removed users stopped contributing through this rule";
            }
        }

        // Attributes removed users to inclusionary group-family rules that no longer exist in the current configuration.
        private async Task<List<string>> AttributeDeletedGroupFamilyPartsForRemovesAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartTransition> historicalTransitions,
            IReadOnlyList<QueryPartInfo> previousRunParts,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            var deletedParts = GetOutstandingHistoricalComparison(
                    currentParts,
                    historicalTransitions)
                .Removed
                .Where(part =>
                    IsGroupFamilyAttributionType(part.Type)
                    && !part.Exclusionary)
                .DistinctBy(
                    GetQueryPartSemanticKey,
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (deletedParts.Count == 0) return lines;

            foreach (var prevPart in deletedParts)
            {
                var label = FormatGroupFamilyPartLabel(prevPart, groupNames);

                HashSet<Guid>? prevMembers = null;
                var previousRunPart = FindSemanticallyEqualPart(previousRunParts, prevPart);
                var previousTag = previousRunPart == null
                    ? null
                    : $"{previousRunPart.Type}_{previousRunPart.Index + 1}";
                if (previousTag != null
                    && previousPartFiles.TryGetValue(previousTag, out var blob)
                    && blob.BlobStatus == BlobStatus.Found)
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

                if (prevMembers != null)
                {
                    var matched = 0;
                    foreach (var g in prevMembers)
                    {
                        if (removedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Rule removed from the query in the recent config update (previously {label}): {bucket} of the removed users previously contributed through this now-deleted rule");
                    }

                    continue;
                }

                lines.Add($"- Rule removed from the query in the recent config update (previously {label}): per-part membership counts are unavailable, but this deleted rule is the likely cause of this run's removals");
            }
            return lines;
        }

        // Detects group-family sources whose inclusionary/exclusionary role flipped between the previous config and the current config.
        // A flip from inclusionary → exclusionary causes removals for users who were being included from that source.
        // A flip from exclusionary → inclusionary is not a cause of removals, so we only attribute the incl → excl direction here.
        private async Task<List<string>> AttributeFlippedGroupFamilyPartsForRemovesAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartTransition> historicalTransitions,
            IReadOnlyList<QueryPartInfo> previousRunParts,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            if (currentParts == null || currentParts.Count == 0) return lines;

            var flippedParts = GetOutstandingHistoricalComparison(
                    currentParts,
                    historicalTransitions)
                .Matched
                .Where(match =>
                    IsGroupFamilyAttributionType(match.Current.Type)
                    && !match.Previous.Exclusionary
                    && match.Current.Exclusionary)
                .Select(match => (match.Previous, match.Current))
                .DistinctBy(
                    match =>
                        $"{GetQueryPartSemanticKey(match.Previous)}->" +
                        GetQueryPartSemanticKey(match.Current),
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (flippedParts.Count == 0) return lines;

            foreach (var (prevPart, currentPart) in flippedParts.Take(MaxGroupPartsToAttribute))
            {
                var label = FormatGroupFamilyPartLabel(currentPart, groupNames);

                // Prefer the current selected contribution. A previous contribution is comparable only when
                // the role changed without also changing the selected identity population.
                HashSet<Guid>? sourceMembers = null;
                var currentTag =
                    $"{currentPart.Type}_{currentPart.Index + 1}";
                if (currentTag != null
                    && partFiles.TryGetValue(currentTag, out var currentBlob)
                    && currentBlob.BlobStatus == BlobStatus.Found)
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
                if (sourceMembers == null
                    && prevPart.IdentityTypes == currentPart.IdentityTypes)
                {
                    var previousRunPart = FindSemanticallyEqualPart(previousRunParts, prevPart);
                    var previousTag = previousRunPart == null
                        ? null
                        : $"{previousRunPart.Type}_{previousRunPart.Index + 1}";
                    if (previousTag != null
                        && previousPartFiles.TryGetValue(previousTag, out var previousBlob)
                        && previousBlob.BlobStatus == BlobStatus.Found)
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

                if (sourceMembers != null)
                {
                    var matched = 0;
                    foreach (var g in sourceMembers)
                    {
                        if (removedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Rule flipped from inclusionary to exclusionary in the recent config update ({label}): {bucket} of the removed users match this now-exclusionary rule");
                    }

                    continue;
                }

                lines.Add($"- Rule flipped from inclusionary to exclusionary in the recent config update ({label}): per-part membership counts are unavailable, but this role flip is the likely cause of this run's removals");
            }
            return lines;
        }

        // Attributes removals to newly added exclusionary rules.
        private async Task<List<string>> AttributeNewExclusionaryGroupFamilyPartsForRemovesAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartTransition> historicalTransitions,
            HashSet<string> removedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            if (currentParts == null || currentParts.Count == 0) return lines;

            var newExclParts = GetOutstandingHistoricalComparison(
                    currentParts,
                    historicalTransitions)
                .Added
                .Where(part =>
                    IsGroupFamilyAttributionType(part.Type)
                    && part.Exclusionary)
                .DistinctBy(
                    GetQueryPartSemanticKey,
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (newExclParts.Count == 0) return lines;

            foreach (var part in newExclParts)
            {
                var label = FormatGroupFamilyPartLabel(part, groupNames);
                var currentTag = $"{part.Type}_{part.Index + 1}";
                HashSet<Guid>? sourceMembers = null;
                if (currentTag != null
                    && partFiles.TryGetValue(currentTag, out var blob)
                    && blob.BlobStatus == BlobStatus.Found)
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

                if (sourceMembers != null)
                {
                    var matched = 0;
                    foreach (var g in sourceMembers)
                    {
                        if (removedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- New exclusionary rule added in the recent config update ({label}): {bucket} of the removed users match this newly added exclusionary rule");
                    }

                    continue;
                }

                lines.Add($"- New exclusionary rule added in the recent config update ({label}): per-part membership counts are unavailable, but this newly added exclusionary rule is the likely cause of this run's removals");
            }
            return lines;
        }

        // Attributes additions to removed exclusionary rules.
        private async Task<List<string>> AttributeDeletedExclusionaryGroupFamilyPartsForAddsAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartTransition> historicalTransitions,
            IReadOnlyList<QueryPartInfo> previousRunParts,
            HashSet<string> addedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> previousPartFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            var deletedExclParts = GetOutstandingHistoricalComparison(
                    currentParts,
                    historicalTransitions)
                .Removed
                .Where(part =>
                    IsGroupFamilyAttributionType(part.Type)
                    && part.Exclusionary)
                .DistinctBy(
                    GetQueryPartSemanticKey,
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaxGroupPartsToAttribute)
                .ToList();
            if (deletedExclParts.Count == 0) return lines;

            foreach (var prevPart in deletedExclParts)
            {
                var label = FormatGroupFamilyPartLabel(prevPart, groupNames);

                HashSet<Guid>? prevMembers = null;
                var previousRunPart = FindSemanticallyEqualPart(previousRunParts, prevPart);
                var previousTag = previousRunPart == null
                    ? null
                    : $"{previousRunPart.Type}_{previousRunPart.Index + 1}";
                if (previousTag != null
                    && previousPartFiles.TryGetValue(previousTag, out var blob)
                    && blob.BlobStatus == BlobStatus.Found)
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

                if (prevMembers != null)
                {
                    var matched = 0;
                    foreach (var g in prevMembers)
                    {
                        if (addedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Exclusionary rule removed from the query in the recent config update (previously {label}): {bucket} of the added users were previously blocked by this now-removed exclusionary rule");
                    }

                    continue;
                }

                lines.Add($"- Exclusionary rule removed from the query in the recent config update (previously {label}): per-part membership counts are unavailable, but this deleted exclusionary rule is the likely cause of this run's additions");
            }
            return lines;
        }

        private static QueryPartComparison GetOutstandingHistoricalComparison(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartTransition> historicalTransitions)
        {
            var appliedParts = historicalTransitions.Count == 0
                ? currentParts
                : historicalTransitions[^1].Previous;
            return MatchQueryParts(appliedParts, currentParts);
        }

        private static QueryPartInfo? FindSemanticallyEqualPart(
            IReadOnlyList<QueryPartInfo> parts,
            QueryPartInfo part)
        {
            return parts.FirstOrDefault(candidate => AreSemanticallyEqual(candidate, part));
        }

        // Attributes additions to rules that flipped from exclusionary to inclusionary.
        private async Task<List<string>> AttributeFlippedGroupFamilyPartsForAddsAsync(
            IReadOnlyList<QueryPartInfo> currentParts,
            IReadOnlyList<QueryPartTransition> historicalTransitions,
            HashSet<string> addedSet,
            int totalSampled,
            IReadOnlyDictionary<string, BlobResult> partFiles,
            IReadOnlyDictionary<Guid, string>? groupNames)
        {
            var lines = new List<string>();
            if (currentParts == null || currentParts.Count == 0) return lines;

            var flippedParts = GetOutstandingHistoricalComparison(
                    currentParts,
                    historicalTransitions)
                .Matched
                .Where(match =>
                    IsGroupFamilyAttributionType(match.Current.Type)
                    && match.Previous.Exclusionary
                    && !match.Current.Exclusionary)
                .Select(match => match.Current)
                .DistinctBy(
                    GetQueryPartSemanticKey,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (flippedParts.Count == 0) return lines;

            foreach (var currentPart in flippedParts.Take(MaxGroupPartsToAttribute))
            {
                var label = FormatGroupFamilyPartLabel(currentPart, groupNames);
                var currentTag =
                    $"{currentPart.Type}_{currentPart.Index + 1}";
                HashSet<Guid>? sourceMembers = null;
                if (currentTag != null
                    && partFiles.TryGetValue(currentTag, out var blob)
                    && blob.BlobStatus == BlobStatus.Found)
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

                if (sourceMembers != null)
                {
                    var matched = 0;
                    foreach (var g in sourceMembers)
                    {
                        if (addedSet.Contains(g.ToString())) matched++;
                    }
                    if (matched > 0)
                    {
                        var bucket = BucketAttribution(matched, totalSampled);
                        lines.Add($"- Rule flipped from exclusionary to inclusionary in the recent config update ({label}): {bucket} of the added users match this newly inclusionary rule");
                    }

                    continue;
                }

                lines.Add($"- Rule flipped from exclusionary to inclusionary in the recent config update ({label}): per-part membership counts are unavailable, but this role flip is the likely cause of this run's additions");
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
            var role = part.Exclusionary
                ? "Exclusionary"
                : "Inclusionary";
            var selection = string.Equals(
                part.Type,
                "GroupMembership",
                StringComparison.OrdinalIgnoreCase)
                ? $"; {DescribeIdentitySelection(part.IdentityTypes, part.Type)}"
                : string.Empty;
            return $"{role} {part.Type} part #{part.Index + 1} ({sourceLabel}{selection})";
        }
    }
}
