// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ISyncJobRepository
    {
        IAsyncEnumerable<SyncJob> GetSpecificSyncJobsAsync();
        Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> jobs);
        Task DeleteSyncJobsAsync(IEnumerable<SyncJob> jobs);
    }
}
