// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.AgentReader.Services.Entities
{
    public sealed record AgentPage(
        IReadOnlyList<AgentRecord> Agents,
        string? NextLink,
        AgentReadCounters Counters);
}
