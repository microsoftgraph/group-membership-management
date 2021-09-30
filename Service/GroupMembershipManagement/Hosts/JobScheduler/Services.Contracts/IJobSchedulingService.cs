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
        public Task UpdateSyncJobsAsync(List<SchedulerSyncJob> updatedSyncJobs);
        public Task<List<SchedulerSyncJob>> DistributeJobStartTimesAsync(List<SchedulerSyncJob> schedulerSyncJobs);
        public List<SchedulerSyncJob> ResetJobStartTimes(List<SchedulerSyncJob> schedulerSyncJobs, DateTime newStartTime, bool onlyUpdateOlderStartDates);
    }
}
