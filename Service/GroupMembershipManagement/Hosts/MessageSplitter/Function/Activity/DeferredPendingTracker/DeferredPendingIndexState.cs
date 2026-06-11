// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Hosts.MessageSplitter
{
    public class DeferredPendingIndexState
    {
        public DateTimeOffset? DrainLockUntilUtc { get; set; }

        public List<DeferredPendingItem> Items { get; set; } = new();

        public int Count => Items?.Count ?? 0;
    }
}
