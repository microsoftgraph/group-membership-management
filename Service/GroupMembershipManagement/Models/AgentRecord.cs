// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

#nullable enable

using System;

namespace Models
{
    public sealed record AgentRecord(Guid AgentObjectId, int ManagerId, string? BlueprintId, bool AccountEnabled);
}
