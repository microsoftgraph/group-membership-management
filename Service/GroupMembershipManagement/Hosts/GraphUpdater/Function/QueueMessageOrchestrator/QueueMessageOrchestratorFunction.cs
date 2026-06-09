// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.QueueMessageOrchestrator;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class QueueMessageOrchestratorFunction
    {
        [Function(nameof(QueueMessageOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("GraphUpdater.QueueMessageOrchestratorFunction");
            var orchestratorRequest = context.GetInput<QueueMessageOrchestratorRequest>();

            try
            {
                var request = await context.CallActivityAsync<OrchestratorRequest>(nameof(MessageReaderFunction), orchestratorRequest);

                if (request == null)
                {
                    logger.NoMoreMessages();
                    return;
                }

                var runId = orchestratorRequest.IsMultiLaneEnabled ? request.GroupMembership.RunId : request.MembershipHttpRequest.SyncJob.RunId.GetValueOrDefault();
                var syncJob = orchestratorRequest.IsMultiLaneEnabled ? request.GroupMembership.SyncJob : request.MembershipHttpRequest.SyncJob;
                var additionalProperties = new Dictionary<string, object>
                {
                    ["Instance"] = orchestratorRequest.SubscriptionName ?? string.Empty
                };
                using var scope = logger.BeginSyncJobScope(syncJob, additionalProperties);

                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), new GetGroupRequest { SyncJob = syncJob });

                logger.ProcessingMessageForGroup(groupId);

                if (orchestratorRequest.IsMultiLaneEnabled)
                    await context.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(nameof(OrchestratorMultiLaneFunction), new OrchestratorMultiLaneRequest
                    {
                        RunId = runId,
                        TopicName = orchestratorRequest.TopicName,
                        LaneSize = orchestratorRequest.LaneSize,
                        SubscriptionName = orchestratorRequest.SubscriptionName,
                        GroupMembership = request.GroupMembership
                    });
                else
                    await context.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(nameof(OrchestratorFunction), request.MembershipHttpRequest);
            }
            catch (Exception ex)
            {
                logger.QueueOrchestratorException(ex, ex.Message);
            }

            // Not needed for multi-lane, as the time interval will determine when the next message is processed.
            if (!orchestratorRequest.IsMultiLaneEnabled)
            {
                context.ContinueAsNew(orchestratorRequest);
            }
        }
    }
}
