// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Hosts.MessageSplitter
{
    public class RunLimiterState
    {
        public Dictionary<string, DateTimeOffset> Leases { get; set; } = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
    }
}
