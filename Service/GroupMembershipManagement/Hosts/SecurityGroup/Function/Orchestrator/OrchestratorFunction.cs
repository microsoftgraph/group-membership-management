// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IGraphGroupRepository _graphGroup;
        private readonly IConfiguration _configuration;
        private readonly SGMembershipCalculator _calculator;
        private const string SyncDisabledNoValidGroupIds = "SyncDisabledNoValidGroupIds";

        public OrchestratorFunction(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository,
            SGMembershipCalculator calculator,
            IConfiguration configuration)
        {
            _log = loggingRepository;
            _graphGroup = graphGroupRepository;
            _calculator = calculator;
            _configuration = configuration;
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var syncJob = context.GetInput<SyncJob>();
            _log.SyncJobProperties = syncJob.ToDictionary();
            var runId = syncJob.RunId.GetValueOrDefault(context.NewGuid());
            _graphGroup.RunId = runId;
            List<AzureADUser> distinctUsers = null;

            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started", RunId = runId });
            var sourceGroups = await context.CallActivityAsync<AzureADGroup[]>(nameof(SourceGroupsReaderFunction), new SourceGroupsReaderRequest { SyncJob = syncJob, RunId = runId });
            try
            {
                if (sourceGroups.Length == 0)
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"None of the source groups in {syncJob.Query} were valid guids. Marking job as errored." });
                    await context.CallActivityAsync(nameof(EmailSenderFunction), new EmailSenderRequest { SyncJob = syncJob, RunId = runId });
                }
                else
                {
                    // Run multiple source group processing flows in parallel
                    var processingTasks = new List<Task<List<AzureADUser>>>();
                    foreach (var sourceGroup in sourceGroups)
                    {
                        var processTask = context.CallSubOrchestratorAsync<List<AzureADUser>>(nameof(SubOrchestratorFunction), new SecurityGroupRequest { SyncJob = syncJob, SourceGroup = sourceGroup, RunId = runId });
                        processingTasks.Add(processTask);
                    }
                    var tasks = await Task.WhenAll(processingTasks);

                    var users = new List<AzureADUser>();
                    foreach (var task in tasks)
                        users.AddRange(task);

                    distinctUsers = users.GroupBy(user => user.ObjectId).Select(userGrp => userGrp.First()).ToList();

                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage
                    {
                        RunId = runId,
                        Message =
                            $"Found {users.Count - distinctUsers.Count} duplicate user(s). Read {distinctUsers.Count} users from source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
                    });

                    var filePath = await context.CallActivityAsync<string>(nameof(UsersSenderFunction), new UsersSenderRequest { SyncJob = syncJob, RunId = runId, Users = distinctUsers });

                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        _ = _log.LogMessageAsync(new LogMessage { Message = "Calling GraphUpdater", RunId = runId });
                        var content = new GraphUpdaterHttpRequest { FilePath = filePath, RunId = runId, JobPartitionKey = syncJob.PartitionKey, JobRowKey = syncJob.RowKey };
                        var request = new DurableHttpRequest(HttpMethod.Post, new Uri(_configuration["graphUpdaterUrl"]), content: JsonConvert.SerializeObject(content));
                        request.Headers.Add("x-functions-key", _configuration["graphUpdaterFunctionKey"]);
                        var response = await context.CallHttpAsync(request);
                        _ = _log.LogMessageAsync(new LogMessage { Message = $"GraphUpdater response Code: {response.StatusCode}, Content: {response.Content}", RunId = runId });

                        if (response.StatusCode != HttpStatusCode.NoContent)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                        }
                    }
                    else
                    {
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                        _ = _log.LogMessageAsync(new LogMessage { Message = "Membership file path is not valid, marking sync job as errored.", RunId = runId });
                    }
                }
            }
            catch (Exception ex)
            {
                _ = _log.LogMessageAsync(new LogMessage { Message = "Caught unexpected exception, marking sync job as errored. Exception:\n" + ex, RunId = runId });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });

                // make sure this gets thrown to where App Insights will handle it
                throw;
            }

            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId });
        }
    }
}