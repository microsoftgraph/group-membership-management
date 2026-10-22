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
using Models.AdfRunHistory;

namespace Services
{
    public partial class GetRunExplanationHandler
    {
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

        private static bool IsDryRunEnabledInChangeDetails(string? changeDetails)
        {
            if (string.IsNullOrWhiteSpace(changeDetails))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(changeDetails);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (document.RootElement.TryGetProperty("isDryRunEnabled", out var camel)
                    && camel.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return camel.GetBoolean();
                }

                if (document.RootElement.TryGetProperty("IsDryRunEnabled", out var pascal)
                    && pascal.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return pascal.GetBoolean();
                }
            }
            catch (JsonException) { }

            return false;
        }

        private static string? NormalizeQuery(string? query)
        {
            return ChangeHistoryQueryNormalizer.Normalize(query);
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
            var comparison = MatchQueryParts(
                previousParts ?? new List<QueryPartInfo>(),
                currentParts ?? new List<QueryPartInfo>());

            // Parts added
            foreach (var part in comparison.Added)
            {
                var role = part.Exclusionary ? "exclusionary" : "inclusionary";
                if (part.Type.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(
                        $"- New {role} group source added: {FormatGroupRef(part.Source, groupNames)} " +
                        $"(selecting {DescribeIdentitySelection(part.IdentityTypes, part.Type)})");
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
            foreach (var part in comparison.Removed)
            {
                var role = part.Exclusionary ? "exclusionary" : "inclusionary";
                var roleLabel = char.ToUpperInvariant(role[0]) + role.Substring(1);
                if (part.Type.Equals("GroupMembership", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(
                        $"- {roleLabel} group source removed: {FormatGroupRef(part.Source, groupNames)} " +
                        $"(selected {DescribeIdentitySelection(part.IdentityTypes, part.Type)})");
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
            foreach (var match in comparison.Matched)
            {
                var prevPart = match.Previous;
                var currPart = match.Current;

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

                if (prevPart.IdentityTypes != currPart.IdentityTypes)
                {
                    sb.AppendLine(
                        $"- Identity types changed from {DescribeIdentitySelection(prevPart.IdentityTypes, prevPart.Type)} " +
                        $"to {DescribeIdentitySelection(currPart.IdentityTypes, currPart.Type)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static QueryPartComparison MatchQueryParts(
            IReadOnlyList<QueryPartInfo> previousParts,
            IReadOnlyList<QueryPartInfo> currentParts)
        {
            var unmatchedPrevious = previousParts.ToList();
            var unmatchedCurrent = currentParts.ToList();
            var matched = new List<QueryPartMatch>();

            foreach (var currentPart in currentParts)
            {
                var previousPart = unmatchedPrevious.FirstOrDefault(
                    candidate => AreSemanticallyEqual(candidate, currentPart));
                if (previousPart == null)
                {
                    continue;
                }

                matched.Add(new QueryPartMatch(previousPart, currentPart));
                unmatchedPrevious.Remove(previousPart);
                unmatchedCurrent.Remove(currentPart);
            }

            foreach (var currentPart in unmatchedCurrent.ToList())
            {
                var previousPart = unmatchedPrevious
                    .Where(candidate => HasSameBaseIdentity(candidate, currentPart))
                    .OrderByDescending(candidate =>
                        GetStructuralSimilarity(candidate, currentPart))
                    .ThenBy(candidate =>
                        Math.Abs(candidate.Index - currentPart.Index))
                    .FirstOrDefault();
                if (previousPart == null)
                {
                    continue;
                }

                matched.Add(new QueryPartMatch(previousPart, currentPart));
                unmatchedPrevious.Remove(previousPart);
                unmatchedCurrent.Remove(currentPart);
            }

            return new QueryPartComparison(
                matched.OrderBy(match => match.Current.Index).ToList(),
                unmatchedCurrent.OrderBy(part => part.Index).ToList(),
                unmatchedPrevious.OrderBy(part => part.Index).ToList());
        }

        private static bool AreSemanticallyEqual(
            QueryPartInfo previousPart,
            QueryPartInfo currentPart)
        {
            return HasSameBaseIdentity(previousPart, currentPart)
                && EqualsNormalized(previousPart.Filter, currentPart.Filter)
                && string.Equals(
                    previousPart.ManagerId,
                    currentPart.ManagerId,
                    StringComparison.Ordinal)
                && previousPart.ManagerDepth == currentPart.ManagerDepth
                && previousPart.Exclusionary == currentPart.Exclusionary
                && previousPart.IdentityTypes == currentPart.IdentityTypes;
        }

        private static bool HasSameBaseIdentity(
            QueryPartInfo previousPart,
            QueryPartInfo currentPart)
        {
            if (!string.Equals(
                previousPart.Type,
                currentPart.Type,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var previousHasSource =
                !string.IsNullOrWhiteSpace(previousPart.Source);
            var currentHasSource =
                !string.IsNullOrWhiteSpace(currentPart.Source);
            if (previousHasSource || currentHasSource)
            {
                return previousHasSource
                    && currentHasSource
                    && EqualsNormalized(
                        previousPart.Source,
                        currentPart.Source);
            }

            return true;
        }

        private static int GetStructuralSimilarity(
            QueryPartInfo previousPart,
            QueryPartInfo currentPart)
        {
            var score = 0;
            if (previousPart.IdentityTypes == currentPart.IdentityTypes)
            {
                score += 8;
            }
            if (previousPart.Exclusionary == currentPart.Exclusionary)
            {
                score += 4;
            }
            if (EqualsNormalized(previousPart.Filter, currentPart.Filter))
            {
                score += 2;
            }
            if (string.Equals(
                previousPart.ManagerId,
                currentPart.ManagerId,
                StringComparison.Ordinal)
                && previousPart.ManagerDepth == currentPart.ManagerDepth)
            {
                score++;
            }

            return score;
        }

        private static bool EqualsNormalized(string? first, string? second) =>
            string.Equals(
                first?.Trim(),
                second?.Trim(),
                StringComparison.OrdinalIgnoreCase);

        private sealed record QueryPartMatch(
            QueryPartInfo Previous,
            QueryPartInfo Current);

        private sealed record QueryPartComparison(
            IReadOnlyList<QueryPartMatch> Matched,
            IReadOnlyList<QueryPartInfo> Added,
            IReadOnlyList<QueryPartInfo> Removed);

        private sealed record QueryPartTransition(
            IReadOnlyList<QueryPartInfo> Previous,
            IReadOnlyList<QueryPartInfo> Current);

        private static List<IReadOnlyList<QueryPartInfo>> BuildHistoricalQuerySnapshots(
            IReadOnlyList<QueryPartInfo> currentParts,
            IEnumerable<string?> historicalQueries)
        {
            var snapshots = new List<IReadOnlyList<QueryPartInfo>>
            {
                currentParts
            };
            var previousSnapshotKey = GetQuerySnapshotKey(currentParts);

            foreach (var query in historicalQueries)
            {
                var candidateParts = ParseQueryParts(query);
                if (candidateParts == null)
                {
                    continue;
                }

                var candidateKey = GetQuerySnapshotKey(candidateParts);
                if (string.Equals(
                    candidateKey,
                    previousSnapshotKey,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                snapshots.Add(candidateParts);
                previousSnapshotKey = candidateKey;
            }

            return snapshots;
        }

        private static List<QueryPartTransition> BuildHistoricalQueryTransitions(
            IReadOnlyList<IReadOnlyList<QueryPartInfo>> snapshots)
        {
            var transitions = new List<QueryPartTransition>();
            for (var index = 0; index + 1 < snapshots.Count; index++)
            {
                transitions.Add(new QueryPartTransition(
                    snapshots[index + 1],
                    snapshots[index]));
            }

            return transitions;
        }

        private static string GetQuerySnapshotKey(
            IReadOnlyList<QueryPartInfo> parts) =>
            string.Join(
                "\n",
                parts.Select(GetQueryPartSemanticKey));

        private static string GetQueryPartSemanticKey(QueryPartInfo part) =>
            string.Join(
                "|",
                part.Type?.Trim() ?? string.Empty,
                part.Source?.Trim() ?? string.Empty,
                part.Filter?.Trim() ?? string.Empty,
                part.ManagerId?.Trim() ?? string.Empty,
                part.ManagerDepth?.ToString(CultureInfo.InvariantCulture)
                    ?? string.Empty,
                part.Exclusionary.ToString(CultureInfo.InvariantCulture),
                part.IdentityTypes?.ToString() ?? string.Empty);

        internal static string DescribeIdentitySelection(
            IdentityTypeSet? selection,
            string? sourceType = null)
        {
            if (selection == null)
            {
                return "an unreadable selection";
            }

            var isGroupMembership = string.Equals(
                sourceType,
                "GroupMembership",
                StringComparison.OrdinalIgnoreCase);
            var includesUsers =
                selection.Value.Includes(IdentityType.Users);
            var includesAgents =
                selection.Value.Includes(IdentityType.Agents);

            if (includesUsers && includesAgents)
            {
                return isGroupMembership
                    ? "people and agent users"
                    : "employees and the AI agents they own";
            }

            if (includesAgents)
            {
                return isGroupMembership
                    ? "only agent users"
                    : "only the AI agents owned by matching employees";
            }

            return isGroupMembership
                ? "only people"
                : "only employees";
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

                    var isSqlMembership = string.Equals(
                        part.Type,
                        "SqlMembership",
                        StringComparison.OrdinalIgnoreCase);
                    var isGroupMembership = string.Equals(
                        part.Type,
                        "GroupMembership",
                        StringComparison.OrdinalIgnoreCase);
                    if (isSqlMembership || isGroupMembership)
                    {
                        var omittedDefault = isGroupMembership
                            ? IdentityTypeSet.GroupDefault
                            : IdentityTypeSet.UsersOnly;
                        var allowedTypes = isGroupMembership
                            ? IdentityTypeSet.GroupDefault
                            : IdentityTypeSet.All;
                        part.IdentityTypes = IdentityTypeSelection.TryParse(
                            element,
                            omittedDefault,
                            allowedTypes,
                            out var selection,
                            out _)
                            ? selection
                            : null;
                    }

                    parts.Add(part);
                }

                return parts;
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
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

        // Coarse source-shape of a job's query, used to pick owner-friendly upstream-fallback wording:
        // an HrData job must never be described as having a "source group".
        public enum JobSourceKind { None, HrData, Group, Mixed }

        // Classifies a job by its source parts: HrData if any SqlMembership part, Group if any group-referencing
        // part (GroupMembership/GroupOwnership/TeamsChannelMembership), Mixed if both, None otherwise.
        public static JobSourceKind ClassifyJobSourceKind(IReadOnlyList<QueryPartInfo>? parts)
        {
            if (parts == null || parts.Count == 0) return JobSourceKind.None;

            var hasHrData = false;
            var hasGroup = false;
            foreach (var part in parts)
            {
                if (string.Equals(part.Type, "SqlMembership", StringComparison.OrdinalIgnoreCase)) hasHrData = true;
                if (IsGroupReferencingPartType(part.Type)) hasGroup = true;
            }

            if (hasHrData && hasGroup) return JobSourceKind.Mixed;
            if (hasHrData) return JobSourceKind.HrData;
            if (hasGroup) return JobSourceKind.Group;
            return JobSourceKind.None;
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
                string? latestTable = null;
                try
                {
                    latestTable = (await _adfRunHistoryRepository.GetLatestAdfRunAsync() ?? throw new InvalidOperationException(AdfRunHistory.NoEligibleRunMessage)).GetTableName();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch latest ADF run id for manager-name fallback.");
                }
                if (!string.IsNullOrWhiteSpace(latestTable))
                {
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
    }
}
