// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.GroupMembershipObtainer
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

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
                await _membershipCalculator.UpdateSyncJobStatusAsync(request.SyncJob, request.Status);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}