// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Services.AutoApprover.Helpers
{
    internal static class JsonParser
    {
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