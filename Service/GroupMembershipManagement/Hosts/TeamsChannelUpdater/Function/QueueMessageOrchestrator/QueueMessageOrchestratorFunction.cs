// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class QueueMessageOrchestratorFunction
    {
        public QueueMessageOrchestratorFunction()
        {
        }

        [Function(nameof(QueueMessageOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("TeamsChannelUpdater.QueueMessageOrchestratorFunction");

            try
            {
                var request = await context.CallActivityAsync<MembershipHttpRequest>(nameof(MessageReaderFunction), null);
                if (request == null)
                {
                    logger.NoMoreMessages();
                    return;
                }

                using var scope = logger.BeginSyncJobScope(request.SyncJob);

                logger.ProcessingMessage(request.GroupId);

                await context.CallSubOrchestratorAsync(nameof(OrchestratorFunction), request);
            }
            catch
            {
                // no op
                // exception was logged and handled in main orchestrator
                // we catch it here so we can get the next message from the queue.
            }

            context.ContinueAsNew((object)null);
        }
    }
}

