// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record DeferredPendingEnqueueRequest(string LaneSize, long SequenceNumber, Guid RunId);
}
