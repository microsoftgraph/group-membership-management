// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAgentGraphRepository
    {
        Task<AgentSourcePage> ReadPageAsync(Guid runId, string? nextLink, CancellationToken cancellationToken);

        /// <summary>
        /// Looks up the display names of agent identity blueprints by their application (client) ID.
        /// </summary>
        /// <returns>The names found. A blueprint that is not visible in this tenant has no entry.</returns>
        Task<IReadOnlyDictionary<Guid, string>> GetBlueprintNamesAsync(
            Guid runId, IReadOnlyCollection<Guid> blueprintIds, CancellationToken cancellationToken);
    }
}
