// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseSyncJobRepository
    {        
        Task<List<SyncJob>> SelectSyncJobsAsync();
    }
}
