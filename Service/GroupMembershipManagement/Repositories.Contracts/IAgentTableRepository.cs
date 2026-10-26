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
        Task<AgentTableInfo> ValidateTableAsync(Guid runId, CancellationToken cancellationToken);
        Task<int> WriteAsync(Guid runId, IReadOnlyList<AgentRecord> agents, CancellationToken cancellationToken);
        Task<long> GetRowCountAsync(Guid runId, CancellationToken cancellationToken);

        /// <summary>The distinct non-empty Blueprint_Code values written for the run.</summary>
        Task<IReadOnlyList<string>> GetBlueprintCodesAsync(Guid runId, CancellationToken cancellationToken);

        /// <summary>
        /// Replaces the run's rows for <paramref name="columnName"/> in the mappings table (Code, Description, ColumnName).
        /// Rows for other columns are left untouched. The mappings table must already exist.
        /// </summary>
        Task WriteMappingsAsync(
            Guid runId, string columnName, IReadOnlyDictionary<string, string> descriptionsByCode, CancellationToken cancellationToken);
    }
}
