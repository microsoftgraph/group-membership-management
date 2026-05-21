// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hosts.AutoApprover
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly IConfiguration _configuration;

        public StarterFunction(ILogger<StarterFunction> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [Function(nameof(StarterFunction))]
        public Task Run(
            [ServiceBusTrigger("%serviceBusAutoApproverQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message)
        {
            if (!CommonServices.GetBoolSettingBase(_configuration, "AutoApprover:IsEnabled", false))
            {
                _logger.AutoApproverDisabled();
                return Task.CompletedTask;
            }

            _logger.FunctionStarted(nameof(StarterFunction));

            var messageBody = Encoding.UTF8.GetString(message.Body.ToArray());
            _logger.MessageReceived(message.MessageId, messageBody.Length);

            _logger.FunctionCompleted(nameof(StarterFunction));
            return Task.CompletedTask;
        }
    }
}
