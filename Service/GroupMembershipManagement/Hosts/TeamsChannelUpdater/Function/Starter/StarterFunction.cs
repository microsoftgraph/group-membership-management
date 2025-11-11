// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ServiceBusReceiver _serviceBusReceiver = null;

        public StarterFunction(ILoggingRepository loggingRepository, ServiceBusReceiver serviceBusReceiver)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusReceiver = serviceBusReceiver ?? throw new ArgumentNullException(nameof(serviceBusReceiver));
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
         [TimerTrigger("%triggerSchedule%")] TimerInfo myTimer,
         [DurableClient] DurableTaskClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);

            var instanceId = nameof(QueueMessageOrchestratorFunction);
            var orchestratorStatus = await starter.GetInstanceAsync(instanceId);
            var isRunning = orchestratorStatus != null
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Failed;

            if (!isRunning)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Calling {instanceId}" }, VerbosityLevel.INFO);
                await starter.ScheduleNewOrchestrationInstanceAsync(instanceId, (object)null, new StartOrchestrationOptions { InstanceId = instanceId });
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}

