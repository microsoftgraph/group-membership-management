// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabasePendingSyncJobChangesRepository
    {
        Task<Guid> InsertPendingSyncJobChangeAsync(PendingSyncJobChange pendingSyncJobChange);
        Task<IEnumerable<PendingSyncJobChange>> GetAllPendingSyncJobChangesAsync();
        Task<IEnumerable<PendingSyncJobChange>> GetActivePendingSyncJobChangesAsync();
        Task<PendingSyncJobChange> GetPendingSyncJobChangeByObjectIdAsync(Guid objectId);
    }
}
