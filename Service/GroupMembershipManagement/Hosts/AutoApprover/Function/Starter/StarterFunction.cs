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
using Models;
using Models.ServiceBus;
using Services.AutoApprover.Contracts;
using System.Text.Json;

namespace Hosts.AutoApprover
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAutoApproverService _autoApproverService;

        public StarterFunction(ILogger<StarterFunction> logger, IConfiguration configuration, IAutoApproverService autoApproverService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _autoApproverService = autoApproverService ?? throw new ArgumentNullException(nameof(autoApproverService));
        }

        [Function(nameof(StarterFunction))]
        public async Task Run(
            [ServiceBusTrigger("%serviceBusAutoApproverQueue%", Connection = "gmmServiceBus")] ServiceBusReceivedMessage message)
        {
            _logger.FunctionStarted(nameof(StarterFunction));

            var messageBody = Encoding.UTF8.GetString(message.Body.ToArray());
            _logger.MessageReceived(message.MessageId, messageBody.Length);

            AutoApprovalQueueMessage autoApprovalMessage;
            try
            {
                autoApprovalMessage = JsonSerializer.Deserialize<AutoApprovalQueueMessage>(messageBody);
                if (autoApprovalMessage == null)
                {
                    _logger.MessageDeserializedToNull();
                    return;
                }
            }
            catch (JsonException ex)
            {
                _logger.MessageDeserializationFailed(ex.Message);
                return;
            }

            if (!CommonServices.GetBoolSettingBase(_configuration, "AutoApprover:IsEnabled", false))
            {
                // When auto-approval is disabled we still consume the message (a Service Bus trigger
                // return completes it), so move the job out of PendingAutoApproval and into PendingReview
                // for human review. Otherwise the job would be stranded in PendingAutoApproval with no
                // pending message to reprocess if the feature is later re-enabled.
                _logger.AutoApproverDisabled();
                await _autoApproverService.MoveJobToPendingReviewAsync(autoApprovalMessage.SyncJobId);
                return;
            }

            await _autoApproverService.ProcessAutoApprovalAsync(autoApprovalMessage);

            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}
