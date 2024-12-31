// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.Entities;
using Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            var queries = JArray.Parse(query);
            var queryTypeCounts = new Dictionary<string, int>();

            foreach ( var token in queries.SelectTokens("$..type"))
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
