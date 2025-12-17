// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record AcquireLeaseRequest(Guid RunId, int MaxInFlight, int LeaseTimeoutMinutes, DateTimeOffset UtcNow);
}
