// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.ServiceBus
{
    public class MessageSplitterLeaseRenewSignal
    {
        public MessageSplitterLeaseRenewSignal(Guid runId, string laneSize, int leaseTimeoutMinutes)
        {
            RunId = runId;
            LaneSize = laneSize;
            LeaseTimeoutMinutes = leaseTimeoutMinutes;
        }

        public Guid RunId { get; set; }
        public string LaneSize { get; set; }
        public int LeaseTimeoutMinutes { get; set; }
    }
}
