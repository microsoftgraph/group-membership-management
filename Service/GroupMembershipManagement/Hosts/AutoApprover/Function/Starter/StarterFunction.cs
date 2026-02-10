// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Models;
using Repositories.Contracts;

namespace Hosts.AutoApprover
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IConfiguration _configuration;

        public StarterFunction(ILoggingRepository loggingRepository, IConfiguration configuration)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [Function(nameof(StarterFunction))]
        public async Task RunAsync(
            [ServiceBusTrigger("%serviceBusAutoApproverQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message)
        {
            if (!CommonServices.GetBoolSettingBase(_configuration, "AutoApprover:IsEnabled", false))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = "AutoApprover is disabled. Skipping message processing."
                }, VerbosityLevel.DEBUG);
                return;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(StarterFunction)} function started"
            }, VerbosityLevel.DEBUG);

            var messageBody = Encoding.UTF8.GetString(message.Body.ToArray());
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"AutoApprover message received. MessageId: {message.MessageId}. BodyLength: {messageBody.Length}"
            }, VerbosityLevel.DEBUG);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(StarterFunction)} function completed"
            }, VerbosityLevel.DEBUG);
        }
    }
}
