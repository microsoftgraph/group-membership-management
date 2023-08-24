// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.Entities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphUpdater.Helpers
{
    internal static class JsonParser
    {
        internal static Destination GetDestination(string destinationJson)
        {
            var destinations = JArray.Parse(destinationJson);
            var destinationToken = destinations.First();
            var destination = new Destination
            {
                Type = destinationToken["type"].ToString(),
                ObjectId = Guid.Parse(destinationToken["value"]["objectId"].Value<string>())
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
