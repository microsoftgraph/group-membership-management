// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Core;
using Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Graph;
using Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Repositories.Logging;
using Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using static Microsoft.Graph.Constants;

namespace Hosts.AzureMembershipProvider
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IConfiguration _configuration;
        private readonly AzureMembershipProviderService _calculator;

        public OrchestratorFunction(
            ILoggingRepository loggingRepository,
            AzureMembershipProviderService calculator,
            IConfiguration configuration)
        {
            _log = loggingRepository;
            _calculator = calculator;
            _configuration = configuration;
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            var syncJob = mainRequest.SyncJob;
            var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
            List<AzureADUser> distinctUsers = null;

            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started", RunId = syncJob.RunId });

            try
            {
                var queryParts = JArray.Parse(syncJob.Query);
                if (mainRequest.CurrentPart == mainRequest.TotalParts)
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Target Group", RunId = syncJob.RunId });
                    return;
                }
                
                var currentPart = queryParts[mainRequest.CurrentPart - 1];
                var currentType = currentPart.Value<string>("type");

                if (currentType != "AzureMembershipProvider")
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Not AzureMembershipProvider Type", RunId = syncJob.RunId });
                    return;
                }

                var currentQuery = currentPart.Value<string>("source");
                var currentQueryAsString = Convert.ToString(currentQuery);

                if (string.IsNullOrWhiteSpace(currentQueryAsString))
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"No url found in Part# {mainRequest.CurrentPart} {syncJob.Query}. Marking job as errored." });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    return;
                }

                var response = await context.CallSubOrchestratorAsync<(List<AzureADUser> Users, SyncStatus Status)>(nameof(SubOrchestratorFunction), 
                    new SubOrchestratorRequest { SyncJob = syncJob, Url = currentQueryAsString, RunId = runId });

                if (response.Status != SyncStatus.InProgress)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    return;
                }

                var users = response.Users;
                distinctUsers = users.GroupBy(user => user.ObjectId).Select(userGrp => userGrp.First()).ToList();

                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Found {users.Count - distinctUsers.Count} duplicate user(s). " +
                                $"Read {distinctUsers.Count} users from source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
                });

                var filePath = await context.CallActivityAsync<string>(
                                    nameof(UsersSenderFunction),
                                    new UsersSenderRequest
                                    {
                                        SyncJob = syncJob,
                                        RunId = runId,
                                        Users = distinctUsers,
                                        CurrentPart = mainRequest.CurrentPart,
                                        Exclusionary = mainRequest.Exclusionary
                                    });                

                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = "Calling MembershipAggregator" });
                    var content = new MembershipAggregatorHttpRequest
                    {
                        FilePath = filePath,
                        PartNumber = mainRequest.CurrentPart,
                        PartsCount = mainRequest.TotalParts,
                        SyncJob = mainRequest.SyncJob
                    };

                    var request = new DurableHttpRequest(HttpMethod.Post,
                                        new Uri(_configuration["membershipAggregatorUrl"]),
                                        content: JsonConvert.SerializeObject(content),
                                        headers: new Dictionary<string, StringValues> { { "x-functions-key", _configuration["membershipAggregatorFunctionKey"] } },
                                        httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(30), 3));

                    var httpResponse = await context.CallHttpAsync(request);
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"MembershipAggregator response Code:{httpResponse.StatusCode}, Content: {httpResponse.Content}" });

                    if (httpResponse.StatusCode != HttpStatusCode.NoContent)
                    {
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    }
                }
                else
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Membership file path is not valid, marking sync job as {SyncStatus.FilePathNotValid}." });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.FilePathNotValid });
                }

            }
            catch (Exception ex)
            {
                if (ex.Message != null && ex.Message.Contains("The request timed out"))
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    _ = _log.LogMessageAsync(new LogMessage { Message = $"Rescheduling job at {syncJob.StartDate} due to Graph API timeout.", RunId = runId });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                    return;
                }

                _ = _log.LogMessageAsync(new LogMessage { Message = $"Caught unexpected exception in Part# {mainRequest.CurrentPart}, marking sync job as errored. Exception:\n{ex}", RunId = runId });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });

                // make sure this gets thrown to where App Insights will handle it
                throw;
            }
            finally
            {
                if (syncJob != null && syncJob.RunId.HasValue)                    
                    _log.RemoveSyncJobProperties(runId);
            }

            _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId });
        }
    }
}