// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class TopicMessageSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IServiceBusTopicsRepository _serviceBusTopicsRepository = null;
        public TopicMessageSenderFunction(ILoggingRepository loggingRepository, IServiceBusTopicsRepository serviceBusTopicsRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusTopicsRepository = serviceBusTopicsRepository ?? throw new ArgumentNullException(nameof(serviceBusTopicsRepository));
        }

        [FunctionName(nameof(TopicMessageSenderFunction))]
        public async Task SendMessageAsync([ActivityTrigger] MembershipHttpRequest request)
        {

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function started",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);

            var destinations = JArray.Parse(request.SyncJob.Destination);
            var destinationType = destinations.SelectTokens("$..type").Select(x => x.Value<string>()).First();
            var body = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

            var message = new ServiceBusMessage
            {
                MessageId = $"{request.SyncJob.PartitionKey}_{request.SyncJob.RowKey}_{request.SyncJob.RunId}_{destinationType}",
                Body = body
            };

            message.ApplicationProperties.Add("Type", destinationType);

            await _serviceBusTopicsRepository.AddMessageAsync(message);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Sent message to {destinationType} membership updater",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.INFO);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(TopicMessageSenderFunction)} function completed",
                RunId = request.SyncJob.RunId
            }, VerbosityLevel.DEBUG);
        }
    }
}