// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;

namespace Hosts.Notifier
{
    public class PipelineInvocationStarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public PipelineInvocationStarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(PipelineInvocationStarterFunction))]
        public async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PipelineInvocationStarterFunction)} function started" }, VerbosityLevel.DEBUG);

            var requestBody = JsonConvert.DeserializeObject<Dictionary<string, string>>(await req.Content.ReadAsStringAsync());
            var delayForDeploymentInMinutes = int.Parse(requestBody.GetValueOrDefault("DelayForDeploymentInMinutes"));

            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction),
                new OrchestratorRequest
                {
                    StartTimeDelayMinutes = delayForDeploymentInMinutes
                });
            var response = starter.CreateCheckStatusResponse(req, instanceId);

            var responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());
            var statusQueryGetUri = responseDict.GetValueOrDefault("statusQueryGetUri");

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PipelineInvocationStarterFunction)} function completed" }, VerbosityLevel.DEBUG);

            return response;
        }
    }
}
