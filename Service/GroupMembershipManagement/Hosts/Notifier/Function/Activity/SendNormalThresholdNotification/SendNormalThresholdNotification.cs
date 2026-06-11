// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Notifier.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.Notifier
{
    public class SendNormalThresholdNotification
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INotifierService _notifierService = null;

        public SendNormalThresholdNotification(ILoggingRepository loggingRepository, INotifierService notifierService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(SendNormalThresholdNotification))]
        public async Task SendNormalThresholdNotificationAsync([ActivityTrigger] OrchestratorRequest message)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { RunId = message.RunId, Message = $"{nameof(SendNormalThresholdNotification)} function started at: {DateTime.UtcNow}" });
            await _notifierService.SendNormalThresholdEmailAsync(message.MessageBody);
            await _loggingRepository.LogMessageAsync(new LogMessage { RunId = message.RunId, Message = $"{nameof(SendNotification)} function completed at: {DateTime.UtcNow}" });
        }
    }
}
