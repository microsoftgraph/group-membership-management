// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DIConcreteTypes;
using Hosts.MembershipAggregator;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<TopicMessageSenderService> _logger;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository = null;
        private readonly IServiceBusTopicsRepository _messageSplitterSender = null;
        private readonly MultiLaneConfig _multilaneConfig = null;
        private const string MESSAGE_SUBSCRIPTION_SMALL = "Small";
        private const string MESSAGE_SUBSCRIPTION_LARGE = "Large";
        private const string LANE_SIZE_PROPERTY = "LaneSize";

        public TopicMessageSenderService(
            ILogger<TopicMessageSenderService> logger,
            IServiceBusTopicsRepository serviceBusTopicsRepository,
            IServiceBusTopicsRepository messageSplitterSender,
            MultiLaneConfig multilaneConfig)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                _logger.SentToMembershipUpdater(request.SyncJob.MembershipType);
            }
        }

        private async Task SendMessageToTopicAsync(Models.ServiceBus.ServiceBusMessage message, MembershipHttpRequest request)
        {
            if (request.MembersToBeUpdated <= _multilaneConfig.Small)
            {
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_SMALL);
            }
            else
            {
                // Large
                message.ApplicationProperties.Add(LANE_SIZE_PROPERTY, MESSAGE_SUBSCRIPTION_LARGE);
            }

            await _messageSplitterSender.AddMessageAsync(message);

            _logger.SentToLane((string)message.ApplicationProperties[LANE_SIZE_PROPERTY], request.MembersToBeUpdated);
        }
    }
}
