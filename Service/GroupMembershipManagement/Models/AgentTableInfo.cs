// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

#nullable enable

namespace Models
{
    // What validation found about a run's agents table. AgentManagerIdAllowsNull is false for tables
    // created before the column allowed NULL; agents without a valid manager can't be written to those.
    public sealed record AgentTableInfo(bool AgentManagerIdAllowsNull);
}
