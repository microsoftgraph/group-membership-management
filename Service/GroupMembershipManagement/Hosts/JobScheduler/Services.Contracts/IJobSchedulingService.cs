// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobSchedulingService
    {
        public Task<List<SyncJob>> GetSyncJobsAsync();
        public Task<List<DistributionSyncJob>> ResetJobsAsync(List<DistributionSyncJob> jobs, int daysToAddForReset);
        public Task<List<DistributionSyncJob>> DistributeJobsAsync(List<DistributionSyncJob> jobs, int startTimeDelayMinutes, int delayBetweenSyncsSeconds);
        public Task BatchUpdateSyncJobsAsync(IEnumerable<UpdateMergeSyncJob> updatedSyncJobs);
    }
}
