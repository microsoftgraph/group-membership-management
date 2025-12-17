// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record RenewLeaseRequest(Guid RunId, int LeaseTimeoutMinutes, DateTimeOffset UtcNow);
}
