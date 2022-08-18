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

namespace Hosts.JobScheduler
{
    public class HttpStarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public HttpStarterFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(HttpStarterFunction))]
        public async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(HttpStarterFunction)} function started" }, VerbosityLevel.DEBUG);
   
            var instanceId = await starter.StartNewAsync(nameof(OrchestratorFunction), null);
            var response = starter.CreateCheckStatusResponse(req, instanceId);

            var responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());
            var statusQueryGetUri = responseDict.GetValueOrDefault("statusQueryGetUri");
           
            await starter.StartNewAsync(nameof(StatusCallbackOrchestratorFunction), GetCallbackRequest(req, statusQueryGetUri));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(HttpStarterFunction)} function completed" }, VerbosityLevel.DEBUG);

            return response;
        }

        private StatusCallbackOrchestratorRequest GetCallbackRequest(HttpRequestMessage req, string statusUrl) {
            var url = req.Headers.GetValues("PlanUrl").First();
            var projectId = req.Headers.GetValues("ProjectId").First();
            var hubName = req.Headers.GetValues("HubName").First();
            var planId = req.Headers.GetValues("PlanId").First();
            var jobId = req.Headers.GetValues("JobId").First();
            var taskInstanceId = req.Headers.GetValues("TaskinstanceId").First();
            var authToken = req.Headers.GetValues("AuthToken").First();

            var successBody = JsonConvert.SerializeObject(new
            {
                name = "TaskCompleted",
                taskId = taskInstanceId.ToString(),
                jobId = jobId.ToString(),
                result = "Finished"
            });

            var callbackUrl = $"{url}/{projectId}/_apis/distributedtask/hubs/{hubName}/plans/{planId}/events?api-version=2.0-preview.1";

            var callbackRequest = new StatusCallbackOrchestratorRequest
            {
                JobSchedulerStatusUrl = statusUrl,
                AuthToken = authToken,
                CallbackUrl = callbackUrl,
                SuccessBody = successBody
            };

            return callbackRequest;
        }
    }
}
