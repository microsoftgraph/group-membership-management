// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Repositories.Contracts.DestinationResolution
{
    public class ResolvedTeamsChannelDestination : ResolvedDestination
    {
        public Guid TeamObjectId { get; init; }
        public string ChannelId { get; init; }
        public string TeamName { get; init; }
        public string ChannelName { get; init; }
        public override DestinationType DestinationType => DestinationType.TeamsChannel;
    }
}
