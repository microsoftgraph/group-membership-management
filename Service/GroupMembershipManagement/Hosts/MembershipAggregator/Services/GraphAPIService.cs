// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Polly;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services
{
    public class GraphAPIService : IGraphAPIService
    {
        private const int NumberOfGraphRetries = 5;
        private readonly ILogger<GraphAPIService> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IServiceBusQueueRepository _notificationsQueueRepository;

        public GraphAPIService(
                ILogger<GraphAPIService> logger,
                IGraphGroupRepository graphGroupRepository,
                IServiceBusQueueRepository notificationsQueueRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _notificationsQueueRepository = notificationsQueueRepository ?? throw new ArgumentNullException(nameof(notificationsQueueRepository));
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task<PolicyResult<bool>> GroupExistsAsync(Guid groupId)
        {
            var graphRetryPolicy = Policy.Handle<SocketException>()
                                    .WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                   onRetry: (ex, sleepDuration) =>
                   {
                       _logger.TransientSocketException(ex, sleepDuration, NumberOfGraphRetries);
                   });

            return await graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(groupId));
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupRepository.GetGroupOwnersAsync(groupObjectId, top);
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            return await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(email, groupObjectId);
        }
        public async Task SendEmailAsync(SyncJob job, NotificationMessageType notificationType, string[] additionalContentParameters)
        {
			 var messageContent = new Dictionary<string, Object>
            {
                { "SyncJob", job },
                { "AdditionalContentParameters", additionalContentParameters }
            };
            var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageContent));
            var message = new ServiceBusMessage
            {
                MessageId = $"{job.Id}_{job.RunId}_{notificationType}",
                Body = body
            };
            message.ApplicationProperties.Add("MessageType", notificationType.ToString());
            await _notificationsQueueRepository.SendMessageAsync(message);
            _logger.SentGraphNotificationMessage(message.MessageId);
        }
    }
}