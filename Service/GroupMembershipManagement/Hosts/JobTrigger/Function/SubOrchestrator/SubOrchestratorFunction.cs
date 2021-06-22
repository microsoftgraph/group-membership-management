// Copyright(c) Microsoft Corporation.
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
    public class SubOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ISyncJobTopicService _syncJobTopicService = null;
        public SubOrchestratorFunction(ILoggingRepository loggingRepository, ISyncJobTopicService syncJobService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _syncJobTopicService = syncJobService ?? throw new ArgumentNullException(nameof(syncJobService)); ;
        }

        [FunctionName(nameof(SubOrchestratorFunction))]
        public async Task RunSubOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            var syncJob = context.GetInput<SyncJob>();
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function started", RunId = syncJob.RunId });
            var groupName = await context.CallActivityAsync<SyncJobGroup>(nameof(GroupNameReaderFunction), syncJob);
            await context.CallActivityAsync(nameof(EmailSenderFunction), groupName);
            var canWriteToGroup = await context.CallActivityAsync<bool>(nameof(GroupVerifierFunction), syncJob);
            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { CanWriteToGroup = canWriteToGroup, SyncJob = syncJob });
            if (canWriteToGroup)
            {
                await context.CallActivityAsync(nameof(TopicMessageSenderFunction), syncJob);
            }
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = syncJob.RunId });
        }
    }
}