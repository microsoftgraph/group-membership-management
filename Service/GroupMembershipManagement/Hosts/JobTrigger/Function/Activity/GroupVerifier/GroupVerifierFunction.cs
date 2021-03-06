// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
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
        public async Task<bool> VerifyGroup([ActivityTrigger] SyncJob syncJob, ILogger log)
        {
            var canWriteToGroup = false;
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupVerifierFunction)} function started", RunId = syncJob.RunId });
                canWriteToGroup = await _jobTriggerService.CanWriteToGroup(syncJob);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupVerifierFunction)} function completed", RunId = syncJob.RunId });
            }
            return canWriteToGroup;
        }
    }
}