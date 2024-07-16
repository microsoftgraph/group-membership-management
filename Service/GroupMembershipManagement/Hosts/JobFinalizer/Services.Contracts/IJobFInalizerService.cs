// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobFinalizerService
    {
        Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status);
    }
}
