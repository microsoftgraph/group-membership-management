// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Models;

namespace Hosts.Notifier
{
    public class UpdateNotificationStatusFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INotifierService _notifierService = null;

        public UpdateNotificationStatusFunction(ILoggingRepository loggingRepository, INotifierService notifierService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [FunctionName(nameof(UpdateNotificationStatusFunction))]
        public async Task UpdateNotificationStatusAsync([ActivityTrigger] UpdateNotificationStatusRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UpdateNotificationStatusFunction)} function started at: {DateTime.UtcNow}" });
            await _notifierService.UpdateNotificationStatus(request.Notification, request.Status);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UpdateNotificationStatusFunction)} function completed at: {DateTime.UtcNow}" });
        }
    }
}
