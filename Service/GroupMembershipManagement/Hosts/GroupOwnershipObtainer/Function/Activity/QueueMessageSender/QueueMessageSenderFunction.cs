// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.ServiceBus;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class QueueMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IServiceBusQueueRepository _serviceBusQueueRepository = null;
        public QueueMessageSenderFunction(
            ILoggingRepository loggingRepository,
            IServiceBusQueueRepository serviceBusQueueRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusQueueRepository = serviceBusQueueRepository ?? throw new ArgumentNullException(nameof(serviceBusQueueRepository));
        }

        [FunctionName(nameof(QueueMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] MembershipAggregatorHttpRequest request)
        {

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(QueueMessageSenderFunction)} function started",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);

            var body = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

            var message = new ServiceBusMessage
            {
                MessageId = $"{request.SyncJob.RowKey}_{request.SyncJob.RunId}_{Guid.NewGuid()}",
                Body = body
            };

            await _serviceBusQueueRepository.SendMessageAsync(message);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Sent message {message.MessageId} to membership aggregator",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.INFO);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(QueueMessageSenderFunction)} function completed",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);
        }
    }
}