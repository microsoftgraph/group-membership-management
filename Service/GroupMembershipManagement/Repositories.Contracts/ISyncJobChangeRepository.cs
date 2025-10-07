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
        
        /// <summary>
        /// Retrieves the most recent sync job change record (ANY reason) by its sync job id.
        /// This differs from <see cref="GetLastSyncJobChangeBySyncJobIdAsync"/> which limits reasons
        /// to onboarding / update / submission rejected today. Use this when you need the true
        /// last change regardless of reason (e.g. to show an accurate "Last Modified" timestamp
        /// that includes SubmissionApproved, StatusUpdate, IgnoreThresholdOnce, GroupSettings, etc.).
        /// </summary>
        Task<SyncJobChange> GetLastSyncJobRecordBySyncJobIdAsync(Guid syncJobId);
        Task<SyncJobChange> GetLastSyncJobChangeWithOnboardingOrUpdateBySyncJobIdAsync(Guid syncJobId);
        Task<SyncJobChange> GetRecentGroupSettingsBySyncJobIdAsync(Guid syncJobId);
    }
}
