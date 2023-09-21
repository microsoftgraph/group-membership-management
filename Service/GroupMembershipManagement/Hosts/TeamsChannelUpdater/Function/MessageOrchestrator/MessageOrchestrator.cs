// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class MessageOrchestrator
    {
        [FunctionName(nameof(MessageOrchestrator))]
        public async Task RunMessageOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<MembershipHttpRequest>();
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            try
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(MessageOrchestrator)} function started", RunId = runId });

                var messageTrackerId = new EntityId(MessageTrackerFunction.EntityName, MessageTrackerFunction.EntityKey);
                var messageTrackerProxy = context.CreateEntityProxy<IMessageTracker>(messageTrackerId);
                var messageEntityId = new EntityId(MessageEntity.EntityName, $"{request.SyncJob.TargetOfficeGroupId}_{runId}");
                var messageEntityProxy = context.CreateEntityProxy<IMessageEntity>(messageEntityId);

                using (await context.LockAsync(messageTrackerId, messageEntityId))
                {
                    await messageTrackerProxy.AddAsync(messageEntityId.EntityKey);
                    await messageEntityProxy.SaveAsync(request);

                    var processorStatus = await context.CallActivityAsync<DurableOrchestrationStatus>(nameof(StatusReaderFunction), nameof(MessageProcessorOrchestrator));
                    if (processorStatus != null
                        && processorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                        && processorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                        && processorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Failed)
                    {
                        await context.CallActivityAsync(nameof(LoggerFunction),
                                                        new LoggerRequest
                                                        {
                                                            Message = $"{nameof(MessageProcessorOrchestrator)} is {processorStatus.RuntimeStatus}, queued message {messageEntityId.EntityKey}",
                                                            RunId = runId
                                                        });
                    }
                    else
                    {
                        await context.CallActivityAsync(nameof(LoggerFunction),
                                new LoggerRequest
                                {
                                    Message = $"Calling {nameof(MessageProcessorOrchestrator)} for jobId {request.SyncJob.Id}",
                                    RunId = runId
                                });

                        context.StartNewOrchestration(nameof(MessageProcessorOrchestrator), null, nameof(MessageProcessorOrchestrator));
                    }
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"{nameof(MessageOrchestrator)} function completed",
                                                    RunId = runId
                                                });
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"Unexpected exception occurred in {nameof(MessageOrchestrator)}\n{ex}",
                                                    RunId = runId
                                                });
            }
        }
    }
}
