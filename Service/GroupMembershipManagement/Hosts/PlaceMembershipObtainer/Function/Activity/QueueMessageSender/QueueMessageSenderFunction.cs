// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class QueueMessageSenderFunction
    {
        private readonly ILogger<QueueMessageSenderFunction> _logger;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository;

        public QueueMessageSenderFunction(
            ILogger<QueueMessageSenderFunction> logger,
            IServiceBusQueueRepository serviceBusQueueRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
        }

        [Function(nameof(QueueMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] MembershipAggregatorHttpRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob))
            {
                _logger.FunctionStarted(nameof(QueueMessageSenderFunction));

                var body = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

                var message = new ServiceBusMessage
                {
                    MessageId = $"{request.SyncJob.Id}_{request.SyncJob.RunId}_{Guid.NewGuid()}",
                    Body = body
                };

                await _serviceBusQueueRepository.SendMessageAsync(message);

                _logger.SentMessageToMembershipAggregator(message.MessageId);

                _logger.FunctionCompleted(nameof(QueueMessageSenderFunction));
            }
        }
    }
}