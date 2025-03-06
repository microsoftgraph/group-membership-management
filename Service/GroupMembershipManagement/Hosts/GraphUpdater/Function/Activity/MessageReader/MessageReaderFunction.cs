// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class MessageReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ServiceBusReceiver _serviceBusReceiver;

        public MessageReaderFunction(ILoggingRepository loggingRepository, ServiceBusReceiver serviceBusReceiver)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _serviceBusReceiver = serviceBusReceiver ?? throw new ArgumentNullException(nameof(serviceBusReceiver));
        }

        [FunctionName(nameof(MessageReaderFunction))]
        public async Task<MembershipHttpRequest> GetSyncJobAsync([ActivityTrigger] object input)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(MessageReaderFunction)} function started" }, VerbosityLevel.DEBUG);

            MembershipHttpRequest request = null;
            var message = await _serviceBusReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

            if (message != null)
            {
                await _serviceBusReceiver.CompleteMessageAsync(message);
                request = JsonConvert.DeserializeObject<MembershipHttpRequest>(Encoding.UTF8.GetString(message.Body));
            }

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(MessageReaderFunction)} function started" }, VerbosityLevel.DEBUG);
            return request;
        }
    }
}
