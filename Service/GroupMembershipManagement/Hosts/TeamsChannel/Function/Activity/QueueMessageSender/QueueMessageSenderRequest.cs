// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using TeamsChannel.Service.Contracts;

namespace Hosts.TeamsChannelMembershipObtainer
{
    public class QueueMessageSenderRequest
    {
        public ChannelSyncInfo ChannelSyncInfo { get; set; }
        public string FilePath { get; set; }

    }
}
