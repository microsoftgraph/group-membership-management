// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobSchedulingService
    {
        public Task<List<SyncJob>> GetSyncJobsSegmentAsync(bool includeFutureJobs);
        public Task<List<DistributionSyncJob>> ResetJobsAsync(List<DistributionSyncJob> jobs, int daysToAddForReset, bool includeFutureJobs);
        public Task<List<DistributionSyncJob>> DistributeJobsAsync(List<DistributionSyncJob> jobs, int startTimeDelayMinutes, int delayBetweenSyncsSeconds);
        public Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> updatedSyncJobs);
    }
}
