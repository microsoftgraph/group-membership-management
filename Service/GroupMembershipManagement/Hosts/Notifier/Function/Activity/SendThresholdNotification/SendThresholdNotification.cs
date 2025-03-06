// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Services.Notifier.Contracts;
using Models.ThresholdNotifications;

namespace Hosts.Notifier
{
    public class SendThresholdNotification
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INotifierService _notifierService = null;

        public SendThresholdNotification(ILoggingRepository loggingRepository, INotifierService notifierService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [FunctionName(nameof(SendThresholdNotification))]
        public async Task SendThresholdNotificationAsync([ActivityTrigger] ThresholdNotification notification)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SendThresholdNotification)} function started at: {DateTime.UtcNow}" });
            await _notifierService.SendThresholdEmailAsync(notification);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SendThresholdNotification)} function completed at: {DateTime.UtcNow}" });
        }
    }
}
