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
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient;
        private readonly IGraphUpdaterService _graphUpdaterService = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
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
            IEmailSenderRecipient emailSenderAndRecipients)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _isDryRunEnabled = _loggingRepository.DryRun = dryRun != null ? dryRun.DryRunEnabled : throw new ArgumentNullException(nameof(dryRun));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients)); ;
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task<GroupMembershipMessageResponse> RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            GroupMembership groupMembership = null;
            GraphUpdaterFunctionRequest graphRequest = null;
            GroupMembershipMessageResponse messageResponse = null;

            try
            {
                // Not allowed to await things that aren't another azure function
                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(OrchestratorFunction) + " function started" })
                                            .ConfigureAwait(false);

                graphRequest = context.GetInput<GraphUpdaterFunctionRequest>();
                groupMembership = JsonConvert.DeserializeObject<GroupMembership>(graphRequest.Message);
                graphRequest.RunId = groupMembership.RunId;

                messageResponse = await context.CallActivityAsync<GroupMembershipMessageResponse>(nameof(MessageCollectorFunction), graphRequest);

                if (graphRequest.IsCancelationRequest)
                {
                    _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Canceling session {graphRequest.MessageSessionId}" })
                                       .ConfigureAwait(false);

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                        SyncStatus.Error, groupMembership.MembershipObtainerDryRunEnabled, groupMembership.RunId));

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
                                                                        SyncStatus.Error, groupMembership.MembershipObtainerDryRunEnabled, groupMembership.RunId));

                        _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(OrchestratorFunction) + " function did not complete" })
                                        .ConfigureAwait(false);

                        return messageResponse;
                    }

                    var destinationGroupMembers = await context.CallSubOrchestratorAsync<List<AzureADUser>>(nameof(UsersReaderSubOrchestratorFunction),
                                                    new UsersReaderRequest
                                                    {
                                                        RunId = groupMembership.RunId,
                                                        GroupId = groupMembership.Destination.ObjectId
                                                    });

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
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                        deltaResponse.SyncStatus, groupMembership.MembershipObtainerDryRunEnabled, groupMembership.RunId));
                    }

                    if (deltaResponse.GraphUpdaterStatus != GraphUpdaterStatus.Ok)
                    {
                        _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(OrchestratorFunction) + " function did not complete" })
                                        .ConfigureAwait(false);

                        return messageResponse;
                    }

                    if (!deltaResponse.IsDryRunSync)
                    {
                        await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(nameof(GroupUpdaterSubOrchestratorFunction),
                                        CreateGroupUpdaterRequest(groupMembership.Destination.ObjectId, deltaResponse.MembersToAdd, RequestType.Add, groupMembership.RunId, deltaResponse.IsInitialSync));

                        await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(nameof(GroupUpdaterSubOrchestratorFunction),
                                        CreateGroupUpdaterRequest(groupMembership.Destination.ObjectId, deltaResponse.MembersToRemove, RequestType.Remove, groupMembership.RunId, deltaResponse.IsInitialSync));

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

                    if (!context.IsReplaying)
                    {
                        LogSyncUsersData(groupMembership.Destination.ObjectId, deltaResponse.MembersToAdd.Count, deltaResponse.MembersToRemove.Count, groupMembership.RunId);
                    }

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                        SyncStatus.Idle, groupMembership.MembershipObtainerDryRunEnabled, groupMembership.RunId));


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

                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(OrchestratorFunction) + " function completed" })
                                        .ConfigureAwait(false);

                return messageResponse;
            }
            catch (Exception ex)
            {
                _ = _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = "Caught unexpected exception, marking sync job as errored. Exception:\n" + ex,
                    RunId = groupMembership?.RunId
                });

                if (groupMembership != null && !string.IsNullOrWhiteSpace(groupMembership.SyncJobPartitionKey) && !string.IsNullOrWhiteSpace(groupMembership.SyncJobRowKey))
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(groupMembership.SyncJobPartitionKey, groupMembership.SyncJobRowKey,
                                                                    SyncStatus.Error, groupMembership.MembershipObtainerDryRunEnabled, groupMembership.RunId));
                }

                throw;
            }
        }

        private JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(string partitionKey, string rowKey, SyncStatus syncStatus, bool isDryRun, Guid runId)
        {
            return new JobStatusUpdaterRequest
            {
                RunId = runId,
                JobPartitionKey = partitionKey,
                JobRowKey = rowKey,
                Status = syncStatus,
                IsDryRun = isDryRun
            };
        }

        private GroupUpdaterRequest CreateGroupUpdaterRequest(Guid targetGroupId, ICollection<AzureADUser> members, RequestType type, Guid runId, bool isInitialSync)
        {
            return new GroupUpdaterRequest
            {
                RunId = runId,
                DestinationGroupId = targetGroupId,
                Members = members,
                Type = type,
                IsInitialSync = isInitialSync
            };
        }

        private void LogSyncUsersData(Guid targetGroupId, int membersToAdd, int membersToRemove, Guid runId)
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

            _ = _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = message,
                RunId = runId
            }).ConfigureAwait(false); ;
        }
    }
}
