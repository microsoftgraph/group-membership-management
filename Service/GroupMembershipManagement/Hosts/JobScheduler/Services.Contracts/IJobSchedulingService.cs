// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobSchedulingService
    {
        public Task<List<SchedulerSyncJob>> GetAllSyncJobsAsync(bool includeFutureStartDates);
        public Task<List<SchedulerSyncJob>> GetJobsToUpdateAsync();
        public Task ResetJobsAsync(List<SchedulerSyncJob> jobs);
        public Task DistributeJobsAsync(List<SchedulerSyncJob> jobs);
    }
}
