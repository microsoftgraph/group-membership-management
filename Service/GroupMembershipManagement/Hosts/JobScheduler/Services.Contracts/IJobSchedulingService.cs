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
        public Task<TableSegmentBulkResult<DistributionSyncJob>> GetSyncJobsSegmentAsync(AsyncPageable<SyncJob> pageableQueryResult, string continuationToken);
        public Task<List<DistributionSyncJob>> ResetJobsAsync(List<DistributionSyncJob> jobs);
        public Task<List<DistributionSyncJob>> DistributeJobsAsync(List<DistributionSyncJob> jobs);
        public Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> updatedSyncJobs);
    }
}
