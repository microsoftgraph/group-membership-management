// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.MessageSplitter
{
    public record ReceiveDeferredPendingResponse(bool Dispatched, bool ShouldRemoveFromIndex, string OrchestrationInstanceId, bool MessageNotFound);
}
