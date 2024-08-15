// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.JobTrigger
{
    public class JobUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public JobUpdaterFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [Function(nameof(JobUpdaterFunction))]
        public async Task UpdateJobAsync([ActivityTrigger] JobUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
                _jobTriggerService.RunId = request.SyncJob.RunId ?? Guid.Empty;
                await _jobTriggerService.UpdateSyncJobAsync(request.Status, request.SyncJob);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            }
        }
    }
}