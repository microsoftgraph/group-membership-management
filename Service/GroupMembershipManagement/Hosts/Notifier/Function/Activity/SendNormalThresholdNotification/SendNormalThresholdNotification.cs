// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Notifier;
using Services.Notifier.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.Notifier
{
    public class SendNormalThresholdNotification
    {
        private readonly ILogger<SendNormalThresholdNotification> _logger;
        private readonly INotifierService _notifierService;

        public SendNormalThresholdNotification(ILogger<SendNormalThresholdNotification> logger, INotifierService notifierService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(SendNormalThresholdNotification))]
        public async Task SendNormalThresholdNotificationAsync([ActivityTrigger] OrchestratorRequest message)
        {
            var messageContent = NotificationMessageContentParser.ParseMessageBody(message.MessageBody);
            var job = NotificationMessageContentParser.GetRequiredValue<SyncJob>(messageContent, "SyncJob");

            using (_logger.BeginSyncJobScope(job))
            {
                _logger.FunctionStarted(nameof(SendNormalThresholdNotification));
                await _notifierService.SendNormalThresholdEmailAsync(message.MessageBody);
                _logger.FunctionCompleted(nameof(SendNormalThresholdNotification));
            }
        }
    }
}
