// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Models;
using Repositories.Contracts;
using Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IConfiguration _configuration;
        private readonly PlaceMembershipObtainerService _calculator;

        public OrchestratorFunction(
            ILoggingRepository loggingRepository,
            PlaceMembershipObtainerService calculator,
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
                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), syncJob);
                if (groupId.Equals(Guid.Empty))
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Unable to get group id for job:{syncJob.Id}", RunId = syncJob.RunId });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });
                    return;
                }
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Group Id for job:{syncJob.Id} is {groupId}", RunId = syncJob.RunId });
                var queryParts = JsonNode.Parse(syncJob.Query).AsArray();
                if (mainRequest.CurrentPart == mainRequest.TotalParts)
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Target Group", RunId = syncJob.RunId });
                    return;
                }
                
                var currentPart = queryParts[mainRequest.CurrentPart - 1];
                var currentType = currentPart["type"].GetValue<string>();

                if (currentType != "PlaceMembership")
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Not PlaceMembership Type", RunId = syncJob.RunId });
                    return;
                }

                var currentQuery = currentPart["source"].GetValue<string>();
                var currentQueryAsString = Convert.ToString(currentQuery);

                if (string.IsNullOrWhiteSpace(currentQueryAsString))
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"No url found in Part# {mainRequest.CurrentPart} {syncJob.Query}. Marking job as errored." });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    return;
                }

                else
                {
                    try
                    {
                        var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { Query = currentPart.ToString(), RunId = syncJob.RunId });
                        if (!hasValidJson)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob });
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Source query is not valid for job:{syncJob.Id}" });
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                        return;
                    }
                }

                var response = await context.CallSubOrchestratorAsync<SubOrchestratorResponse>(nameof(SubOrchestratorFunction), 
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
                                $"Read {distinctUsers.Count} users from source groups {syncJob.Query} to be synced into the destination group {groupId}."
                });

                var filePath = await context.CallActivityAsync<string>(
                                    nameof(UsersSenderFunction),
                                    new UsersSenderRequest
                                    {
                                        SyncJob = syncJob,
                                        RunId = runId,
                                        GroupId = groupId,
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
                        SyncJob = mainRequest.SyncJob,
                        IsDestinationPart = false
                    };

                    await context.CallActivityAsync(nameof(QueueMessageSenderFunction), content);
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