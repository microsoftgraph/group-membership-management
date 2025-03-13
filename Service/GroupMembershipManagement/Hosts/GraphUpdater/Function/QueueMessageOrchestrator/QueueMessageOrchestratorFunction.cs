// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.QueueMessageOrchestrator;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class QueueMessageOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public QueueMessageOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(QueueMessageOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            Guid runId = Guid.Empty;
            SyncJob syncJob = null;
            var orchestratorRequest = context.GetInput<QueueMessageOrchestratorRequest>();

            try
            {
                var request = await context.CallActivityAsync<OrchestratorRequest>(nameof(MessageReaderFunction), orchestratorRequest);

                if (request == null)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                    new LoggerRequest
                                                    {
                                                        Message = $"There are no more messages to process at this time.",
                                                        Verbosity = VerbosityLevel.INFO,
                                                        AdditionalProperties = new Dictionary<string, string>
                                                        {
                                                            { "Instance", orchestratorRequest.SubscriptionName }
                                                        }
                                                    });

                    return;
                }

                runId = orchestratorRequest.IsMultiLaneEnabled ? request.GroupMembership.RunId : request.MembershipHttpRequest.SyncJob.RunId.GetValueOrDefault();
                syncJob = orchestratorRequest.IsMultiLaneEnabled ? request.GroupMembership.SyncJob : request.MembershipHttpRequest.SyncJob;
                var groupId = syncJob.TargetOfficeGroupId;
                var syncJobProperties = syncJob.ToDictionary();
                syncJobProperties.Add("Instance", orchestratorRequest.SubscriptionName);
                _loggingRepository.SetSyncJobProperties(runId, syncJobProperties);

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                   new LoggerRequest
                                                   {
                                                       Message = $"Processing message for group {groupId}",
                                                       SyncJob = syncJob,
                                                       Verbosity = VerbosityLevel.INFO
                                                   });

                if (orchestratorRequest.IsMultiLaneEnabled)
                    await context.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(nameof(OrchestratorMultiLaneFunction), new OrchestratorMultiLaneRequest
                    {
                        RunId = runId,
                        TopicName = orchestratorRequest.TopicName,
                        SubscriptionName = orchestratorRequest.SubscriptionName,
                        GroupMembership = request.GroupMembership
                    });
                else
                    await context.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(nameof(OrchestratorFunction), request.MembershipHttpRequest);
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                   new LoggerRequest
                                                   {
                                                       Message = $"Unexpected exception: {ex.Message}",
                                                       SyncJob = syncJob,
                                                       Verbosity = VerbosityLevel.INFO,
                                                       AdditionalProperties = new Dictionary<string, string>
                                                       {
                                                           { "Instance", orchestratorRequest.SubscriptionName }
                                                       }
                                                   });
            }
            finally
            {
                _loggingRepository.RemoveSyncJobProperties(runId);
            }

            // Not needed for multi-lane, as the time interval will determine when the next message is processed.
            if (!orchestratorRequest.IsMultiLaneEnabled)
            {
                context.ContinueAsNew(orchestratorRequest);
            }
        }
    }
}
