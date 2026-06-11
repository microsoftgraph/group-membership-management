// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.Functions.Worker;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Services.Notifier.Contracts;

namespace Hosts.Notifier
{
    public class SendNotification
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INotifierService _notifierService = null;

        public SendNotification(ILoggingRepository loggingRepository, INotifierService notifierService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [Function(nameof(SendNotification))]
        public async Task SendNotificationAsync([ActivityTrigger] OrchestratorRequest message)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { RunId = message.RunId, Message = $"{nameof(SendNotification)} function started at: {DateTime.UtcNow}" });
            await _notifierService.SendEmailAsync(message.MessageType, message.MessageBody, message.MessageTitle, message.SubjectTemplate, message.ContentTemplate);
            await _loggingRepository.LogMessageAsync(new LogMessage { RunId = message.RunId, Message = $"{nameof(SendNotification)} function completed at: {DateTime.UtcNow}" });
        }
    }
}

