// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Repositories.Contracts;

namespace SqlDataChecker
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(StarterFunction))]
        public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req, [DurableClient] DurableTaskClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), null);
            var response = await starter.CreateCheckStatusResponseAsync(req, instanceId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);

            return response;
        }
    }
}
