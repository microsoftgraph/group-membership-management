// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using System.Linq;
using Entities;

namespace Hosts.JobScheduler
{
    public class GetJobsFunction
    {
        private readonly IJobSchedulingService _jobSchedulingService = null;
        private readonly ILoggingRepository _loggingRepository = null;
        public GetJobsFunction(IJobSchedulingService jobSchedulingService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobSchedulingService = jobSchedulingService ?? throw new ArgumentNullException(nameof(jobSchedulingService));
        }

        [FunctionName(nameof(GetJobsFunction))]
        public async Task<GetJobsResponse> GetJobsToUpdateAsync([ActivityTrigger] GetJobsRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var tableQuerySegment = await _jobSchedulingService.GetSyncJobsAsync(request.IncludeFutureJobs);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetJobsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return new GetJobsResponse
            {
                JobsSegment = tableQuerySegment.Select(x => new DistributionSyncJob(x)).ToList()
            };
        }
    }
}
