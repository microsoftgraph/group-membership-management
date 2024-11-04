// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using MessageSplitter.Contracts;
using MessageSplitter.Entities;
using MessageSplitter.TrackerOrchestrator;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Models;
using Repositories.Contracts;
using System.Text;
using System.Text.Json;

namespace Hosts.MessageSplitter
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IMessageSplitterService _messageSplitterService;
        private readonly MembershipUpdaters _membershipUpdaters;

        public StarterFunction(ILoggingRepository loggingRepository,
                               IMessageSplitterService messageSplitterService,
                               MembershipUpdaters membershipUpdaters)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
            _messageSplitterService = messageSplitterService ?? throw new ArgumentNullException(nameof(messageSplitterService));
        }

        [Function(nameof(StarterFunction))]
        [Singleton(Mode = SingletonMode.Function)]
        public async Task ProcessServiceBusMessageAsync(
            [ServiceBusTrigger(topicName: "%serviceBusMessageSplitterTopic%", subscriptionName: "%messageSplitterSubscription%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message,
            ServiceBusMessageActions actions,
            [DurableClient] DurableTaskClient starter)
        {
            var request = JsonSerializer.Deserialize<MembershipHttpRequest>(Encoding.UTF8.GetString(message.Body));
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            try
            {
                var instanceToUse = await GetMembershipUpdaterInstanceToUseAsync(message, starter);
                var instanceId = await RunMainOrchestratorAsync(message, request, starter, instanceToUse);
                await actions.CompleteMessageAsync(message);
                await WaitForOrchestratorToCompleteAsync(instanceId, starter);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unexpected error {ex.Message}", RunId = runId }, VerbosityLevel.DEBUG);
                await _messageSplitterService.UpdateJobStatusAsync(request.SyncJob.Id, SyncStatus.Error);
                throw;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);

        }

        private async Task<string> RunMainOrchestratorAsync(
                                    ServiceBusReceivedMessage message,
                                    MembershipHttpRequest request,
                                    DurableTaskClient starter,
                                    int instanceToUse)
        {
            var updaterType = message.ApplicationProperties["Type"].ToString();
            var subscription = _membershipUpdaters.AvailableInstances[updaterType][_membershipUpdaters.CurrentLaneSize];
            var orchestrationRequest = new OrchestratorRequest
            {
                MembershipRequest = request,
                MessageId = message.MessageId,
                UpdaterType = updaterType,
                SubscriptionName = subscription.Name,
                InstanceToUse = instanceToUse,
                CurrentLaneSize = _membershipUpdaters.CurrentLaneSize
            };

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), orchestrationRequest);

            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"InstanceID: '{instanceId}'", RunId = runId });
            return instanceId;
        }


        private async Task WaitForOrchestratorToCompleteAsync(string instanceId, DurableTaskClient starter)
        {
            if (_membershipUpdaters.CurrentLaneSize.Equals("small", StringComparison.InvariantCultureIgnoreCase) ||
                _membershipUpdaters.CurrentLaneSize.Equals("medium", StringComparison.InvariantCultureIgnoreCase))
            {
                await starter.WaitForInstanceCompletionAsync(instanceId);
            }
            else
            {
                var isCompleted = false;
                do
                {
                    await Task.Delay(5000);
                    var instanceMetadata = await starter.GetInstanceAsync(instanceId);
                    isCompleted = IsOrchestrationCompleted(instanceMetadata.RuntimeStatus);
                }
                while (!isCompleted);
            }
        }

        private async Task<int> GetMembershipUpdaterInstanceToUseAsync(ServiceBusReceivedMessage message, DurableTaskClient starter)
        {
            var updaterType = message.ApplicationProperties["Type"].ToString();
            var instanceTrackerEntityId = new EntityInstanceId(nameof(InstanceTracker), nameof(InstanceTracker));
            var updaterInstanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(InstanceTrackerOrchestrator),
                                                                                        new InstanceTrackerOrchestratorRequest(updaterType,
                                                                                                                               _membershipUpdaters.CurrentLaneSize));
            await starter.WaitForInstanceCompletionAsync(updaterInstanceId);
            var instanceTracker = await starter.Entities.GetEntityAsync<int>(instanceTrackerEntityId);
            return instanceTracker.State;
        }

        private bool IsOrchestrationCompleted(OrchestrationRuntimeStatus status)
        {
            return status == OrchestrationRuntimeStatus.Completed
                || status == OrchestrationRuntimeStatus.Failed
                || status == OrchestrationRuntimeStatus.Terminated;
        }
    }
}
