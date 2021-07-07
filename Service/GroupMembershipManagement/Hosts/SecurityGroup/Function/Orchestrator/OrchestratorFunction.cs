// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IGraphGroupRepository _graphGroup;
        private readonly SGMembershipCalculator _calculator;
        private const string SyncDisabledNoValidGroupIds = "SyncDisabledNoValidGroupIds";

        public OrchestratorFunction(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository;
            _graphGroup = graphGroupRepository;
            _calculator = calculator;
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
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

                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage {
                        RunId = runId,
                        Message =
                            $"Found {users.Count - distinctUsers.Count} duplicate user(s). Read {distinctUsers.Count} users from source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
                    });
                }
            }
            catch (Exception ex)
            {
                _ = _log.LogMessageAsync(new LogMessage { Message = "Caught unexpected exception, marking sync job as errored. Exception:\n" + ex, RunId = runId });
                distinctUsers = null;

                // make sure this gets thrown to where App Insights will handle it
                throw;
            }
            finally
            {
                if (distinctUsers != null)
                {
                    await context.CallActivityAsync(nameof(UsersSenderFunction), new UsersSenderRequest { SyncJob = syncJob, RunId = runId, Users = distinctUsers });
                }
                else
                {
                    syncJob.Enabled = false;
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                }
            }

            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId });
        }
    }
}