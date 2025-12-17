// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record RenewLeaseResponse(bool Renewed, DateTimeOffset? LeaseExpiresAt);
}
