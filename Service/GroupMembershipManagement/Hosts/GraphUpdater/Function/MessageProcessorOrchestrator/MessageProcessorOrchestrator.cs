// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageProcessorOrchestrator
    {
        [Singleton(Mode = SingletonMode.Function)]
        [FunctionName(nameof(MessageProcessorOrchestrator))]
        public async Task RunMessageProcessorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            try
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(MessageProcessorOrchestrator)} started" });

                var messageTrackerId = new EntityId(MessageTrackerFunction.EntityName, MessageTrackerFunction.EntityKey);
                var messageTrackerProxy = context.CreateEntityProxy<IMessageTracker>(messageTrackerId);
                string messageId = null;
                int messageCount = 0;

                using (await context.LockAsync(messageTrackerId))
                {
                    messageId = await messageTrackerProxy.GetNextMessageIdAsync();
                    messageCount = await messageTrackerProxy.GetMessageCountAsync();
                }

                if (string.IsNullOrWhiteSpace(messageId))
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(MessageProcessorOrchestrator)} has no messages to process" });
                    return;
                }

                var messageEntityId = new EntityId(nameof(MessageEntity), messageId);
                var messageEntityProxy = context.CreateEntityProxy<IMessageEntity>(messageEntityId);

                MembershipHttpRequest request = null;
                using (await context.LockAsync(messageEntityId))
                {
                    request = await messageEntityProxy.GetAsync();
                    await messageEntityProxy.DeleteAsync();
                }

                if (request != null)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Calling {nameof(OrchestratorFunction)} for jobId {request.SyncJob.JobId}", SyncJob = request.SyncJob });
                    await context.CallSubOrchestratorAsync(nameof(OrchestratorFunction), request);
                }

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(MessageProcessorOrchestrator)} has {messageCount} messages to process" });
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(MessageProcessorOrchestrator)} completed" });
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"An unexpected exception ocurred in {nameof(MessageProcessorOrchestrator)}\n{ex}" });
            }

            context.ContinueAsNew(null);
        }
    }
}
