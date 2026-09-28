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

        // US3: identity of a shared channel used as a *source* part. These are populated
        // for non-destination parts (IsDestinationPart == false) from the query part payload
        // (source.groupId / source.channelId). They are null for destination parts, whose
        // identity is read from the TeamsChannels table via GetDestinationAsync.
        public Guid? SourceGroupId { get; init; }
        public string SourceChannelId { get; init; }
    }
}

