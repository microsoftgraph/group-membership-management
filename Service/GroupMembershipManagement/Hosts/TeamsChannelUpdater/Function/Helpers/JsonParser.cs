// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Models.Entities;

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

        internal static List<string> GetQueryTypes(string query)
        {
            var queries = JArray.Parse(query);
            var queryTypes = queries.SelectTokens("$..type")
                                    .Select(x => x.Value<string>())
                                    .ToList();

            return queryTypes;
        }
    }
}
