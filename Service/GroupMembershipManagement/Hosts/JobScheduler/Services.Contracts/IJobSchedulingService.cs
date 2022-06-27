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
        public Task<List<SchedulerSyncJob>> GetJobsToUpdate();
        public Task ResetJobs(List<SchedulerSyncJob> jobs);
        public Task DistributeJobs(List<SchedulerSyncJob> jobs);
    }
}
