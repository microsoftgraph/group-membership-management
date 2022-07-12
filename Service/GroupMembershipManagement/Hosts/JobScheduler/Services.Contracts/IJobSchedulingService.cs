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
        public Task<List<SchedulerSyncJob>> ResetJobsAsync(List<SchedulerSyncJob> jobs);
        public Task<List<SchedulerSyncJob>> DistributeJobsAsync(List<SchedulerSyncJob> jobs);
        public Task BatchUpdateSyncJobsAsync(IEnumerable<SyncJob> updatedSyncJobs);
    }
}
