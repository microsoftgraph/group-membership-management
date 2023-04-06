// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Services.Contracts;

namespace Hosts.Notifier
{
    public class SendNotificationFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INotifierService _notifierService = null;

        public SendNotificationFunction(ILoggingRepository loggingRepository, INotifierService notifierService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _notifierService = notifierService ?? throw new ArgumentNullException(nameof(notifierService));
        }

        [FunctionName(nameof(SendNotificationFunction))]
        public async Task SendNotificationAsync([ActivityTrigger] Guid targetOfficeGroupId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SendNotificationFunction)} function started at: {DateTime.UtcNow}" });
            await _notifierService.SendEmailAsync(targetOfficeGroupId);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SendNotificationFunction)} function completed at: {DateTime.UtcNow}" });
        }
    }
}
