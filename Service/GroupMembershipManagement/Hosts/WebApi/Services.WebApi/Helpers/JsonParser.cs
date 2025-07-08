// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Text.Json.Nodes;

namespace Services.Helpers
{
    internal static class JsonParser
    {
        /// <summary>
        /// Parses a query JSON string and extracts all GroupMembership source group IDs.
        /// Returns null if the query is invalid or contains non-GroupMembership sources.
        /// </summary>
        /// <param name="query">The JSON query string to parse</param>
        /// <returns>List of group IDs if all sources are GroupMembership, null otherwise</returns>
        internal static List<Guid>? GetGroupMembershipSourceIds(string query)
        {
            try
            {
                var queryArray = JsonNode.Parse(query)?.AsArray();
                if (queryArray == null || queryArray.Count == 0)
                    return null;

                var groupIds = new List<Guid>();

                foreach (var item in queryArray)
                {
                    var sourceObject = item?.AsObject();
                    if (sourceObject == null)
                        return null;

                    var typeValue = sourceObject["type"]?.GetValue<string>();
                    if (typeValue != "GroupMembership")
                        return null;

                    var sourceValue = sourceObject["source"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(sourceValue) || !Guid.TryParse(sourceValue, out var groupId))
                        return null;

                    groupIds.Add(groupId);
                }

                return groupIds.Count > 0 ? groupIds : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
