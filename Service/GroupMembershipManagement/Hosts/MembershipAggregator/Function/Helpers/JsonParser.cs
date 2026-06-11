// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace MembershipAggregator.Helpers
{
    internal static class JsonParser
    {
        internal static AzureADGroup GetDestination(string destinationJson)
        {
            var destinations = JsonNode.Parse(destinationJson).AsArray();
            var destinationToken = destinations.First();

            var destination = new AzureADGroup
            {
                Type = destinationToken["type"].ToString(),
                ObjectId = Guid.Parse(destinationToken["value"]["objectId"].GetValue<string>())
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

            foreach (var type in queryTypes)
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
