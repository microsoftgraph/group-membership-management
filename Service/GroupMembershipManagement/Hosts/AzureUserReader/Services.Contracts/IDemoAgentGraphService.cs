// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IDemoAgentGraphService
    {
        Task<List<ExistingDemoAgent>> GetDemoAgentsAsync(CancellationToken cancellationToken = default);
        Task<string> EnsureBlueprintAsync(CancellationToken cancellationToken = default, string sponsorId = null);
        Task<string> CreateAgentAsync(DemoAgentSpec spec, string blueprintAppId, CancellationToken cancellationToken = default);
        Task SetManagerAsync(string agentId, string managerId, CancellationToken cancellationToken = default);
    }
}
