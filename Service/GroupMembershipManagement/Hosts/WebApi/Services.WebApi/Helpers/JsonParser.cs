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

        /// <summary>
        /// Checks if a query contains only GroupMembership sources for auto-approval.
        /// </summary>
        /// <param name="query">The JSON query string to parse</param>
        /// <returns>True if all sources are GroupMembership, false otherwise</returns>
        internal static bool IsGroupMembershipOnlyQuery(string query)
        {
            try
            {
                var queryArray = JsonNode.Parse(query)?.AsArray();
                if (queryArray == null || queryArray.Count == 0)
                    return false;

                foreach (var item in queryArray)
                {
                    var sourceObject = item?.AsObject();
                    if (sourceObject == null)
                        return false;

                    var typeValue = sourceObject["type"]?.GetValue<string>();
                    if (typeValue != "GroupMembership")
                        return false;

                    var sourceValue = sourceObject["source"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(sourceValue) || !Guid.TryParse(sourceValue, out _))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a query is a single SqlMembership query with a manager ID for auto-approval.
        /// </summary>
        /// <param name="query">The JSON query string to parse</param>
        /// <param name="expectedManagerId">The expected manager ID to match</param>
        /// <returns>True if it's a single SqlMembership query with matching manager ID, false otherwise</returns>
        internal static bool IsSingleSqlMembershipQueryWithManagerId(string query, int expectedManagerId)
        {
            try
            {
                var queryArray = JsonNode.Parse(query)?.AsArray();
                if (queryArray == null || queryArray.Count != 1)
                    return false;

                var item = queryArray[0];
                var sourceObject = item?.AsObject();
                if (sourceObject == null)
                    return false;

                var typeValue = sourceObject["type"]?.GetValue<string>();
                if (typeValue != "SqlMembership")
                    return false;

                var sourceValue = sourceObject["source"]?.AsObject();
                if (sourceValue == null)
                    return false;

                var managerObject = sourceValue["manager"]?.AsObject();
                if (managerObject == null)
                    return false;

                var managerIdValue = managerObject["id"]?.GetValue<int?>();
                if (!managerIdValue.HasValue)
                    return false;

                return managerIdValue.Value == expectedManagerId;
            }
            catch
            {
                return false;
            }
        }
    }
}
