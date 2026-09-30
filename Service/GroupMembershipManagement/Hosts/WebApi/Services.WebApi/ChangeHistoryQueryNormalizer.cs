// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Services
{
    // Produces comparison text for change-history query snapshots. Snapshots that describe the same rules produce the
    // same text: property order is ignored, a valid identityTypes list is written in one fixed order, and a SQL part
    // without identityTypes is read as the users-only default, which is the value the post-deployment migration writes
    // explicitly. An invalid identityTypes value is kept as written. The result is for comparison only.
    internal static class ChangeHistoryQueryNormalizer
    {
        private const string SqlMembershipType = "SqlMembership";

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

                if (isQueryPart && IsSqlMembershipPart(jsonObject))
                {
                    if (!properties.TryGetValue(IdentityTypeSelection.PropertyName, out var selectionNode))
                    {
                        properties.Add(IdentityTypeSelection.PropertyName, CreateSelection(IdentityTypeSet.UsersOnly));
                    }
                    else if (TryReadSelection(selectionNode, out var selection))
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

        private static bool IsSqlMembershipPart(JsonObject part)
        {
            if (!part.TryGetPropertyValue("type", out var typeNode) ||
                typeNode == null ||
                typeNode.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            return string.Equals(
                typeNode.GetValue<string>(),
                SqlMembershipType,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadSelection(JsonNode? selectionNode, out IdentityTypeSet selection)
        {
            selection = default;
            if (selectionNode == null)
            {
                return false;
            }

            using var document = JsonDocument.Parse(selectionNode.ToJsonString());
            return IdentityTypeSelection.TryParseProperty(document.RootElement, out selection, out _);
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
