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

        /// <summary>
        /// Tracks when this item was last denied capacity during drain.
        /// Used to suppress repeated "no capacity" log messages within a short time window.
        /// </summary>
        public DateTimeOffset? LastCapacityDeniedAtUtc { get; set; }
    }
}
