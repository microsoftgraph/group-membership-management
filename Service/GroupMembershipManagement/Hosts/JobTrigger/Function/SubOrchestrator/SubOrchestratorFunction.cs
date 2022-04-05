// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class SubOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        public SubOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(SubOrchestratorFunction))]
        public async Task RunSubOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var syncJob = context.GetInput<SyncJob>();
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function started", RunId = syncJob.RunId });
            var groupInformation = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), syncJob);

            if (string.IsNullOrEmpty(groupInformation.Name))
            {
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), 
                                                new JobStatusUpdaterRequest { Status = SyncStatus.DestinationGroupNotFound, SyncJob = syncJob });
                return;
            }

            await context.CallActivityAsync(nameof(EmailSenderFunction), groupInformation);
            var canWriteToGroup = await context.CallActivityAsync<bool>(nameof(GroupVerifierFunction), syncJob);
            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), 
                                            new JobStatusUpdaterRequest { Status = canWriteToGroup ? SyncStatus.InProgress : SyncStatus.NotOwnerOfDestinationGroup, SyncJob = syncJob });
            
            if (canWriteToGroup)
            {
                await context.CallActivityAsync(nameof(TopicMessageSenderFunction), syncJob);
            }

            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = syncJob.RunId });
        }
    }
}