// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.SyncJobHistory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISyncJobHistoryRepository
    {
        /// <summary>
        /// Creates a new job history entry
        /// </summary>
        /// <param name="jobHistory">The job history entry to create</param>
        Task CreateAsync(SyncJobHistory jobHistory);

        /// <summary>
        /// Gets job history entries for a specific sync job
        /// </summary>
        /// <param name="syncJobId">The sync job identifier</param>
        /// <param name="pageSize">Number of entries per page</param>
        /// <param name="pageNumber">Page number (1-based)</param>
        /// <returns>List of job history entries</returns>
        Task<List<SyncJobHistory>> GetBySyncJobIdAsync(Guid syncJobId, int pageSize = 50, int pageNumber = 1);

        /// <summary>
        /// Gets a specific job history entry by run ID
        /// </summary>
        /// <param name="runId">The run identifier</param>
        /// <returns>Job history entry or null if not found</returns>
        Task<SyncJobHistory?> GetByRunIdAsync(Guid runId);

        /// <summary>
        /// Gets the most recent job history entry for a sync job
        /// </summary>
        /// <param name="syncJobId">The sync job identifier</param>
        /// <returns>Most recent job history entry or null if not found</returns>
        Task<SyncJobHistory?> GetMostRecentAsync(Guid syncJobId);

        /// <summary>
        /// Updates an existing job history entry
        /// </summary>
        /// <param name="jobHistory">The job history entry to update</param>
        Task UpdateAsync(SyncJobHistory jobHistory);

        /// <summary>
        /// Persists the ADF pipeline run identifier onto the run's existing history row without ever
        /// disturbing its status. Performs a field-scoped, primary-context UPDATE that sets only
        /// <see cref="SyncJobHistory.AdfRunId"/> and <see cref="SyncJobHistory.UpdatedAt"/> WHERE the row's
        /// RunId matches. <see cref="SyncJobHistory.AdfRunId"/> is the sole link from a sync run to the ADF
        /// run that produced its data. JobTrigger creates the run's history row at claim time, so the row
        /// already exists; if it does not (0 rows updated) the caller can surface a warning. No row is ever
        /// inserted here, so this can never create a duplicate. No-op (returns 0) when
        /// <paramref name="runId"/> is <see cref="Guid.Empty"/> or <paramref name="adfRunId"/> has no value.
        /// </summary>
        /// <param name="runId">The run identifier whose history row should carry the ADF run identifier.</param>
        /// <param name="adfRunId">The ADF pipeline run identifier to persist.</param>
        /// <returns>The number of history rows updated (0 if none matched or there was nothing to persist).</returns>
        Task<int> SaveAdfRunIdAsync(Guid runId, Guid? adfRunId);

        /// <summary>
        /// Deletes job history entries older than the specified date
        /// </summary>
        /// <param name="cutoffDate">The cutoff date. Entries with UpdatedAt before this date will be deleted.</param>
        /// <returns>Number of records deleted</returns>
        Task<int> DeleteOlderThanAsync(DateTime cutoffDate);
    }
}
