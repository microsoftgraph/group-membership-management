// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAgentGraphRepository
    {
        Task<AgentSourcePage> ReadPageAsync(Guid runId, string? nextLink, CancellationToken cancellationToken);
    }
}
