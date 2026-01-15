// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class LeaseRenewOrchestratorFunction
    {
        private readonly RunLimiterSettings _runLimiterSettings;

        public LeaseRenewOrchestratorFunction(RunLimiterSettings runLimiterSettings)
        {
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
        }

        [Function(nameof(LeaseRenewOrchestratorFunction))]
        public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            if (!_runLimiterSettings.IsEnabled)
            {
                return;
            }

            var request = context.GetInput<MessageSplitterLeaseRenewSignal>();
            var lane = (request.LaneSize ?? "large").ToLowerInvariant();
            var entityId = new EntityInstanceId(nameof(RunLimiter), lane);

            var response = await context.Entities.CallEntityAsync<RenewLeaseResponse>(
                entityId,
                nameof(RunLimiter.Renew),
                new RenewLeaseRequest(request.RunId, request.LeaseTimeoutMinutes, context.CurrentUtcDateTime));

            if (!response.Renewed)
            {
                await context.CallActivityAsync(
                    nameof(LoggerFunction),
                    new LoggerRequest
                    {
                        Message = new LogMessage
                        {
                            Message = $"Lease renew signal ignored; no existing lease found. lane={lane}",
                            RunId = request.RunId
                        },
                        Verbosity = VerbosityLevel.INFO
                    });
            }
        }
    }
}
