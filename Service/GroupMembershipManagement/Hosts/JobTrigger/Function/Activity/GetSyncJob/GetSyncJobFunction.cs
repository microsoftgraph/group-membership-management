// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class GetSyncJobFunction
    {
        private readonly IJobTriggerService _jobTriggerService;
        private readonly ILoggingRepository _loggingRepository;

        public GetSyncJobFunction(IJobTriggerService jobTriggerService, ILoggingRepository loggingRepository)
        {
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(GetSyncJobFunction))]
        public async Task<SyncJob> GetSyncJobByIdAsync([ActivityTrigger] Guid syncJobId)
        {
            await _loggingRepository.LogMessageAsync(
                new LogMessage 
                { 
                    Message = $"{nameof(GetSyncJobFunction)} retrieving job {syncJobId}" 
                }, 
                VerbosityLevel.DEBUG);
            
            var syncJob = await _jobTriggerService.GetSyncJobByIdAsync(syncJobId);
            
            if (syncJob != null && syncJob.RunId.HasValue)
            {
                _jobTriggerService.RunId = syncJob.RunId.Value;
            }
            
            await _loggingRepository.LogMessageAsync(
                new LogMessage 
                { 
                    Message = $"{nameof(GetSyncJobFunction)} completed for job {syncJobId}, found: {syncJob != null}",
                    RunId = syncJob?.RunId
                }, 
                VerbosityLevel.DEBUG);
            
            return syncJob;
        }
    }
}
