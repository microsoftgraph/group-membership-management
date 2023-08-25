// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
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
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(MessageOrchestrator)} function started", SyncJob = request.SyncJob });

                var messageTrackerId = new EntityId(MessageTrackerFunction.EntityName, MessageTrackerFunction.EntityKey);
                var messageTrackerProxy = context.CreateEntityProxy<IMessageTracker>(messageTrackerId);
                var messageEntityId = new EntityId(nameof(MessageEntity), $"{request.SyncJob.TargetOfficeGroupId}_{runId}");
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
                                                            SyncJob = request.SyncJob
                                                        });
                    }
                    else
                    {
                        await context.CallActivityAsync(nameof(LoggerFunction),
                                new LoggerRequest
                                {
                                    Message = $"Calling {nameof(MessageProcessorOrchestrator)} for jobId {request.SyncJob.JobId}}",
                                    SyncJob = request.SyncJob
                                });

                        context.StartNewOrchestration(nameof(MessageProcessorOrchestrator), null, nameof(MessageProcessorOrchestrator));
                    }
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"{nameof(MessageOrchestrator)} function completed",
                                                    SyncJob = request.SyncJob
                                                });
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"Unexpected exception occurred in {nameof(MessageOrchestrator)}\n{ex}",
                                                    SyncJob = request.SyncJob
                                                });
            }
        }
    }
}
