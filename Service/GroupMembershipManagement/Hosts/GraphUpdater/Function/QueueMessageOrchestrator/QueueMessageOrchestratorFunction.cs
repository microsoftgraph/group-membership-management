// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace Hosts.GraphUpdater
{
    public class QueueMessageOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public QueueMessageOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(QueueMessageOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            try
            {
                var request = await context.CallActivityAsync<MembershipHttpRequest>(nameof(MessageReaderFunction), null);
                if (request == null)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                    new LoggerRequest
                                                    {
                                                        Message = $"There are no more messages to process at this time.",
                                                        Verbosity = VerbosityLevel.INFO
                                                    });

                    return;
                }

                if (!context.IsReplaying)
                {
                    var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
                    _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                   new LoggerRequest
                                                   {
                                                       Message = $"Processing message for group {request.SyncJob.TargetOfficeGroupId}",
                                                       SyncJob = request.SyncJob,
                                                       Verbosity = VerbosityLevel.INFO,
                                                   });

                await context.CallSubOrchestratorAsync<OrchestrationRuntimeStatus>(nameof(OrchestratorFunction), request);
            }
            catch
            {
                // no op
                // exception was logged and handled in main orchestrator
                // we catch it here so we can get the next message from the queue.
            }

            context.ContinueAsNew(null);
        }
    }
}
