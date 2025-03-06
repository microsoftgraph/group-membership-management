// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services.Notifier.Contracts;
using Models;

namespace Hosts.Notifier
{
    public class RetrieveNotificationsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INotifierService _notifierService = null;

        public RetrieveNotificationsFunction(ILoggingRepository loggingRepository, INotifierService notifierService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [FunctionName(nameof(RetrieveNotificationsFunction))]
        public async Task<List<Models.ThresholdNotifications.ThresholdNotification>> RetrieveNotificationsAsync([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RetrieveNotificationsFunction)} function started at: {DateTime.UtcNow}" });
            var notifications = await _notifierService.RetrieveQueuedNotificationsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RetrieveNotificationsFunction)} function completed at: {DateTime.UtcNow}" });
            return notifications;
        }
    }
}
