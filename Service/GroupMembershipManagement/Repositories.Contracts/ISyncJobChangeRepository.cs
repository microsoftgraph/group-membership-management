// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Models;
using Models.SyncJobChange;

namespace Repositories.Contracts
{
    public interface ISyncJobChangeRepository
    {
        /// <summary>
        /// Retrieves a page of sync job changes for a given sync job id with paging metadata.
        /// </summary>
        Task<RepositoryPage<SyncJobChange>> GetPageOfSyncJobChangesBySyncJobId(
            Guid syncJobId,
            int startPage = 1,
            int pageSize = 10,
            SyncJobChangeSortingField sortBy = SyncJobChangeSortingField.ChangeTime,
            bool sortAscending = false);

        /// <summary>
        /// Saves a new sync job change to the database.
        /// </summary>
        Task SaveSyncJobChange(SyncJobChange syncJobChange);
    }
}
