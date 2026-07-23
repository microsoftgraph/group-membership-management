// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.ServiceBus;
using Models.SyncJobHistory;
using System;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISyncJobStatusService
    {
        Task UpdateJobStatusAsync(SyncJob job, SyncStatus? status, SyncJobHistory? history = null,  string? functionName = null);
        Task CreateOrUpdateJobHistoryAsync(SyncJobHistory history);

        /// <summary>
        /// Persists the ADF pipeline run identifier onto the run's existing history row without ever
        /// changing the run's status.
        /// </summary>
        /// <param name="runId">The run identifier whose history row should carry the ADF run identifier.</param>
        /// <param name="adfRunId">The ADF pipeline run identifier to persist.</param>
        /// <returns>The number of history rows updated.</returns>
        Task<int> SaveAdfRunIdAsync(Guid runId, Guid? adfRunId);
    }
}
