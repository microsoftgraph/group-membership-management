// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record AcquireLeaseResponse(bool Acquired, int InFlightCount, DateTimeOffset? LeaseExpiresAt);
}
