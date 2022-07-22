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
    public class GetJobsQueryFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public GetJobsQueryFunction(IJobSchedulingService jobSchedulingService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [FunctionName(nameof(GetJobsQueryFunction))]
        public async Task<GetJobsQueryResponse> GetJobsToUpdateAsync([ActivityTrigger] object request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsQueryFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsQueryFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return new GetJobsQueryResponse
            {
                JobsQuery = null
            };
        }
    }
}
