// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly ServiceBusReceiver _serviceBusReceiver = null;

        public StarterFunction(ILogger<StarterFunction> logger, ServiceBusReceiver serviceBusReceiver)
        {
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            _serviceBusReceiver = serviceBusReceiver ?? throw new System.ArgumentNullException(nameof(serviceBusReceiver));
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
         [TimerTrigger("%triggerSchedule%")] TimerInfo myTimer,
         [DurableClient] DurableTaskClient starter)
        {
            _logger.FunctionStarted(nameof(StarterFunction));

            var instanceId = nameof(QueueMessageOrchestratorFunction);
            var orchestratorStatus = await starter.GetInstanceAsync(instanceId);
            var isRunning = orchestratorStatus != null
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Failed;

            if (!isRunning)
            {
                _logger.CallingOrchestrator(instanceId);
                await starter.ScheduleNewOrchestrationInstanceAsync(instanceId, (object)null, new StartOrchestrationOptions { InstanceId = instanceId });
            }

            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}

