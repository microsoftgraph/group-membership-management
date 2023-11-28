// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class GroupNameReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphAPIService _graphAPIService  = null;
        public GroupNameReaderFunction(ILoggingRepository loggingRepository, IGraphAPIService graphAPIService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService)); ;
        }

        [FunctionName(nameof(GroupNameReaderFunction))]
        public async Task<SyncJobGroup> GetGroupNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            var group = new SyncJobGroup();
            
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupNameReaderFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
                _graphAPIService.RunId = syncJob.RunId ?? Guid.Empty;
                var groupName = await _graphAPIService.GetGroupNameAsync(syncJob.TargetOfficeGroupId);
                group.SyncJob = syncJob;
                group.Name = groupName;
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupNameReaderFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            }
            return group;
        }
    }
}