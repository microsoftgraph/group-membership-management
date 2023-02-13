// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class SyncJobsReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public SyncJobsReaderFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [FunctionName(nameof(SyncJobsReaderFunction))]
        public async Task<List<SyncJob>> GetSyncJobsAsync([ActivityTrigger] SyncStatus status)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SyncJobsReaderFunction)} function started" }, VerbosityLevel.DEBUG);
            var jobs = await _jobTriggerService.GetSyncJobsAsync(status);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SyncJobsReaderFunction)} function completed" }, VerbosityLevel.DEBUG);
            return jobs;
        }
    }
}