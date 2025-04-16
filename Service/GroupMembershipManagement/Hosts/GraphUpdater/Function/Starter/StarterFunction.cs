// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using GraphUpdater.QueueMessageOrchestrator;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Options;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly ServiceBusReceiver _serviceBusReceiver = null;
        private readonly MembershipUpdaters _membershipUpdaters = null;
        private readonly MultiLaneConfig _multilaneConfig = null;
        private const string SUBSCRIPTION_PREFIX = "GraphUpdater";

        public StarterFunction(ILoggingRepository loggingRepository,
            ServiceBusReceiver serviceBusReceiver,
            MembershipUpdaters membershipUpdaters,
            IOptions<MultiLaneConfig> multilaneConfig)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusReceiver = serviceBusReceiver ?? throw new ArgumentNullException(nameof(serviceBusReceiver));
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
            _multilaneConfig = multilaneConfig?.Value ?? throw new ArgumentNullException(nameof(multilaneConfig));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task RunAsync(
         [TimerTrigger("%triggerSchedule%")] TimerInfo myTimer,
         [DurableClient] IDurableOrchestrationClient client)
        {
            // Handle the default GraphUpdater instance
            if (_multilaneConfig.IsEnabled && string.IsNullOrWhiteSpace(_membershipUpdaters.CurrentLaneSize))
            {
                return;
            }

            // Handle the multilane GraphUpdater instances
            if (!_multilaneConfig.IsEnabled && !string.IsNullOrWhiteSpace(_membershipUpdaters.CurrentLaneSize))
            {
                return;
            }

            if (_multilaneConfig.IsEnabled)
            {
                if (_multilaneConfig.TriggerDelay > 0)
                {
                    await Task.Delay(_multilaneConfig.TriggerDelay * 1000);
                }

                var starters = new List<Task>();
                foreach (var instance in GetInstanceInformation())
                {
                    starters.Add(ProcessTimerAsync(client, instance));
                }

                await Task.WhenAny(starters);
            }
            else
            {
                await ProcessTimerAsync(client, SUBSCRIPTION_PREFIX);
            }
        }

        private List<string> GetInstanceInformation()
        {
            var instancePrefix = _membershipUpdaters.CurrentLaneSize.ToLowerInvariant();
            var laneCount = _membershipUpdaters.AvailableInstances["GroupMembership"][_membershipUpdaters.CurrentLaneSize].Instances;

            var instances = new List<string>();
            for (int i = 1; i <= laneCount; i++)
            {
                instances.Add($"{SUBSCRIPTION_PREFIX}_{instancePrefix}_{i}");
            }

            return instances;
        }


        private async Task ProcessTimerAsync(IDurableOrchestrationClient client, string subscriptionName)
        {
            var additionalProperties = new Dictionary<string, string> { { "Instance", subscriptionName } };
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(StarterFunction)} function started",
                DynamicProperties = additionalProperties
            }, VerbosityLevel.DEBUG);

            var orchestrator = nameof(QueueMessageOrchestratorFunction);
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";
            var orchestratorStatus = await client.GetStatusAsync(instanceId);
            var isRunning = orchestratorStatus != null
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Failed;

            if (!isRunning)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Calling {instanceId}",
                    DynamicProperties = additionalProperties
                }, VerbosityLevel.INFO);

                await client.StartNewAsync(orchestrator, instanceId, new QueueMessageOrchestratorRequest
                {
                    TopicName = _membershipUpdaters.CurrentTopicName,
                    LaneSize = _membershipUpdaters.CurrentLaneSize,
                    SubscriptionName = subscriptionName,
                    IsMultiLaneEnabled = _multilaneConfig.IsEnabled
                });
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(StarterFunction)} function completed",
                DynamicProperties = additionalProperties
            }, VerbosityLevel.DEBUG);
        }
    }
}