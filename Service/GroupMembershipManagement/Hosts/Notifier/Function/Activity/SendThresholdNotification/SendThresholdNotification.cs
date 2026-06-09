// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Services.Notifier.Contracts;
using Models.ThresholdNotifications;

namespace Hosts.Notifier
{
    public class SendThresholdNotification
    {
        private readonly ILogger<SendThresholdNotification> _logger;
        private readonly INotifierService _notifierService;

        public SendThresholdNotification(ILogger<SendThresholdNotification> logger, INotifierService notifierService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(SendThresholdNotification))]
        public async Task SendThresholdNotificationAsync([ActivityTrigger] ThresholdNotification notification)
        {
            _logger.FunctionStarted(nameof(SendThresholdNotification));
            await _notifierService.SendThresholdEmailAsync(notification);
            _logger.FunctionCompleted(nameof(SendThresholdNotification));
        }
    }
}
