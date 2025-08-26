// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using GraphUpdater.QueueMessageOrchestrator;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Options;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly MembershipUpdaters _membershipUpdaters = null;
        private readonly MultiLaneConfig _multilaneConfig = null;
        private const string SUBSCRIPTION_PREFIX = "GraphUpdater";
        private const string SMALL_SUBSCRIPTION_NAME = "GraphUpdater_small_1";
        private const string SMALL_FUNCTION_NAME = $"{nameof(StarterFunction)}_small";
        private const string LARGE_SUBSCRIPTION_NAME = "GraphUpdater_large_1";
        private const string LARGE_FUNCTION_NAME = $"{nameof(StarterFunction)}_large";

        public StarterFunction(ILoggingRepository loggingRepository,
            MembershipUpdaters membershipUpdaters,
            IOptions<MultiLaneConfig> multilaneConfig)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
            _multilaneConfig = multilaneConfig?.Value ?? throw new ArgumentNullException(nameof(multilaneConfig));
        }

        [FunctionName(SMALL_FUNCTION_NAME)]
        public async Task RunSmallLaneAsync(
           [ServiceBusTrigger("membershipupdaters", SMALL_SUBSCRIPTION_NAME, Connection = "gmmServiceBus")]
            ServiceBusReceivedMessage message,
           [DurableClient] IDurableOrchestrationClient client)
        {
            var groupMembership = JsonSerializer.Deserialize<GroupMembership>(Encoding.UTF8.GetString(message.Body));
            var dynamicProperties = groupMembership.SyncJob.ToDictionary();
            dynamicProperties.Add("Instance", SMALL_SUBSCRIPTION_NAME);
            _loggingRepository.SetSyncJobProperties(groupMembership.RunId, dynamicProperties);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{SMALL_FUNCTION_NAME} function started.",
                RunId = groupMembership.RunId,
            });

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Processing message {message.MessageId} " +
                          $"with {groupMembership.TotalMembersToAdd ?? 0} additions " +
                          $"and {groupMembership.TotalMembersToRemove ?? 0} removals.",
                RunId = groupMembership.RunId,
            });

            var request = new OrchestratorMultiLaneRequest
            {
                GroupMembership = groupMembership,
                SubscriptionName = SMALL_SUBSCRIPTION_NAME,
                LaneSize = "small",
                TopicName = _membershipUpdaters.CurrentTopicName,
            };

            await client.StartNewAsync(nameof(OrchestratorMultiLaneFunction), null, request);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{SMALL_FUNCTION_NAME} function completed.",
                RunId = groupMembership.RunId,
            });
        }

        [FunctionName(LARGE_FUNCTION_NAME)]
        public async Task RunLargeLaneAsync(
            [ServiceBusTrigger("membershipupdaters", LARGE_SUBSCRIPTION_NAME, Connection = "gmmServiceBus", IsSessionsEnabled = true)]
             ServiceBusReceivedMessage message,
            [DurableClient] IDurableOrchestrationClient client)
        {
            var groupMembership = JsonSerializer.Deserialize<GroupMembership>(Encoding.UTF8.GetString(message.Body));
            var dynamicProperties = groupMembership.SyncJob.ToDictionary();
            dynamicProperties.Add("Instance", LARGE_SUBSCRIPTION_NAME);
            _loggingRepository.SetSyncJobProperties(groupMembership.RunId, dynamicProperties);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{LARGE_FUNCTION_NAME} function started.",
                RunId = groupMembership.RunId,
            });

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Processing message {message.MessageId} " +
                          $"with {groupMembership.TotalMembersToAdd ?? 0} additions " +
                          $"and {groupMembership.TotalMembersToRemove ?? 0} removals.",
                RunId = groupMembership.RunId,
            });

            try
            {
                var instanceId = $"{groupMembership.RunId}_{message.SequenceNumber}";
                var orchestratorStatus = await client.GetStatusAsync(instanceId);

                if (orchestratorStatus == null)
                {
                    var request = new OrchestratorMultiLaneRequest
                    {
                        GroupMembership = groupMembership,
                        SubscriptionName = LARGE_SUBSCRIPTION_NAME,
                        LaneSize = "large",
                        TopicName = _membershipUpdaters.CurrentTopicName,
                    };

                    await client.StartNewAsync(nameof(OrchestratorMultiLaneFunction), instanceId, request);
                    await WaitForInstanceAsync(client, instanceId);

                }
                else if (orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Running
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Pending
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.ContinuedAsNew)
                {
                    await WaitForInstanceAsync(client, instanceId);
                }
                else if (orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Completed
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Terminated
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Failed
                        || orchestratorStatus.RuntimeStatus == OrchestrationRuntimeStatus.Canceled)

                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Message {message.MessageId} ({message.SequenceNumber}) was already processed ended as {orchestratorStatus.RuntimeStatus}"
                    });
                }
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error processing Service Bus message: {ex.Message}"
                });

                throw;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{LARGE_FUNCTION_NAME} function completed.",
                RunId = groupMembership.RunId,
            });
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

        private async Task WaitForInstanceAsync(IDurableOrchestrationClient client, string instanceId)
        {
            var delay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);

            while (true)
            {
                await Task.Delay(delay);

                var orchestratorStatus = await client.GetStatusAsync(instanceId);
                if (orchestratorStatus == null)
                {
                    // Keep waiting
                }
                else if (orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Completed
                        && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Terminated
                        && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Failed
                        && orchestratorStatus.RuntimeStatus != OrchestrationRuntimeStatus.Canceled)
                {
                    return;
                }

                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, maxDelay.TotalSeconds));
            }
        }
    }
}