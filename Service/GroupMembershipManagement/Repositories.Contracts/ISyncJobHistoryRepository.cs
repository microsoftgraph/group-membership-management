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
    }
}
