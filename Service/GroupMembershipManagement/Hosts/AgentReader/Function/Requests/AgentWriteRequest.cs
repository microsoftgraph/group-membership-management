// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Hosts.AgentReader.Services.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.AgentReader
{
    public sealed record AgentWriteRequest(Guid RunId, IReadOnlyList<AgentRecord> Agents);
}
