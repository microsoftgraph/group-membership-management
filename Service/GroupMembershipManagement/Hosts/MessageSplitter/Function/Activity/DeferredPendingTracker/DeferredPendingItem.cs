// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record DeferredPendingItem(long SequenceNumber, Guid RunId, DateTimeOffset EnqueuedAtUtc)
    {
        public DateTimeOffset? InProgressUntilUtc { get; set; }

        public bool Dispatched { get; set; }

        public string OrchestrationInstanceId { get; set; }
    }
}
