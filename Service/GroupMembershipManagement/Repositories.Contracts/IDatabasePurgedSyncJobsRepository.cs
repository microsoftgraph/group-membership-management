// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabasePurgedSyncJobsRepository
    {       
        Task<int> InsertPurgedSyncJobsAsync(IEnumerable<PurgedSyncJob> jobs);
        Task<IEnumerable<PurgedSyncJob>> GetPurgedSyncJobsAsync(DateTime cutOffDate);
        Task<int> DeletePurgedSyncJobsAsync(IEnumerable<PurgedSyncJob> jobs);
    }
}
