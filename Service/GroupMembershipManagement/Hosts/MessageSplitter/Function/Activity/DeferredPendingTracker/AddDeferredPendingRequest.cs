// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record AddDeferredPendingRequest(long SequenceNumber, Guid RunId, DateTimeOffset UtcNow);
}
