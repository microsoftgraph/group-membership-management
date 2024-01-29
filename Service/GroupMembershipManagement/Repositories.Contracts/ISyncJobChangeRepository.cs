// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;

namespace Repositories.Contracts
{
    public interface ISyncJobChangeRepository
    {
        /// <summary>
        /// Retrieves a list of sync job changes for a given sync job id in descending order by change time.
        /// </summary>
        Task<IEnumerable<SyncJobChange>> GetSyncJobChangesBySyncJobId(Guid syncJobId);
        /// <summary>
        /// Saves a new sync job change to the database.
        /// </summary>
        Task SaveSyncJobChange(SyncJobChange syncJobChange);
    }
}
