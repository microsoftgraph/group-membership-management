// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record PruneOlderThanMinutesRequest(DateTimeOffset UtcNow, int MaxAgeMinutes);
}
