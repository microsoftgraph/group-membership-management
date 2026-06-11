// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record ReceiveDeferredPendingRequest(long SequenceNumber, Guid RunId, bool AlreadyDispatched, string OrchestrationInstanceId);
}
