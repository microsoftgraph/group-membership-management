// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class OrchestratorFunction
    {
        private const string SyncCompletedEmailBody = "SyncCompletedEmailBody";
        private readonly TelemetryClient _telemetryClient;
        private readonly IGraphUpdaterService _graphUpdaterService = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly IThresholdConfig _thresholdConfig = null;
        private readonly bool _isDryRunEnabled;
        enum Metric
        {
            SyncComplete,
            SyncJobTimeElapsedSeconds,
            ProjectedMemberCount
        }

        public OrchestratorFunction(
            ILoggingRepository loggingRepository,
            TelemetryClient telemetryClient,
            IGraphUpdaterService graphUpdaterService,
            IDryRunValue dryRun,
            IEmailSenderRecipient emailSenderAndRecipients,
            IThresholdConfig thresholdConfig)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _isDryRunEnabled = loggingRepository.DryRun = dryRun != null ? dryRun.DryRunEnabled : throw new ArgumentNullException(nameof(dryRun));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _thresholdConfig = thresholdConfig ?? throw new ArgumentNullException(nameof(thresholdConfig));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task<GroupMembershipMessageResponse> RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            GroupMembership groupMembership = null;
            GraphUpdaterFunctionRequest graphRequest = null;
            GroupMembershipMessageResponse messageResponse = null;
            SyncJob syncJob = null;

            graphRequest = context.GetInput<GraphUpdaterFunctionRequest>();
            groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphRequest.Message);
            graphRequest.RunId = groupMembership.RunId;

            try
            {
                syncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                                       new JobReaderRequest
                                                       {
                                                           JobPartitionKey = groupMembership.SyncJobPartitionKey,
                                                           JobRowKey = groupMembership.SyncJobRowKey,
                                                           RunId = groupMembership.RunId
                                                       });

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", SyncJob = syncJob });

                messageResponse = await context.CallActivityAsync<GroupMembershipMessageResponse>(nameof(MessageCollectorFunction), graphRequest);

                if (graphRequest.IsCancelationRequest)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Canceling session {graphRequest.MessageSessionId}", SyncJob = syncJob });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                        SyncStatus.Error, groupMembership.MembershipObtainerDryRunEnabled, syncJob.ThresholdViolations, groupMembership.RunId));

                    return messageResponse;
                }

                if (messageResponse.ShouldCompleteMessage)
                {
                    var isValidGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction),
                                               new GroupValidatorRequest
                                               {
                                                   RunId = groupMembership.RunId,
                                                   GroupId = groupMembership.Destination.ObjectId,
                                                   JobPartitionKey = groupMembership.SyncJobPartitionKey,
                                                   JobRowKey = groupMembership.SyncJobRowKey
                                               });

                    if (!isValidGroup)
                    {
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                        SyncStatus.Error, groupMembership.MembershipObtainerDryRunEnabled, syncJob.ThresholdViolations, groupMembership.RunId));

                        await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function did not complete", SyncJob = syncJob });

                        return messageResponse;
                    }

                    var destinationGroupMembers = await context.CallSubOrchestratorAsync<List<AzureADUser>>(nameof(UsersReaderSubOrchestratorFunction),
                                                                                                            new UsersReaderRequest { SyncJob = syncJob });

                    var fullMembership = new GroupMembership
                    {
                        Destination = groupMembership.Destination,
                        IsLastMessage = groupMembership.IsLastMessage,
                        RunId = groupMembership.RunId,
                        SourceMembers = messageResponse.CompletedGroupMembershipMessages.SelectMany(x => x.Body.SourceMembers).ToList(),
                        SyncJobPartitionKey = groupMembership.SyncJobPartitionKey,
                        SyncJobRowKey = groupMembership.SyncJobRowKey
                    };

                    var deltaResponse = await context.CallActivityAsync<DeltaResponse>(nameof(DeltaCalculatorFunction),
                                                    new DeltaCalculatorRequest
                                                    {
                                                        RunId = groupMembership.RunId,
                                                        GroupMembership = fullMembership,
                                                        MembersFromDestinationGroup = destinationGroupMembers,
                                                    });

                    if (deltaResponse.GraphUpdaterStatus == GraphUpdaterStatus.Error ||
                        deltaResponse.GraphUpdaterStatus == GraphUpdaterStatus.ThresholdExceeded)
                    {
                        var updateRequest = CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                        deltaResponse.SyncStatus, groupMembership.MembershipObtainerDryRunEnabled, syncJob.ThresholdViolations, groupMembership.RunId);

                        if (deltaResponse.GraphUpdaterStatus == GraphUpdaterStatus.ThresholdExceeded)
                        {
                            updateRequest.ThresholdViolations++;

                            if (updateRequest.ThresholdViolations >= _thresholdConfig.NumberOfThresholdViolationsToDisableJob)
                                updateRequest.Status = SyncStatus.Disabled;
                        }

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), updateRequest);
                    }

                    if (deltaResponse.GraphUpdaterStatus != GraphUpdaterStatus.Ok)
                    {
                        await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function did not complete", SyncJob = syncJob });

                        return messageResponse;
                    }

                    if (!deltaResponse.IsDryRunSync)
                    {
                        await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(nameof(GroupUpdaterSubOrchestratorFunction),
                                        CreateGroupUpdaterRequest(syncJob, deltaResponse.MembersToAdd, RequestType.Add, deltaResponse.IsInitialSync));

                        await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(nameof(GroupUpdaterSubOrchestratorFunction),
                                        CreateGroupUpdaterRequest(syncJob, deltaResponse.MembersToRemove, RequestType.Remove, deltaResponse.IsInitialSync));

                        if (deltaResponse.IsInitialSync)
                        {
                            var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                            new GroupNameReaderRequest { RunId = groupMembership.RunId, GroupId = groupMembership.Destination.ObjectId });
                            var additonalContent = new[] { groupName, groupMembership.Destination.ObjectId.ToString(), deltaResponse.MembersToAdd.Count.ToString(), deltaResponse.MembersToRemove.Count.ToString() };

                            await context.CallActivityAsync(nameof(EmailSenderFunction),
                                            new EmailSenderRequest
                                            {
                                                ToEmail = deltaResponse.Requestor,
                                                CcEmail = _emailSenderAndRecipients.SyncCompletedCCAddresses,
                                                ContentTemplate = SyncCompletedEmailBody,
                                                AdditionalContentParams = additonalContent,
                                                RunId = groupMembership.RunId
                                            });
                        }
                    }

                    var message = GetUsersDataMessage(groupMembership.Destination.ObjectId, deltaResponse.MembersToAdd.Count, deltaResponse.MembersToRemove.Count);
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = message });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                        SyncStatus.Idle, groupMembership.MembershipObtainerDryRunEnabled, 0, groupMembership.RunId));

                    var timeElapsedForJob = (context.CurrentUtcDateTime - deltaResponse.Timestamp).TotalSeconds;
                    _telemetryClient.TrackMetric(nameof(Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob);

                    var syncCompleteEvent = new Dictionary<string, string>
                    {
                        { nameof(SyncJob.TargetOfficeGroupId), groupMembership.Destination.ObjectId.ToString() },
                        { nameof(SyncJob.Type), deltaResponse.SyncJobType },
                        { "Result", deltaResponse.SyncStatus == SyncStatus.Idle ? "Success": "Failure" },
                        { nameof(SyncJob.IsDryRunEnabled), deltaResponse.IsDryRunSync.ToString() },
                        { nameof(Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob.ToString() },
                        { nameof(DeltaResponse.MembersToAdd), deltaResponse.MembersToAdd.Count.ToString() },
                        { nameof(DeltaResponse.MembersToRemove), deltaResponse.MembersToRemove.Count.ToString() },
                        { nameof(Metric.ProjectedMemberCount), fullMembership.SourceMembers.Count.ToString() }
                    };

                    _telemetryClient.TrackEvent(nameof(Metric.SyncComplete), syncCompleteEvent);
                }

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", SyncJob = syncJob });

                return messageResponse;
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Caught unexpected exception, marking sync job as errored. Exception:\n{ex}", SyncJob = syncJob });

                if (groupMembership != null && !string.IsNullOrWhiteSpace(groupMembership.SyncJobPartitionKey) && !string.IsNullOrWhiteSpace(groupMembership.SyncJobRowKey))
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                    SyncStatus.Error, groupMembership.MembershipObtainerDryRunEnabled, syncJob.ThresholdViolations, groupMembership.RunId));
                }

                throw;
            }
        }

        private JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(string partitionKey, string rowKey, SyncStatus syncStatus, bool isDryRun, int thresholdViolations, Guid runId)
        {
            return new JobStatusUpdaterRequest
            {
                RunId = runId,
                JobPartitionKey = partitionKey,
                JobRowKey = rowKey,
                Status = syncStatus,
                IsDryRun = isDryRun,
                ThresholdViolations = thresholdViolations
            };
        }

        private GroupUpdaterRequest CreateGroupUpdaterRequest(SyncJob syncJob, ICollection<AzureADUser> members, RequestType type, bool isInitialSync)
        {
            return new GroupUpdaterRequest
            {
                SyncJob = syncJob,
                Members = members,
                Type = type,
                IsInitialSync = isInitialSync
            };
        }

        private string GetUsersDataMessage(Guid targetGroupId, int membersToAdd, int membersToRemove)
        {
            string message;
            if (_isDryRunEnabled)
                message = $"A Dry Run Synchronization for {targetGroupId} is now complete. " +
                          $"{membersToAdd} users would have been added. " +
                          $"{membersToRemove} users would have been removed.";
            else
                message = $"Synchronization for {targetGroupId} is now complete. " +
                          $"{membersToAdd} users have been added. " +
                          $"{membersToRemove} users have been removed.";

            return message;
        }
    }
}
