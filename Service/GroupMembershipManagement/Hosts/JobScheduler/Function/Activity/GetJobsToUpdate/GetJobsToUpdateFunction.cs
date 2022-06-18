// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using Services.Entities;

namespace Hosts.JobScheduler
{
    public class GetJobsToUpdateFunction
    {
        private readonly IApplicationService _applicationService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public GetJobsToUpdateFunction(IApplicationService applicationService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        }

        [FunctionName(nameof(GetJobsToUpdateFunction))]
        public async Task<List<SchedulerSyncJob>> GetJobsToUpdate([ActivityTrigger] object request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsToUpdateFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var jobsToUpdate = await _applicationService.GetJobsToUpdate();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsToUpdateFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return jobsToUpdate;
        }
    }
}
