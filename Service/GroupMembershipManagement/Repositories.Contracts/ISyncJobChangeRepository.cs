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
    }
}
