// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using Microsoft.Azure.Cosmos.Table;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobSchedulingService
    {
        public Task<TableSegmentBulkResult> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken);
        public Task ResetJobsAsync(List<SchedulerSyncJob> jobs);
        public Task DistributeJobsAsync(List<SchedulerSyncJob> jobs);
    }
}
