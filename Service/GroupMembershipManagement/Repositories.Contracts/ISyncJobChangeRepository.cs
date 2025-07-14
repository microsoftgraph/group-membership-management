// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.SyncJobChange;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISyncJobChangeRepository
    {
        /// <summary>
        /// Retrieves a page of sync job changes for a given sync job id with paging metadata.
        /// </summary>
        Task<RepositoryPage<SyncJobChange>> GetPageBySyncJobId(
            Guid syncJobId,
            int startPage = 1,
            int pageSize = 10,
            SyncJobChangeSortingField sortBy = SyncJobChangeSortingField.ChangeTime,
            bool sortAscending = false);

        /// <summary>
        /// Adds a new sync job change to the database.
        /// </summary>
        /// <remarks>
        /// SyncJobChanges should be immutable once created, so this will not update existing records.
        /// </remarks>
        Task Save(SyncJobChange syncJobChange);
        Task BulkSaveAsync(IEnumerable<SyncJobChange> syncJobChanges);
        Task UpdateSyncJobChangeAsync(SyncJobChange syncJobChange);

        /// <summary>
        /// Retrieves the last sync job change by its sync job id.
        /// </summary>
        Task<SyncJobChange> GetLastSyncJobChangeBySyncJobIdAsync(Guid syncJobId);
        Task<SyncJobChange> GetLastSyncJobChangeWithOnboardingOrUpdateBySyncJobIdAsync(Guid syncJobId);
        Task<SyncJobChange> GetRecentGroupSettingsBySyncJobIdAsync(Guid syncJobId);
    }
}
