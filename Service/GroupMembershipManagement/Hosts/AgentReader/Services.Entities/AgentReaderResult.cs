// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.AgentReader.Services.Entities
{
    public sealed record AgentReaderResult(Guid RunId, string Status, AgentReadCounters Counters);
}
