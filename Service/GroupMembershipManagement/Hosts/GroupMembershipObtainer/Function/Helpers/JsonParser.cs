// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace GroupMembershipObtainer.Helpers
{
    internal static class JsonParser
    {
        internal static AzureADGroup GetDestination(string destinationJson)
        {
            var destinations = JArray.Parse(destinationJson);
            var destinationToken = destinations.First();

            if (destinationToken["type"].ToString() == "GroupMembership")
            {
                return new AzureADGroup
                {
                    Type = destinationToken["type"].ToString(),
                    ObjectId = Guid.Parse(destinationToken["value"]["objectId"].Value<string>())
                };
            }
            else if (destinationToken["type"].ToString() == "TeamsChannelMembership")
            {
                return new AzureADTeamsChannel
                {
                    Type = destinationToken["type"].ToString(),
                    ObjectId = Guid.Parse(destinationToken["value"]["objectId"].Value<string>()),
                    ChannelId = destinationToken["value"]["channelId"].Value<string>()
                };
            }

            return null;
        }
    }
}
