// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

#nullable enable

using System;

namespace Models
{
    // AgentManagerId is null when the agent has no valid manager; such agents are still written.
    public sealed record AgentRecord(Guid AgentObjectId, int? AgentManagerId, string? BlueprintId, bool AgentAccountEnabled);
}
