// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAgentTableRepository
    {
        Task ValidateTableAsync(Guid runId, CancellationToken cancellationToken);
        Task<int> WriteAsync(Guid runId, IReadOnlyList<AgentRecord> agents, CancellationToken cancellationToken);
        Task<long> GetRowCountAsync(Guid runId, CancellationToken cancellationToken);
    }
}
