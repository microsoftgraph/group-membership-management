// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Services.Notifier.Contracts;
using Models;

namespace Hosts.Notifier
{
    public class CreateActionableNotificationFromContentFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INotifierService _notifierService = null;

        public CreateActionableNotificationFromContentFunction(ILoggingRepository loggingRepository, INotifierService notifierService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [FunctionName(nameof(CreateActionableNotificationFromContentFunction))]
        public async Task<Models.ThresholdNotifications.ThresholdNotification> CreateActionableNotificationFromContentAsync([ActivityTrigger] OrchestratorRequest message)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CreateActionableNotificationFromContentFunction)} function started at: {DateTime.UtcNow}" });
            var notification = await _notifierService.CreateActionableNotificationFromContentAsync(message.MessageBody);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(CreateActionableNotificationFromContentFunction)} function completed at: {DateTime.UtcNow}" });
            return notification;
        }
    }
}