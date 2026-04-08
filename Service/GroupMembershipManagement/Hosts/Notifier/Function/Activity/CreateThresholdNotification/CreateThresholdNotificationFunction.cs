// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Notifier;
using System;
using System.Threading.Tasks;
using Services.Notifier.Contracts;
using Models;

namespace Hosts.Notifier
{
    public class CreateThresholdNotificationFunction
    {
        private readonly ILogger<CreateThresholdNotificationFunction> _logger;
        private readonly INotifierService _notifierService;

        public CreateThresholdNotificationFunction(ILogger<CreateThresholdNotificationFunction> logger, INotifierService notifierService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(CreateThresholdNotificationFunction))]
        public async Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotificationFromContentAsync([ActivityTrigger] OrchestratorRequest message)
        {
            var messageContent = NotificationMessageContentParser.ParseMessageBody(message.MessageBody);
            var job = NotificationMessageContentParser.GetRequiredValue<SyncJob>(messageContent, "SyncJob");

            using (_logger.BeginSyncJobScope(job))
            {
                _logger.FunctionStarted(nameof(CreateThresholdNotificationFunction));
                var notification = await _notifierService.CreateActionableNotificationFromContentAsync(message.MessageBody);
                _logger.FunctionCompleted(nameof(CreateThresholdNotificationFunction));
                return notification;
            }
        }
    }
}