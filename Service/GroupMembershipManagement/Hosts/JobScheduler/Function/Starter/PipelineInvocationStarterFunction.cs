// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.IO;
using System.Linq;
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
            
            // Parse PrioritizeThresholdJobs - defaults to false
            // ADO pipeline deployments should explicitly set this to true to prioritize jobs with thresholds
            var prioritizeThresholdJobs = false;
            if (requestBody.TryGetProperty("PrioritizeThresholdJobs", out var prioritizeProperty))
            {
                prioritizeThresholdJobs = prioritizeProperty.GetBoolean();
            }

            var instanceId = await starter.ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction),
                new OrchestratorRequest
                {
                    StartTimeDelayMinutes = delayForDeploymentInMinutes,
                    PrioritizeThresholdJobs = prioritizeThresholdJobs
                });

            var response = starter.CreateCheckStatusResponse(req, instanceId);
            using var bodyStream = response.Body;
            bodyStream.Seek(0, SeekOrigin.Begin);
            var json = await JsonSerializer.DeserializeAsync<JsonElement>(bodyStream);
            var statusQueryGetUri = json.TryGetProperty("StatusQueryGetUri", out var uriElement) ? uriElement.GetString() : null;

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
