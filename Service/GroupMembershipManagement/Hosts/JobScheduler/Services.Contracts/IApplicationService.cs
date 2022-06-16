// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IApplicationService
    {
        public Task RunAsync();
        public Task<List<SchedulerSyncJob>> GetJobsToUpdate();
        public Task ResetJobs(List<SchedulerSyncJob> jobs);
        public Task DistributeJobs(List<SchedulerSyncJob> jobs);
    }
}
