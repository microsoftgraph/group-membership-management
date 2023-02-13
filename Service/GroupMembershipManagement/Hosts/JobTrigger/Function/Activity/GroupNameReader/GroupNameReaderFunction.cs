// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class GroupNameReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public GroupNameReaderFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [FunctionName(nameof(GroupNameReaderFunction))]
        public async Task<SyncJobGroup> GetGroupNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            var group = new SyncJobGroup();
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupNameReaderFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
                _jobTriggerService.RunId = syncJob.RunId ?? Guid.Empty;
                var groupName = await _jobTriggerService.GetGroupNameAsync(syncJob.TargetOfficeGroupId);
                group.SyncJob = syncJob;
                group.Name = groupName;
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupNameReaderFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            }
            return group;
        }
    }
}