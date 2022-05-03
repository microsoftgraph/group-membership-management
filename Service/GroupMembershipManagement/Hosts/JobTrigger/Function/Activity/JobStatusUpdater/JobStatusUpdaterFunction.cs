// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId });
                await _jobTriggerService.UpdateSyncJobStatusAsync(request.Status, request.SyncJob);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId });

            }
        }
    }
}