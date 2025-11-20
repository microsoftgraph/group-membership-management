// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
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

            var requestContent = await new StreamReader(req.Body).ReadToEndAsync();
            var requestBody = JsonSerializer.Deserialize<JsonElement>(requestContent);
            var delayForDeploymentInMinutes = requestBody.GetProperty("DelayForDeploymentInMinutes").GetInt32();

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction),
                new OrchestratorRequest
                {
                    StartTimeDelayMinutes = delayForDeploymentInMinutes
                });
            
            var statusQueryGetUri = $"{req.Url.Scheme}://{req.Url.Authority}/runtime/webhooks/durabletask/instances/{instanceId}";

            if (req.Headers.Contains("PlanUrl"))
                await starter.ScheduleNewOrchestrationInstanceAsync(nameof(StatusCallbackOrchestratorFunction), GetCallbackRequest(req, statusQueryGetUri));

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PipelineInvocationStarterFunction)} function completed" }, VerbosityLevel.DEBUG);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new { instanceId, statusQueryGetUri });
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

            var successBody = JsonSerializer.Serialize(new
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
