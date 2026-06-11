// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace GraphUpdater.Helpers
{
    internal static class JsonParser
    {
        internal static AzureADGroup GetDestination(SyncJob synJob)
        {
            var destination = new AzureADGroup
            {
                Type = synJob.MembershipType.ToString(),
                ObjectId = synJob.Group.GroupId
            };

            return destination;
        }

        internal static string GetQueryTypes(string query)
        {
            var queries = JsonNode.Parse(query).AsArray();
            var queryTypeCounts = new Dictionary<string, int>();
            var queryTypes = queries.Select(x => x["type"])
                                       .OfType<JsonValue>()
                                       .Select(x => x.GetValue<string>())
                                       .ToList();

            foreach ( var type in queryTypes)
            {
                if (queryTypeCounts.ContainsKey(type))
                {
                    queryTypeCounts[type]++;
                }
                else
                {
                    queryTypeCounts[type] = 1;
                }
            }

            var sourceTypesCounts = new StringBuilder();

            sourceTypesCounts.Append("{");

            foreach (var kvp in queryTypeCounts)
            {
                if (sourceTypesCounts.Length > 1)
                {
                    sourceTypesCounts.Append(",");
                }

                sourceTypesCounts.Append($"{kvp.Key}:{kvp.Value}");
            }

            sourceTypesCounts.Append("}");

            return sourceTypesCounts.ToString();
        }
    }
}
