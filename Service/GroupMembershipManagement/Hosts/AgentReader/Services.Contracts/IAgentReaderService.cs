// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AgentReader.Services.Entities;
using Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services.Contracts
{
    public interface IAgentReaderService
    {
        Task ValidateTableAsync(Guid runId, CancellationToken cancellationToken);
        Task<AgentPage> ReadPageAsync(Guid runId, string? nextLink, CancellationToken cancellationToken);
        Task<int> WriteAsync(Guid runId, IReadOnlyList<AgentRecord> agents, CancellationToken cancellationToken);
        Task<long> GetRowCountAsync(Guid runId, CancellationToken cancellationToken);
    }
}
