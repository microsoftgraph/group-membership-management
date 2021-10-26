// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Entities;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using Services.Contracts;

namespace Hosts.JobScheduler
{
    public class JobSchedulerFunction
    {
        private readonly IApplicationService _jobSchedulerApplicationService;
        private readonly ILoggingRepository _loggingRepository = null;

        public JobSchedulerFunction(IApplicationService jobSchedulerApplicationService, ILoggingRepository loggingRepository)
        {
            _jobSchedulerApplicationService = jobSchedulerApplicationService ?? throw new ArgumentNullException(nameof(jobSchedulerApplicationService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName("JobScheduler")]
        public async Task Run([TimerTrigger("%jobSchedulerSchedule%")] TimerInfo myTimer)
        {
            _loggingRepository.SyncJobProperties = new Dictionary<string, string> { { "runId", Guid.NewGuid().ToString() } };
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"JobScheduler function started at: {DateTime.UtcNow}" });
            await _jobSchedulerApplicationService.RunAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"JobScheduler function completed at: {DateTime.UtcNow}" });
        }
    }
}
