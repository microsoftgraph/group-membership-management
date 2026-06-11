// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.MessageSplitter
{
    public record MarkDeferredPendingDispatchedRequest(long SequenceNumber, string OrchestrationInstanceId);
}
