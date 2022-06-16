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
        private readonly IApplicationService _applicationService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public ResetJobsFunction(IApplicationService applicationService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        }

        [FunctionName(nameof(ResetJobsFunction))]
        public async Task ResetJobs([ActivityTrigger] ResetJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ResetJobsFunction)} function started at: {DateTime.UtcNow}" });
            await _applicationService.ResetJobs(request.JobsToReset);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ResetJobsFunction)} function completed at: {DateTime.UtcNow}" });
        }
    }
}
