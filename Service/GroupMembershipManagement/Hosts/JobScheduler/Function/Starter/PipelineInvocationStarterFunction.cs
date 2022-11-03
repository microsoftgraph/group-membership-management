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

namespace Hosts.JobScheduler
{
    public class PipelineInvocationStarterFunction
    {
        private IJobSchedulerConfig _jobSchedulerConfig;
        private readonly ILoggingRepository _loggingRepository = null;

        public PipelineInvocationStarterFunction(IJobSchedulerConfig jobSchedulerConfig, ILoggingRepository loggingRepository)
        {
            _jobSchedulerConfig = jobSchedulerConfig;
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

            await starter.StartNewAsync(nameof(StatusCallbackOrchestratorFunction), GetCallbackRequest(req, statusQueryGetUri));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PipelineInvocationStarterFunction)} function completed" }, VerbosityLevel.DEBUG);

            return response;
        }

        private StatusCallbackOrchestratorRequest GetCallbackRequest(HttpRequestMessage req, string statusUrl) {
            var url = req.Headers.GetValues("PlanUrl").FirstOrDefault("NULL");
            var projectId = req.Headers.GetValues("ProjectId").FirstOrDefault("NULL");
            var hubName = req.Headers.GetValues("HubName").FirstOrDefault("NULL");
            var planId = req.Headers.GetValues("PlanId").FirstOrDefault("NULL");
            var jobId = req.Headers.GetValues("JobId").FirstOrDefault("NULL");
            var taskInstanceId = req.Headers.GetValues("TaskinstanceId").FirstOrDefault("NULL");
            var authToken = req.Headers.GetValues("AuthToken").FirstOrDefault("NULL");

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
