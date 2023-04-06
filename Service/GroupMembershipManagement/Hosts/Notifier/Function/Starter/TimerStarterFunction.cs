// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;

namespace Hosts.Notifier
{
    public class TimerStarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IThresholdNotificationConfig _thresholdNotificationConfig;

        public TimerStarterFunction(ILoggingRepository loggingRepository, IThresholdNotificationConfig thresholdNotificationConfig)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _thresholdNotificationConfig = thresholdNotificationConfig ?? throw new ArgumentNullException(nameof(thresholdNotificationConfig));
        }

        [FunctionName(nameof(TimerStarterFunction))]
        public async Task Run(
            [TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            if (_thresholdNotificationConfig.IsThresholdNotificationEnabled)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TimerStarterFunction)} function started" }, VerbosityLevel.DEBUG);
                var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction));
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TimerStarterFunction)} function completed" }, VerbosityLevel.DEBUG);
            }
        }
    }
}
