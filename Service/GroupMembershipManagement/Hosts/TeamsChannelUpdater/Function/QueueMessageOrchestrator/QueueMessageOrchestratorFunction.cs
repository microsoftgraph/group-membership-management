// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
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
                
                var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
                if (!context.IsReplaying)
                {
                    _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                   new LoggerRequest
                                                   {
                                                       Message = $"Processing message for group {request.SyncJob.TargetOfficeGroupId}",
                                                       RunId = runId,
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
