// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record TakeNextRequest(DateTimeOffset UtcNow, int InProgressSeconds);
}
