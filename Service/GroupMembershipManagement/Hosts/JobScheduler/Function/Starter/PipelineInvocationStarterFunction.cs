// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

        [Function(nameof(PipelineInvocationStarterFunction))]
        public async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PipelineInvocationStarterFunction)} function started" }, VerbosityLevel.DEBUG);

            var requestBody = JsonConvert.DeserializeObject<Dictionary<string, string>>(await req.ReadAsStringAsync());
            var delayForDeploymentInMinutes = int.Parse(requestBody.GetValueOrDefault("DelayForDeploymentInMinutes"));

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction),
                new OrchestratorRequest
                {
                    StartTimeDelayMinutes = delayForDeploymentInMinutes
                });

            var response = starter.CreateCheckStatusResponse(req, instanceId);
            response.Body.Position = 0;

            var responseBody = string.Empty;
            using (var reader = new StreamReader(response.Body))
            {
                responseBody = await reader.ReadToEndAsync();
            }

            var responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseBody);
            var statusQueryGetUri = responseDict.GetValueOrDefault("statusQueryGetUri");

            if (req.Headers.Contains("PlanUrl"))
                await starter.ScheduleNewOrchestrationInstanceAsync(nameof(StatusCallbackOrchestratorFunction), GetCallbackRequest(req, statusQueryGetUri));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PipelineInvocationStarterFunction)} function completed" }, VerbosityLevel.DEBUG);

            return response;
        }

        private StatusCallbackOrchestratorRequest GetCallbackRequest(HttpRequestData req, string statusUrl)
        {
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
