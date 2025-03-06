// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly PlaceMembershipObtainerService _membershipProviderService;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, PlaceMembershipObtainerService membershipProviderService)
        {
            _loggingRepository = loggingRepository;
            _membershipProviderService = membershipProviderService;
        }

        [FunctionName(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
                await _membershipProviderService.UpdateSyncJobStatusAsync(request.SyncJob, request.Status);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}