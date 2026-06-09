// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services.Notifier.Contracts;
using Models;

namespace Hosts.Notifier
{
    public class RetrieveNotificationsFunction
    {
        private readonly ILogger<RetrieveNotificationsFunction> _logger;
        private readonly INotifierService _notifierService;

        public RetrieveNotificationsFunction(ILogger<RetrieveNotificationsFunction> logger, INotifierService notifierService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(RetrieveNotificationsFunction))]
        public async Task<List<Models.ThresholdNotifications.ThresholdNotification>> RetrieveNotificationsAsync([ActivityTrigger] object obj)
        {
            _logger.FunctionStarted(nameof(RetrieveNotificationsFunction));
            var notifications = await _notifierService.RetrieveQueuedNotificationsAsync();
            _logger.FunctionCompleted(nameof(RetrieveNotificationsFunction));
            return notifications;
        }
    }
}
