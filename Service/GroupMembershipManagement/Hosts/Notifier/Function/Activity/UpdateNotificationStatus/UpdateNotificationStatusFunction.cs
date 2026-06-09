// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Services.Notifier.Contracts;
using Models;

namespace Hosts.Notifier
{
    public class UpdateNotificationStatusFunction
    {
        private readonly ILogger<UpdateNotificationStatusFunction> _logger;
        private readonly INotifierService _notifierService;

        public UpdateNotificationStatusFunction(ILogger<UpdateNotificationStatusFunction> logger, INotifierService notifierService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(UpdateNotificationStatusFunction))]
        public async Task UpdateNotificationStatusAsync([ActivityTrigger] UpdateNotificationStatusRequest request)
        {
            _logger.FunctionStarted(nameof(UpdateNotificationStatusFunction));
            await _notifierService.UpdateNotificationStatusAsync(request.Notification, request.Status);
            _logger.FunctionCompleted(nameof(UpdateNotificationStatusFunction));
        }
    }
}
