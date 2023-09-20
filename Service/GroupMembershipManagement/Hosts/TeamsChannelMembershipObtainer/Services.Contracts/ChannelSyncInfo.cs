// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace TeamsChannelMembershipObtainer.Service.Contracts
{
    // this should be easy to turn into one of those durable function Request objects later
    public class ChannelSyncInfo
    {
        public SyncJob SyncJob { get; init; }
        public int TotalParts { get; init; }
        public int CurrentPart { get; init; }
        public bool Exclusionary { get; init; }
        public bool IsDestinationPart { get; init; }
    }
}

