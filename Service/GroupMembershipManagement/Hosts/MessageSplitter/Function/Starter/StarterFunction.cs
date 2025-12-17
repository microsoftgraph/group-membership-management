// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using DIConcreteTypes;
using MessageSplitter.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Repositories.Contracts;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IMessageSplitterService _messageSplitterService;
        private readonly MembershipUpdaters _membershipUpdaters;
        private readonly IServiceBusTopicsRepository _messageSplitterTopicSenderRepository;
        private readonly RunLimiterSettings _runLimiterSettings;

        public StarterFunction(ILoggingRepository loggingRepository,
                               IMessageSplitterService messageSplitterService,
                               MembershipUpdaters membershipUpdaters,
                               [FromKeyedServices("messageSplitterTopicSenderRepository")] IServiceBusTopicsRepository messageSplitterTopicSenderRepository,
                               RunLimiterSettings runLimiterSettings)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipUpdaters = membershipUpdaters ?? throw new ArgumentNullException(nameof(membershipUpdaters));
            _messageSplitterService = messageSplitterService ?? throw new ArgumentNullException(nameof(messageSplitterService));
            _messageSplitterTopicSenderRepository = messageSplitterTopicSenderRepository ?? throw new ArgumentNullException(nameof(messageSplitterTopicSenderRepository));
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
        }

        [Function(nameof(StarterFunction))]
        public async Task ProcessServiceBusMessageAsync(
            [ServiceBusTrigger(topicName: "%serviceBusMessageSplitterTopic%", subscriptionName: "%messageSplitterSubscription%", Connection = "gmmServiceBus")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions actions,
            [DurableClient] DurableTaskClient durableClient)
        {
            var request = JsonSerializer.Deserialize<MembershipHttpRequest>(Encoding.UTF8.GetString(message.Body));
            var runId = request.SyncJob.RunId.GetValueOrDefault(Guid.Empty);

            _loggingRepository.SetSyncJobProperties(runId, request.SyncJob.ToDictionary());
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);

            try
            {
                var orchestrationRequest = CreateOrchestratorRequest(message, request);

                if(_runLimiterSettings.IsEnabled)
                    await EnqueuePendingWorkAsync(orchestrationRequest);
                else
                    await durableClient.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), orchestrationRequest);

                await actions.CompleteMessageAsync(message);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unexpected error {ex.Message}", RunId = runId }, VerbosityLevel.DEBUG);
                await _messageSplitterService.UpdateJobStatusAsync(request.SyncJob.Id, SyncStatus.Error);
                throw;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed", RunId = runId }, VerbosityLevel.DEBUG);
        }

        private OrchestratorRequest CreateOrchestratorRequest(ServiceBusReceivedMessage message, MembershipHttpRequest request)
        {
            var updaterType = message.ApplicationProperties["Type"].ToString();
            var subscription = _membershipUpdaters.AvailableInstances[updaterType][_membershipUpdaters.CurrentLaneSize];
            return new OrchestratorRequest
            {
                MembershipRequest = request,
                MessageId = message.MessageId,
                UpdaterType = updaterType,
                SubscriptionName = subscription.Name,
                CurrentLaneSize = _membershipUpdaters.CurrentLaneSize
            };
        }

        private async Task EnqueuePendingWorkAsync(OrchestratorRequest orchestrationRequest)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(orchestrationRequest));
            var pendingMessage = new Models.ServiceBus.ServiceBusMessage
            {
                // Must be unique across retries due to topic duplicate detection.
                MessageId = $"pending_{orchestrationRequest.MembershipRequest.SyncJob.RunId}_{Guid.NewGuid()}"
            };

            pendingMessage.Body = body;

            pendingMessage.ApplicationProperties["MessageType"] = $"pending_{orchestrationRequest.CurrentLaneSize.ToLowerInvariant()}";

            await _messageSplitterTopicSenderRepository.AddMessageAsync(pendingMessage);
        }
    }
}
