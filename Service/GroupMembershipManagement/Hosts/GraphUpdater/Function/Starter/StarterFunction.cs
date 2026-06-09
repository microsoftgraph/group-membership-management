// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using GraphUpdater.QueueMessageOrchestrator;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models;
using Models.ServiceBus;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly MembershipUpdaters _membershipUpdaters = null;
        private readonly MultiLaneConfig _multilaneConfig = null;
        private const string SUBSCRIPTION_PREFIX = "GraphUpdater";
        private const string SMALL_SUBSCRIPTION_NAME = "GraphUpdater_small_1";
        private const string SMALL_FUNCTION_NAME = $"{nameof(StarterFunction)}_small";
        private const string LARGE_SUBSCRIPTION_NAME = "GraphUpdater_large_1";
        private const string LARGE_FUNCTION_NAME = $"{nameof(StarterFunction)}_large";

        public StarterFunction(ILogger<StarterFunction> logger,
            MembershipUpdaters membershipUpdaters,
            IOptions<MultiLaneConfig> multilaneConfig)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
            _multilaneConfig = multilaneConfig?.Value ?? throw new ArgumentNullException(nameof(multilaneConfig));
        }

        [Function(SMALL_FUNCTION_NAME)]
        public async Task RunSmallLaneAsync(
           [ServiceBusTrigger("membershipupdaters", SMALL_SUBSCRIPTION_NAME, Connection = "gmmServiceBus")]
            ServiceBusReceivedMessage message,
           [DurableClient] DurableTaskClient client)
        {
            var groupMembership = JsonSerializer.Deserialize<GroupMembership>(Encoding.UTF8.GetString(message.Body));
            var additionalProperties = new Dictionary<string, object>
            {
                ["Instance"] = SMALL_SUBSCRIPTION_NAME,
                ["MessageIndex"] = groupMembership.MessageIndex,
                ["TotalMessageCount"] = groupMembership.TotalMessageCount
            };
            using var scope = _logger.BeginSyncJobScope(groupMembership.SyncJob, additionalProperties);

            _logger.FunctionStarted(SMALL_FUNCTION_NAME);
            _logger.ProcessingMessage(message.MessageId, groupMembership.TotalMembersToAdd ?? 0, groupMembership.TotalMembersToRemove ?? 0);

            var request = new OrchestratorMultiLaneRequest
            {
                GroupMembership = groupMembership,
                SubscriptionName = SMALL_SUBSCRIPTION_NAME,
                LaneSize = "small",
                TopicName = _membershipUpdaters.CurrentTopicName,
            };

            await client.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorMultiLaneFunction), request);

            _logger.FunctionCompleted(SMALL_FUNCTION_NAME);
        }

        [Function(LARGE_FUNCTION_NAME)]
        public async Task RunLargeLaneAsync(
            [ServiceBusTrigger("membershipupdaters", LARGE_SUBSCRIPTION_NAME, Connection = "gmmServiceBus", IsSessionsEnabled = true)]
             ServiceBusReceivedMessage message,
            [DurableClient] DurableTaskClient client)
        {
            var groupMembership = JsonSerializer.Deserialize<GroupMembership>(Encoding.UTF8.GetString(message.Body));
            var additionalProperties = new Dictionary<string, object>
            {
                ["Instance"] = LARGE_SUBSCRIPTION_NAME,
                ["MessageIndex"] = groupMembership.MessageIndex,
                ["TotalMessageCount"] = groupMembership.TotalMessageCount
            };
            using var scope = _logger.BeginSyncJobScope(groupMembership.SyncJob, additionalProperties);

            _logger.FunctionStarted(LARGE_FUNCTION_NAME);
            _logger.ProcessingMessage(message.MessageId, groupMembership.TotalMembersToAdd ?? 0, groupMembership.TotalMembersToRemove ?? 0);

            try
            {
                var instanceId = $"{groupMembership.RunId}_{message.SequenceNumber}";
                var orchestratorStatus = await client.GetInstanceAsync(instanceId);

                if (orchestratorStatus == null)
                {
                    var request = new OrchestratorMultiLaneRequest
                    {
                        GroupMembership = groupMembership,
                        SubscriptionName = LARGE_SUBSCRIPTION_NAME,
                        LaneSize = "large",
                        TopicName = _membershipUpdaters.CurrentTopicName,
                    };

                    await client.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorMultiLaneFunction), request, new StartOrchestrationOptions { InstanceId = instanceId });
                    await WaitForInstanceAsync(client, instanceId);

                }
                else if (orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Running
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Pending)
                {
                    await WaitForInstanceAsync(client, instanceId);
                }
                else if (orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed)

                {
                    _logger.MessageAlreadyProcessed(message.MessageId, message.SequenceNumber, orchestratorStatus.RuntimeStatus.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.StarterServiceBusError(ex, ex.Message);
                throw;
            }

            _logger.FunctionCompleted(LARGE_FUNCTION_NAME);
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
         [TimerTrigger("%triggerSchedule%")] TimerInfo myTimer,
         [DurableClient] DurableTaskClient client)
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

        private async Task ProcessTimerAsync(DurableTaskClient client, string subscriptionName)
        {
            _logger.FunctionStarted(nameof(StarterFunction));

            var orchestrator = nameof(QueueMessageOrchestratorFunction);
            var instanceId = $"{nameof(QueueMessageOrchestratorFunction)}_{subscriptionName.ToLowerInvariant()}";
            var orchestratorStatus = await client.GetInstanceAsync(instanceId);
            var isRunning = orchestratorStatus != null
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                    && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Failed;

            if (!isRunning)
            {
                _logger.CallingOrchestrator(instanceId);

                await client.ScheduleNewOrchestrationInstanceAsync(orchestrator, new QueueMessageOrchestratorRequest
                {
                    TopicName = _membershipUpdaters.CurrentTopicName,
                    LaneSize = _membershipUpdaters.CurrentLaneSize,
                    SubscriptionName = subscriptionName,
                    IsMultiLaneEnabled = _multilaneConfig.IsEnabled
                }, new StartOrchestrationOptions { InstanceId = instanceId });
            }

            _logger.FunctionCompleted(nameof(StarterFunction));
        }

        private async Task WaitForInstanceAsync(DurableTaskClient client, string instanceId)
        {
            var delay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);

            while (true)
            {
                await Task.Delay(delay);

                var orchestratorStatus = await client.GetInstanceAsync(instanceId);
                if (orchestratorStatus != null)
                {
                    // Only return when the orchestration has reached a terminal state.
                    if (orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed)
                    {
                        return;
                    }

                    // Otherwise (Running/Pending/ContinuedAsNew), keep waiting.
                }

                // If status is null or non-terminal, continue waiting with backoff.
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, maxDelay.TotalSeconds));
            }
        }
    }
}