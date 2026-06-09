// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Notifier;
using System;
using System.Threading.Tasks;
using Services.Notifier.Contracts;

namespace Hosts.Notifier
{
    public class SendNotification
    {
        private readonly ILogger<SendNotification> _logger;
        private readonly INotifierService _notifierService;

        public SendNotification(ILogger<SendNotification> logger, INotifierService notifierService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(SendNotification))]
        public async Task SendNotificationAsync([ActivityTrigger] OrchestratorRequest message)
        {
            var messageContent = NotificationMessageContentParser.ParseMessageBody(message.MessageBody);
            var job = NotificationMessageContentParser.GetRequiredValue<SyncJob>(messageContent, "SyncJob");

            using (_logger.BeginSyncJobScope(job))
            {
                _logger.FunctionStarted(nameof(SendNotification));
                await _notifierService.SendEmailAsync(message.MessageType, message.MessageBody, message.MessageTitle, message.SubjectTemplate, message.ContentTemplate);
                _logger.FunctionCompleted(nameof(SendNotification));
            }
        }
    }
}

