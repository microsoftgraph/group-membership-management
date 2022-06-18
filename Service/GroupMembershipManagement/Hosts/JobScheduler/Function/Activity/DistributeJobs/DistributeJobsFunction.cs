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
    public class DistributeJobsFunction
    {
        private readonly IApplicationService _applicationService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public DistributeJobsFunction(IApplicationService applicationService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
        }

        [FunctionName(nameof(DistributeJobsFunction))]
        public async Task DistributeJobs([ActivityTrigger] DistributeJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DistributeJobsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            await _applicationService.DistributeJobs(request.JobsToDistribute);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DistributeJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
        }
    }
}
