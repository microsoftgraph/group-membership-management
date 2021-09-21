// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IJobSchedulingService
    {
        public Task<List<SchedulerSyncJob>> GetAllSyncJobs(bool includeFutureStartDates);
        public Task<List<SchedulerSyncJob>> DistributeJobStartTimes(List<SchedulerSyncJob> schedulerSyncJobs);
        public Task<List<SchedulerSyncJob>> ResetJobStartTimes(List<SchedulerSyncJob> schedulerSyncJobs, DateTime newStartTime, bool onlyUpdateOlderStartDates);
    }
}
