// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record TakeNextBatchRequest(DateTimeOffset UtcNow, int MaxItems, int InProgressSeconds);
}
