// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.MessageSplitter
{
    /// <summary>
    /// Result of attempting to receive and dispatch a single deferred-pending message.
    /// </summary>
    /// <param name="PeerOrchestrationExists">
    /// When the message was not found, indicates whether the deterministic GraphUpdater orchestration
    /// (deferredpending_{RunId}_{SequenceNumber}) already exists — i.e., a peer drain dispatched this exact
    /// item. Because that instance is created before the peer completes the Service Bus message (which
    /// happens-before this "not found" observation), this signal is race-free and authoritatively rules out
    /// a false orphan. Defaults to false; only meaningful when <paramref name="MessageNotFound"/> is true.
    /// </param>
    public record ReceiveDeferredPendingResponse(bool Dispatched, bool ShouldRemoveFromIndex, string OrchestrationInstanceId, bool MessageNotFound, bool PeerOrchestrationExists = false);
}
