// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class GroupVerifierFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public GroupVerifierFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [FunctionName(nameof(GroupVerifierFunction))]
        public async Task<bool> VerifyGroupAsync([ActivityTrigger] SyncJob syncJob)
        {
            var canWriteToGroup = false;
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupVerifierFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
                canWriteToGroup = await _jobTriggerService.GroupExistsAndGMMCanWriteToGroupAsync(syncJob);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupVerifierFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            }
            return canWriteToGroup;
        }
    }
}