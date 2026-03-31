// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Hosts.DestinationAttributesUpdater
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(StarterFunction))]
        public async Task Run(
            [TimerTrigger("%destinationAttributesUpdaterSchedule%")] TimerInfo myTimer,
            [DurableClient] DurableTaskClient starter)
        {
            _logger.FunctionStarted(nameof(StarterFunction));
            await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction));
            _logger.FunctionCompleted(nameof(StarterFunction));
        }
    }
}
