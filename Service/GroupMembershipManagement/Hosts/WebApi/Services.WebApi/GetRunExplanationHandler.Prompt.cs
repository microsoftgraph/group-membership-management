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
    public partial class GetRunExplanationHandler
    {
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

        // Owner-friendly wording for pattern 5 / any upstream fallback. HR-data jobs must never be called a "source group".
        private static string UpstreamFallbackPhrase(JobSourceKind kind) => kind switch
        {
            JobSourceKind.HrData => "upstream HR data",
            JobSourceKind.Group => "upstream source group memberships",
            _ => "upstream membership sources",
        };

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
            string removesAttribution,
            JobSourceKind sourceKind)
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

            // Tells the model how to phrase pattern 5 / any upstream fallback for THIS job's source shape (the model cannot classify parts itself).
            var sourceKindLabel = sourceKind == JobSourceKind.None ? "Other" : sourceKind.ToString();
            var upstreamPhrase = UpstreamFallbackPhrase(sourceKind);

            return $@"Sync Run: {request.RunId}
Date: {endTime:u}
Status: {status}
Before sync: {runHistory.BeforeSyncUserCount ?? 0} members | After sync: {runHistory.AfterSyncUserCount ?? 0} members
{countsLine}{runSequenceNote}
Job source kind: {sourceKindLabel} (for pattern 5 or any upstream/unattributed fallback, refer to the source as ""{upstreamPhrase}""; never say ""source group"" unless the kind is Group or Mixed)

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

        // Detects the generic "reason could not be determined" hedge in any of its shapes: the exact FallbackExplanation, or a per-add/remove variant the model sometimes emits (e.g. "...The specific reason for the removals could not be determined from the available data."). A blank completion is deliberately NOT treated as a hedge here — that is an AI soft-failure handled separately so the honest FallbackExplanation is preserved.
        private static bool IsHedgeExplanation(string? explanation)
        {
            if (string.IsNullOrWhiteSpace(explanation))
            {
                return false;
            }

            var trimmed = explanation.Trim();
            return string.Equals(trimmed, FallbackExplanation, StringComparison.Ordinal)
                || trimmed.Contains("could not be determined", StringComparison.OrdinalIgnoreCase);
        }

        // Deterministic owner-friendly explanation for an established (non-first) run that actually moved (or, when threshold-blocked, proposed to move) members. Used as a success-path safety net when the model ships the generic hedge despite the never-hedge prompt rules, so the never-hedge behavior no longer depends on model compliance alone. Explanation text only — it never affects any membership calculation.
        private static string BuildNonInitialRuleExplanation(int added, int removed, bool isThresholdBlocked, string membershipRules)
        {
            var rulesInline = SummarizeRulesInline(membershipRules);

            // Never surface the internal "description is unavailable" / generic-criteria sentinels to owners: drop the detail and use the plain rule phrasing instead.
            if (!string.IsNullOrEmpty(rulesInline)
                && (rulesInline.Contains("description is unavailable", StringComparison.OrdinalIgnoreCase)
                    || rulesInline.Contains("the configured HR criteria", StringComparison.OrdinalIgnoreCase)))
            {
                rulesInline = string.Empty;
            }

            var ruleClause = string.IsNullOrEmpty(rulesInline)
                ? "the group's configured membership rules"
                : $"the group's membership rules ({rulesInline})";

            if (isThresholdBlocked)
            {
                if (added > 0 && removed > 0)
                {
                    return $"This sync's proposed changes were blocked because they exceeded the group's configured threshold. It would have added people who newly match {ruleClause} and removed people who no longer match.";
                }
                if (removed > 0)
                {
                    return $"This sync proposed removals that were blocked because the change exceeded the group's configured threshold. The proposed removals are people who were in the group but no longer match {ruleClause}.";
                }
                return $"This sync proposed additions that were blocked because the change exceeded the group's configured threshold. The proposed additions are people who newly match {ruleClause}.";
            }

            if (added > 0 && removed > 0)
            {
                return $"GMM added people who newly match {ruleClause} and removed people who were in the group but no longer match.";
            }
            if (removed > 0)
            {
                return $"GMM removed people who were in the group but no longer match {ruleClause}.";
            }
            return $"GMM added people who newly match {ruleClause}.";
        }
    }
}
