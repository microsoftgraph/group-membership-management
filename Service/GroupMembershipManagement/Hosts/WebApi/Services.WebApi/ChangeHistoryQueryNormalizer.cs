// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.SyncJobChange;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Services
{
    internal sealed record ConfigurationChangeComparison(
        SyncJobChange CurrentLayoutStart,
        string? CurrentQuery,
        string? PreviousQuery,
        bool HasPriorSnapshot);

    internal static class ConfigurationChangeHistory
    {
        public static ConfigurationChangeComparison? Analyze(
            IReadOnlyList<SyncJobChange>? changes,
            Func<SyncJobChange, string?> extractQuery)
        {
            if (changes == null || changes.Count == 0)
            {
                return null;
            }

            var currentLayoutStart = changes[0];
            var currentQuery = extractQuery(currentLayoutStart);
            var currentQueryNormalized = ChangeHistoryQueryNormalizer.Normalize(currentQuery);
            var hasPriorSnapshot = false;
            string? previousQuery = null;

            foreach (var change in changes.Skip(1))
            {
                var candidateQuery = extractQuery(change);
                if (candidateQuery == null)
                {
                    break;
                }

                if (string.Equals(
                    ChangeHistoryQueryNormalizer.Normalize(candidateQuery),
                    currentQueryNormalized,
                    StringComparison.OrdinalIgnoreCase))
                {
                    currentLayoutStart = change;
                    hasPriorSnapshot = true;
                    continue;
                }

                previousQuery = candidateQuery;
                break;
            }

            return new ConfigurationChangeComparison(
                currentLayoutStart,
                currentQuery,
                previousQuery,
                hasPriorSnapshot);
        }

        public static bool HasChangeEvidence(
            ConfigurationChangeComparison? comparison)
        {
            return comparison != null
                && (comparison.PreviousQuery != null
                    || !string.Equals(
                        comparison.CurrentLayoutStart.ChangeReason,
                        SyncJobChangeReason.ScheduledNow.ToString(),
                        StringComparison.OrdinalIgnoreCase));
        }
    }

    // Produces comparison text for change-history query snapshots. Snapshots that describe the same rules produce the
    // same text: property order is ignored, a valid identityTypes list is written in one fixed order, a SQL part
    // without identityTypes is read as users only, and a group part without it is read as users and agents. A part of
    // any type without exclusionary is read as false, the same way the jobs read it. Invalid identityTypes and
    // exclusionary values are kept as written. The result is for comparison only.
    internal static class ChangeHistoryQueryNormalizer
    {
        private const string SqlMembershipType = "SqlMembership";
        private const string GroupMembershipType = "GroupMembership";
        private const string ExclusionaryPropertyName = "exclusionary";

        public static string? Normalize(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            var trimmedQuery = query.Trim();

            try
            {
                var parts = JsonNode.Parse(trimmedQuery) as JsonArray;
                if (parts == null)
                {
                    return trimmedQuery;
                }

                var canonicalParts = new JsonArray();
                foreach (var part in parts)
                {
                    canonicalParts.Add(Canonicalize(part, isQueryPart: true));
                }

                return canonicalParts.ToJsonString();
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                return trimmedQuery;
            }
        }

        private static JsonNode? Canonicalize(JsonNode? node, bool isQueryPart)
        {
            var jsonObject = node as JsonObject;
            if (jsonObject != null)
            {
                var properties = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
                foreach (var property in jsonObject)
                {
                    properties.Add(property.Key, Canonicalize(property.Value, isQueryPart: false));
                }

                if (isQueryPart && !properties.ContainsKey(ExclusionaryPropertyName))
                {
                    properties.Add(ExclusionaryPropertyName, JsonValue.Create(false));
                }

                if (isQueryPart &&
                    TryGetSelectionContract(
                        jsonObject,
                        out var omittedDefault,
                        out var allowedTypes))
                {
                    if (!properties.TryGetValue(IdentityTypeSelection.PropertyName, out var selectionNode))
                    {
                        properties.Add(
                            IdentityTypeSelection.PropertyName,
                            CreateSelection(omittedDefault));
                    }
                    else if (TryReadSelection(selectionNode, allowedTypes, out var selection))
                    {
                        properties[IdentityTypeSelection.PropertyName] = CreateSelection(selection);
                    }
                }

                var canonicalObject = new JsonObject();
                foreach (var property in properties)
                {
                    canonicalObject.Add(property.Key, property.Value);
                }

                return canonicalObject;
            }

            var jsonArray = node as JsonArray;
            if (jsonArray != null)
            {
                var canonicalArray = new JsonArray();
                foreach (var item in jsonArray)
                {
                    canonicalArray.Add(Canonicalize(item, isQueryPart: false));
                }

                return canonicalArray;
            }

            return node?.DeepClone();
        }

        private static bool TryGetSelectionContract(
            JsonObject part,
            out IdentityTypeSet omittedDefault,
            out IdentityTypeSet allowedTypes)
        {
            omittedDefault = default;
            allowedTypes = default;

            if (!part.TryGetPropertyValue("type", out var typeNode) ||
                typeNode == null ||
                typeNode.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            var type = typeNode.GetValue<string>();
            if (string.Equals(type, SqlMembershipType, StringComparison.OrdinalIgnoreCase))
            {
                omittedDefault = IdentityTypeSet.UsersOnly;
                allowedTypes = IdentityTypeSet.All;
                return true;
            }

            if (string.Equals(type, GroupMembershipType, StringComparison.OrdinalIgnoreCase))
            {
                omittedDefault = IdentityTypeSet.GroupDefault;
                allowedTypes = IdentityTypeSet.GroupDefault;
                return true;
            }

            return false;
        }

        private static bool TryReadSelection(
            JsonNode? selectionNode,
            IdentityTypeSet allowedTypes,
            out IdentityTypeSet selection)
        {
            selection = default;
            if (selectionNode == null)
            {
                return false;
            }

            using var document = JsonDocument.Parse(selectionNode.ToJsonString());
            return IdentityTypeSelection.TryParseProperty(
                document.RootElement,
                allowedTypes,
                out selection,
                out _);
        }

        private static JsonArray CreateSelection(IdentityTypeSet selection)
        {
            var selectionArray = new JsonArray();
            foreach (var typeName in selection.ToTypeNames())
            {
                selectionArray.Add(typeName);
            }

            return selectionArray;
        }
    }
}
