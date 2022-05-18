// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly SGMembershipCalculator _membershipCalculator;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, SGMembershipCalculator membershipCalculator)
        {
            _loggingRepository = loggingRepository;
            _membershipCalculator = membershipCalculator;
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId });
                await _membershipCalculator.UpdateSyncJobStatusAsync(request.SyncJob, request.Status);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId });
            }
        }
    }
}