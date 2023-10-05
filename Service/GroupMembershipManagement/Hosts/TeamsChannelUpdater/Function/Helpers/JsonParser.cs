// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Models.Entities;
using System.Text;

namespace TeamsChannelUpdater.Helpers
{
    internal static class JsonParser
    {
        internal static AzureADTeamsChannel GetDestination(string destinationJson)
        {
            var destinations = JArray.Parse(destinationJson);
            var destinationToken = destinations.First();
            var destination = new AzureADTeamsChannel
            {
                Type = destinationToken["type"].ToString(),
                ObjectId = Guid.Parse(destinationToken["value"]["objectId"].ToString()),
                ChannelId = destinationToken["value"]["channelId"].ToString()
            };

            return destination;
        }

        internal static string GetQueryTypes(string query)
        {
            var queries = JArray.Parse(query);
            var queryTypeCounts = new Dictionary<string, int>();

            foreach (var token in queries.SelectTokens("$..type"))
            {
                var type = token.Value<string>();

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
