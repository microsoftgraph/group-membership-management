// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services
{
    public class TopicMessageSenderService : ITopicMessageSenderService
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository = null;
        private readonly IServiceBusTopicsRepository _messageSplitterSender = null;
        private readonly MultiLaneConfig _multilaneConfig = null;
        private const string MESSAGE_SUBSCRIPTION_SMALL = "Small";
        private const string MESSAGE_SUBSCRIPTION_MEDIUM = "Medium";
        private const string MESSAGE_SUBSCRIPTION_LARGE = "Large";
        private const string MESSAGE_SUBSCRIPTION_ONBOARDING = "Onboarding";
        private const string LANE_SIZE_PROPERTY = "LaneSize";

        public TopicMessageSenderService(
            ILoggingRepository loggingRepository,
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            IServiceBusTopicsRepository messageSplitterSender,
            MultiLaneConfig multilaneConfig)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
            _messageSplitterSender = messageSplitterSender ?? throw new ArgumentNullException(nameof(messageSplitterSender));
            _multilaneConfig = multilaneConfig ?? throw new ArgumentNullException(nameof(multilaneConfig));
        }

        public async Task SendMessageAsync(MembershipHttpRequest request)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

            var message = new Models.ServiceBus.ServiceBusMessage
            {
                MessageId = $"{request.SyncJob.Id}_{request.SyncJob.RunId}_{request.SyncJob.MembershipType}",
                Body = body
            };

            message.ApplicationProperties.Add("Type", request.SyncJob.MembershipType);

            // Send message to the appropriate queue or topic
            if (_multilaneConfig.IsEnabled && request.SyncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                await SendMessageToTopicAsync(message, request);
            }
            else
            {
                await _serviceBusTopicsRepository.AddMessageAsync(message);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Sent message to {request.SyncJob.MembershipType} membership updater",
                    RunId = request.SyncJob.RunId
                }, VerbosityLevel.INFO);
            }
        }

        private async Task SendMessageToTopicAsync(Models.ServiceBus.ServiceBusMessage message, MembershipHttpRequest request)
        {
            if (request.SyncJob.LastRunTime == System.Data.SqlTypes.SqlDateTime.MinValue)
            {
                // New jobs
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_ONBOARDING);
            }
            else if (request.MembersToBeUpdated <= _multilaneConfig.Small)
            {
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_SMALL);
            }
            else if (request.MembersToBeUpdated <= _multilaneConfig.Medium)
            {
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_MEDIUM);
            }
            else
            {
                // Large
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_LARGE);
            }

            await _messageSplitterSender.AddMessageAsync(message);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Sent message to {message.ApplicationProperties[LANE_SIZE_PROPERTY]} lane with {request.MembersToBeUpdated} operations.",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.INFO);
        }
    }
}
