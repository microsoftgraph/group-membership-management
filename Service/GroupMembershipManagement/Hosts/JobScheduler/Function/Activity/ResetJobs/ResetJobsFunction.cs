// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;

namespace Hosts.JobScheduler
{
    public class ResetJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public ResetJobsFunction(IJobSchedulingService jobSchedulingService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [FunctionName(nameof(ResetJobsFunction))]
        public async Task ResetJobsAsync([ActivityTrigger] ResetJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ResetJobsFunction)} function started at: {DateTime.UtcNow}" });
            await _jobSchedulingService.ResetJobsAsync(request.JobsToReset);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ResetJobsFunction)} function completed at: {DateTime.UtcNow}" });
        }
    }
}
