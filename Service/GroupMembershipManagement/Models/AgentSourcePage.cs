// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

#nullable enable

using System.Collections.Generic;

namespace Models
{
    public sealed record AgentSourceRecord(
        string? Id,
        bool HasManager,
        string? ManagerIdentifier,
        string? BlueprintId,
        bool? AccountEnabled);

    public sealed record AgentSourcePage(IReadOnlyList<AgentSourceRecord> Agents, string? NextLink);
}
