// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class StarterFunction
    {
        private readonly ILogger<StarterFunction> _logger;

        public StarterFunction(ILogger<StarterFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(StarterFunction))]
        public async Task<HttpResponseData> HttpStart([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient starter)
        {
            _logger.FunctionStarted(nameof(StarterFunction));

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), null);
            var response = await starter.CreateCheckStatusResponseAsync(req, instanceId);

            _logger.FunctionCompleted(nameof(StarterFunction));

            return response;
        }
    }
}