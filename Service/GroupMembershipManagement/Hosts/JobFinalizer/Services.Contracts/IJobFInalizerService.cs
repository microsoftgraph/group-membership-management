// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISyncJobUpdaterService
    {
        Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status);
    }
}
