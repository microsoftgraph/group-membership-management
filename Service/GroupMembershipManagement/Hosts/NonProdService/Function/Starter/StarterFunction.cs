// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Models;
using Repositories.Contracts;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public StarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(StarterFunction))]
        public async Task<HttpResponseMessage> HttpStart([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), null);
            var response = starter.CreateCheckStatusResponse(req, instanceId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);

            return response;
        }
    }
}