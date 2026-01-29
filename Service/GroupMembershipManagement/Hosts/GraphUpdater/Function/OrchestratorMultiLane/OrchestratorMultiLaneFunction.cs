// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using GraphUpdater.Activity.JobTracker;
using GraphUpdater.Entities;
using GraphUpdater.Helpers;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Identity.Client;
using Models;
using Models.Notifications;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class OrchestratorMultiLaneFunction
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly IGraphUpdaterService _graphUpdaterService = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients = null;
        private readonly IGMMResources _gmmResources = null;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDeltaCachingConfig _deltaCachingConfig = null;
        private readonly RunLimiterSettings _runLimiterSettings;
        enum Metric
        {
            SyncComplete,
            SyncJobTimeElapsedSeconds
        }

        public OrchestratorMultiLaneFunction(
            TelemetryClient telemetryClient,
            IGraphUpdaterService graphUpdaterService,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources,
            ILoggingRepository loggingRepository,
            IDeltaCachingConfig deltaCachingConfig,
            RunLimiterSettings runLimiterSettings)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _deltaCachingConfig = deltaCachingConfig ?? throw new ArgumentNullException(nameof(deltaCachingConfig));
            _runLimiterSettings = runLimiterSettings ?? throw new ArgumentNullException(nameof(runLimiterSettings));
        }

        [Function(nameof(OrchestratorMultiLaneFunction))]
        public async Task<OrchestrationRuntimeStatus> RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            SyncJob syncJob = null;
            var sourceUsersNotFound = new List<AzureADUser>();
            var destinationUsersNotFound = new List<AzureADUser>();
            var syncCompleteEvent = new SyncCompleteCustomEvent();

            bool shouldEmitCompletion = false;
            EntityInstanceId? jobTrackerEntityId = null;

            var request = context.GetInput<OrchestratorMultiLaneRequest>();
            var groupMembership = request.GroupMembership;

            var laneSize = request.LaneSize;

            var runId = groupMembership.SyncJob.RunId.GetValueOrDefault(Guid.Empty);
            var dynamicProperties = groupMembership.SyncJob.ToDictionary();
            dynamicProperties.Add("Instance", request.SubscriptionName);
            dynamicProperties.Add("MessageIndex", groupMembership.MessageIndex.ToString());
            _loggingRepository.UpsertSyncJobProperties(groupMembership.RunId, dynamicProperties);

            using var heartbeatCts = new CancellationTokenSource();
            Task heartbeatTask = Task.CompletedTask;
            var isLargeLane = string.Equals(laneSize, "Large", StringComparison.OrdinalIgnoreCase);
            var isRunLimiterEnabled = _runLimiterSettings.IsEnabled;
            var shouldRunLargeLaneHeartbeat = isLargeLane && isRunLimiterEnabled;

            if (shouldRunLargeLaneHeartbeat)
            {
                if (_runLimiterSettings.HeartbeatIntervalMinutes <= 0)
                {
                    throw new Exception("Missing or invalid configuration: MultiLane:Large:RateLimiter:HeartbeatIntervalMinutes must be > 0");
                }

                if (_runLimiterSettings.LeaseTimeoutMinutes <= 0)
                {
                    throw new Exception("Missing or invalid configuration: MultiLane:Large:RateLimiter:LeaseTimeoutMinutes must be > 0");
                }

                heartbeatTask = LargeLaneHeartbeatLoopAsync(
                    context,
                    runId,
                    laneSize,
                    heartbeatIntervalMinutes: _runLimiterSettings.HeartbeatIntervalMinutes,
                    leaseTimeoutMinutes: _runLimiterSettings.LeaseTimeoutMinutes,
                    cancellationToken: heartbeatCts.Token);
            }

            try
            {
                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), groupMembership.SyncJob);
                if (groupId.Equals(Guid.Empty))
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Unable to get group id for job:{groupMembership.SyncJob.Id}", SyncJob = groupMembership.SyncJob });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(groupMembership.SyncJobId, SyncStatus.Error, groupMembership.SyncJob.ThresholdViolations, runId));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });

                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Failed;
                }
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Group Id for job:{groupMembership.SyncJob.Id} is {groupId}", SyncJob = groupMembership.SyncJob });

                jobTrackerEntityId = new EntityInstanceId(nameof(JobTrackerEntity), $"{groupId}_{runId}");

                syncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                                       new JobReaderRequest
                                                       {
                                                           JobId = groupMembership.SyncJob.Id,
                                                           RunId = runId
                                                       });

                if (syncJob.Status != SyncStatus.InProgress.ToString() && syncJob.Status != SyncStatus.StuckInProgress.ToString())
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                    new LoggerRequest { Message = $"Sync job status is {syncJob.Status}. Skipping additional messages if any.", SyncJob = syncJob });

                    if (groupMembership.TotalMessageCount > 1)
                    {
                        await context.CallActivityAsync(nameof(MessageRemoverFunction), new MessageRemoverRequest
                        {
                            RunId = request.RunId,
                            SubscriptionName = request.SubscriptionName,
                            TopicName = request.TopicName
                        });
                    }

                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Completed;
                }

                var sourceTypeCounts = JsonParser.GetQueryTypes(syncJob.Query);
                var destination = JsonParser.GetDestination(syncJob);

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorMultiLaneFunction)} function started", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                {
                    Message = $"Received membership message {groupMembership.MessageIndex}/{groupMembership.TotalMessageCount} from StarterFunction and will sync the obtained " +
                              $"{groupMembership.SourceMembers.Distinct().Count()} distinct members",
                    SyncJob = syncJob
                });

                JobState jobState = null;

                await using (await context.Entities.LockEntitiesAsync(jobTrackerEntityId.Value))
                {
                    jobState = await context.Entities.CallEntityAsync<JobState>(jobTrackerEntityId.Value, "GetState");
                }

                if (jobState.IsValidGroup == null)
                {
                    var isValidGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction),
                                              new GroupValidatorRequest
                                              {
                                                  RunId = groupMembership.RunId,
                                                  GroupId = groupMembership.Destination.ObjectId,
                                                  JobId = groupMembership.SyncJobId
                                              });

                    jobState.IsValidGroup = isValidGroup;

                    await using (await context.Entities.LockEntitiesAsync(jobTrackerEntityId.Value))
                    {
                        await context.Entities.CallEntityAsync(jobTrackerEntityId.Value, "SetState", jobState);
                    }
                }

                if (!jobState.IsValidGroup.Value)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                    SyncStatus.DestinationGroupNotFound, syncJob.ThresholdViolations, groupMembership.RunId));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationGroupNotFound, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorMultiLaneFunction)} function did not complete", SyncJob = syncJob });

                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Completed;
                }

                var isInitialSync = syncJob.LastRunTime == SqlDateTime.MinValue.Value;
                var membersToAdd = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Add).Distinct().ToList();
                var membersToRemove = groupMembership.SourceMembers.Where(x => x.MembershipAction == MembershipAction.Remove).Distinct().ToList();
                var membersAddedResponse = await context.CallActivityAsync<GroupUpdaterResponse>(nameof(GroupUpdaterFunction),
                                CreateGroupUpdaterRequest(syncJob, membersToAdd, RequestType.Add, isInitialSync, groupMembership.TotalMembersToAdd));
                sourceUsersNotFound = membersAddedResponse.UsersNotFound;
                var membersRemovedResponse = await context.CallActivityAsync<GroupUpdaterResponse>(nameof(GroupUpdaterFunction),
                                CreateGroupUpdaterRequest(syncJob, membersToRemove, RequestType.Remove, isInitialSync, groupMembership.TotalMembersToRemove));
                destinationUsersNotFound = membersRemovedResponse.UsersNotFound;

                await using (await context.Entities.LockEntitiesAsync(jobTrackerEntityId.Value))
                {
                    jobState = await context.Entities.CallEntityAsync<JobState>(jobTrackerEntityId.Value, "GetState");
                    jobState.TotalMembersToAdd += membersToAdd.Count;
                    jobState.TotalMembersToRemove += membersToRemove.Count;
                    jobState.TotalMembersAdded += membersAddedResponse.SuccessCount;
                    jobState.TotalMembersToAddNotFound += sourceUsersNotFound.Count;
                    jobState.TotalMembersToAddAlreadyExist += membersAddedResponse.UsersAlreadyExist.Count;
                    jobState.TotalMembersRemoved += membersRemovedResponse.SuccessCount;
                    jobState.TotalMembersToRemoveNotFound += destinationUsersNotFound.Count;
                    jobState.MessagesProcessed++;

                    await context.Entities.CallEntityAsync(jobTrackerEntityId.Value, "SetState", jobState);

                    syncCompleteEvent.Type = destination.Type.ToString();
                    syncCompleteEvent.SourceTypesCounts = sourceTypeCounts;
                    syncCompleteEvent.Destination = $"[{{\"type\":\"{destination.Type}\",\"value\":{{\"objectId\":\"{groupId}\"}}}}]";
                    syncCompleteEvent.GroupId = groupId.ToString();
                    syncCompleteEvent.RunId = syncJob.RunId.ToString();
                    syncCompleteEvent.IsDryRunEnabled = false.ToString();
                    syncCompleteEvent.ProjectedMemberCount = groupMembership.ProjectedMemberCount.HasValue ? groupMembership.ProjectedMemberCount.ToString() : "Not provided";
                    syncCompleteEvent.Identifier = request.LaneSize;
                    syncCompleteEvent.IsInitialSync = isInitialSync.ToString();
                    syncCompleteEvent.MembersToAdd = jobState.TotalMembersToAdd.ToString();
                    syncCompleteEvent.MembersToRemove = jobState.TotalMembersToRemove.ToString();
                    syncCompleteEvent.MembersAdded = jobState.TotalMembersAdded.ToString();
                    syncCompleteEvent.MembersToAddNotFound = jobState.TotalMembersToAddNotFound.ToString();
                    syncCompleteEvent.MembersToAddAlreadyExist = jobState.TotalMembersToAddAlreadyExist.ToString();
                    syncCompleteEvent.MembersRemoved = jobState.TotalMembersRemoved.ToString();
                    syncCompleteEvent.MembersToRemoveNotFound = jobState.TotalMembersToRemoveNotFound.ToString();
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                              new LoggerRequest
                                              {
                                                  Message = $"Users added {jobState.TotalMembersAdded}/{groupMembership.TotalMembersToAdd} so far. Users removed {jobState.TotalMembersRemoved}/{groupMembership.TotalMembersToRemove} so far.",
                                                  SyncJob = syncJob
                                              });

                if (membersAddedResponse.Status == GraphUpdaterStatus.GuestError)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Failing the job because there was an error since guest users cannot be added to this group", SyncJob = syncJob });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                        SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup, syncJob.ThresholdViolations, groupMembership.RunId));

                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                    {
                        JobStatus = SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup,
                        ResultStatus = ResultStatus.Success,
                        RunId = syncJob.RunId
                    });

                    var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                    new GroupNameReaderRequest { RunId = groupMembership.RunId, GroupId = groupMembership.Destination.ObjectId });

                    var additionalContent = new[]
                    {
                                groupMembership.Destination.ObjectId.ToString(),
                                groupName,
                                jobState.TotalMembersAdded.ToString(),
                                jobState.TotalMembersRemoved.ToString(),
                                DisabledNotificationType.StatusDescriptions[NotificationMessageType.GuestUserFailureNotification]
                    };

                    await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                    new EmailSenderRequest
                                                    {
                                                        SyncJob = syncJob,
                                                        NotificationType = NotificationMessageType.GuestUserFailureNotification,
                                                        AdditionalContentParams = additionalContent
                                                    });

                    TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");

                    if (syncJob?.RunId.HasValue ?? false)
                        _loggingRepository.RemoveSyncJobProperties(syncJob.RunId.Value);

                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorMultiLaneFunction)} function completed", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });

                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Completed;
                }


                if (groupMembership.IsLastMessage)
                {
                    if (isInitialSync)
                    {
                        var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                        new GroupNameReaderRequest { RunId = groupMembership.RunId, GroupId = groupMembership.Destination.ObjectId });

                        var additionalContent = new[]
                        {
                                groupMembership.Destination.ObjectId.ToString(),
                                groupName,
                                jobState.TotalMembersToAdd.ToString(),
                                jobState.TotalMembersToRemove.ToString(),
                                syncJob.Requestor,
                                _gmmResources.LearnMoreAboutGMMUrl,
                                _emailSenderAndRecipients.SupportEmailAddresses
                    };

                        await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                        new EmailSenderRequest
                                                        {
                                                            SyncJob = syncJob,
                                                            NotificationType = NotificationMessageType.SyncCompletedNotification,
                                                            AdditionalContentParams = additionalContent
                                                        });
                    }

                    SyncStatus syncStatus = SyncStatus.Idle;
                    ResultStatus resultStatus = ResultStatus.Success;

                    if (jobState.MessagesProcessed != groupMembership.TotalMessageCount)
                    {
                        syncStatus = SyncStatus.Error;
                        resultStatus = ResultStatus.Failure;

                        await context.CallActivityAsync(nameof(LoggerFunction),
                            new LoggerRequest { Message = $"Not all messages were processed, only {jobState.MessagesProcessed} out of {groupMembership.TotalMessageCount} were processed.", SyncJob = syncJob });
                    }

                    var message = GetUsersDataMessage(groupMembership.Destination.ObjectId, jobState.TotalMembersToAdd, jobState.TotalMembersToRemove);
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = message, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                      syncStatus, 0, groupMembership.RunId, jobState.TotalMembersAdded, jobState.TotalMembersRemoved));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                        new TelemetryTrackerRequest { JobStatus = syncStatus, ResultStatus = resultStatus, RunId = syncJob.RunId });

                    if (!context.IsReplaying)
                    {
                        if (syncStatus == SyncStatus.Error)
                        {
                            TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");
                        }
                        else if (jobState.TotalMembersAdded + jobState.TotalMembersToAddNotFound + jobState.TotalMembersToAddAlreadyExist == jobState.TotalMembersToAdd &&
                             jobState.TotalMembersRemoved + jobState.TotalMembersToRemoveNotFound == jobState.TotalMembersToRemove)
                        {
                            TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Success");
                        }
                        else
                        {
                            TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "PartialSuccess");
                        }

                        _telemetryClient.TrackMetric(nameof(Services.Entities.Metric.MembersNotFound), jobState.TotalMembersToAddNotFound + jobState.TotalMembersToRemoveNotFound);
                    }

                    if (_deltaCachingConfig.DeltaCacheEnabled && syncStatus != SyncStatus.Error)
                        await UpdateCachesAsync(context, sourceUsersNotFound, destinationUsersNotFound, syncJob, groupMembership.SourceMembers);

                    shouldEmitCompletion = true;
                }

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorMultiLaneFunction)} function completed", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });

                return OrchestrationRuntimeStatus.Completed;
            }
            catch (HttpRequestException httpEx)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Caught HttpRequestException, marking sync job status as transient error. Exception:\n{httpEx}", SyncJob = syncJob });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(groupMembership.SyncJobId, SyncStatus.TransientError, syncJob.ThresholdViolations, groupMembership.RunId));
                throw;
            }
            catch (MsalClientException msalEx)
            {
                if (msalEx.ErrorCode == "MULTIPLE_MATCHING_TOKENS_DETECTED")
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Caught MsalClientException, marking sync job status as transient error. Exception:\n{msalEx}", SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(groupMembership.SyncJobId, SyncStatus.TransientError, syncJob.ThresholdViolations, groupMembership.RunId));
                }
                throw;
            }
            catch (Exception ex)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Caught unexpected exception, marking sync job as errored. Exception:\n{ex}", SyncJob = syncJob });

                if (syncJob == null)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = "SyncJob is null. Removing the message from the queue..." });
                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Failed;
                }

                if (syncJob != null && groupMembership != null && groupMembership.SyncJobId != Guid.Empty)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(groupMembership.SyncJobId,
                                                                    SyncStatus.Error, syncJob.ThresholdViolations, groupMembership.RunId));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                }

                TrackSyncCompleteEvent(context, syncJob, syncCompleteEvent, "Failure");

                shouldEmitCompletion = true;

                throw;
            }
            finally
            {
                if (shouldRunLargeLaneHeartbeat)
                {
                    heartbeatCts.Cancel();
                    try
                    {
                        await heartbeatTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // expected
                    }
                }

                if (shouldEmitCompletion && isRunLimiterEnabled)
                {
                    await EmitMessageSplitterCompletionOnceAsync(context, runId, request.LaneSize, jobTrackerEntityId);
                }

                if (syncJob?.RunId.HasValue ?? false)
                    _loggingRepository.RemoveSyncJobProperties(syncJob.RunId.Value);
            }
        }

        private static async Task LargeLaneHeartbeatLoopAsync(
            TaskOrchestrationContext context,
            Guid runId,
            string laneSize,
            int heartbeatIntervalMinutes,
            int leaseTimeoutMinutes,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                await context.CallActivityAsync(
                    nameof(MessageSplitterLeaseRenewSenderFunction),
                    new MessageSplitterLeaseRenewSignal(runId, laneSize, leaseTimeoutMinutes));

                var fireAt = context.CurrentUtcDateTime.AddMinutes(heartbeatIntervalMinutes);
                await context.CreateTimer(fireAt, cancellationToken);
            }
        }

        private static async Task EmitMessageSplitterCompletionOnceAsync(
            TaskOrchestrationContext context,
            Guid runId,
            string laneSize,
            EntityInstanceId? jobTrackerEntityId)
        {
            if (runId == Guid.Empty)
            {
                return;
            }

            if (jobTrackerEntityId.HasValue)
            {
                JobState jobState;
                await using (await context.Entities.LockEntitiesAsync(jobTrackerEntityId.Value))
                {
                    jobState = await context.Entities.CallEntityAsync<JobState>(jobTrackerEntityId.Value, "GetState");
                    if (jobState.CompletionSent)
                    {
                        return;
                    }
                }
            }

            await context.CallActivityAsync(nameof(MessageSplitterCompletionSenderFunction), new Models.ServiceBus.MessageSplitterCompletionSignal(runId, laneSize));

            if (jobTrackerEntityId.HasValue)
            {
                await using (await context.Entities.LockEntitiesAsync(jobTrackerEntityId.Value))
                {
                    var jobState = await context.Entities.CallEntityAsync<JobState>(jobTrackerEntityId.Value, "GetState");
                    jobState.CompletionSent = true;
                    await context.Entities.CallEntityAsync(jobTrackerEntityId.Value, "SetState", jobState);
                }
            }
        }

        public async Task UpdateCachesAsync(TaskOrchestrationContext context,
                                                List<AzureADUser> sourceUsersNotFound,
                                                List<AzureADUser> destinationUsersNotFound,
                                                SyncJob syncJob,
                                                List<AzureADUser> sourceMembers)
        {
            if (sourceUsersNotFound != null && destinationUsersNotFound != null)
            {
                var destination = JsonParser.GetDestination(syncJob);

                var sourceObjectIds = new HashSet<Guid>(sourceUsersNotFound.Select(emp => emp.ObjectId));
                var destinationObjectIds = new HashSet<Guid>(destinationUsersNotFound.Select(emp => emp.ObjectId));
                var totalUsersNotFoundCount = sourceObjectIds.Count + destinationObjectIds.Count(id => !sourceObjectIds.Contains(id));

                if (!context.IsReplaying && totalUsersNotFoundCount > 0) { TrackUsersNotFoundEvent(syncJob.RunId, totalUsersNotFoundCount, destination.ObjectId); }

                var sourceGroups = new Dictionary<Guid, HashSet<Guid>>();
                var destinationUserIds = new HashSet<Guid>();

                foreach (var member in sourceMembers)
                {
                    if (sourceObjectIds.Contains(member.ObjectId) && member.SourceGroups != null)
                    {
                        foreach (var sourceGroupId in member.SourceGroups)
                        {
                            if (sourceGroupId == Guid.Empty)
                            {
                                continue;
                            }

                            if (!sourceGroups.TryGetValue(sourceGroupId, out var userIdsForGroup))
                            {
                                userIdsForGroup = new HashSet<Guid>();
                                sourceGroups[sourceGroupId] = userIdsForGroup;
                            }

                            userIdsForGroup.Add(member.ObjectId);
                        }
                    }

                    if (destinationObjectIds.Contains(member.ObjectId))
                    {
                        destinationUserIds.Add(member.ObjectId);
                    }
                }

                if (sourceGroups.Count > 0)
                {
                    // These calls to the cache updater suborchestrator were once done in parallel, but this caused an OutOfMemoryException due to loading multiple big files at once into memory.
                    // Although this does not affect many sync runs, we should revise it once we have upgraded our service plan.
                    foreach (var sourceGroup in sourceGroups)
                    {
                        await context.CallSubOrchestratorAsync(nameof(CacheUserUpdaterSubOrchestratorFunction),
                            new CacheUserUpdaterRequest
                            {
                                GroupId = sourceGroup.Key,
                                UserIds = sourceGroup.Value,
                                RunId = syncJob.RunId,
                                SyncJob = syncJob
                            });
                    }
                }

                if (destinationUserIds.Count > 0)
                {
                    await context.CallSubOrchestratorAsync(
                        nameof(CacheUserUpdaterSubOrchestratorFunction),
                        new CacheUserUpdaterRequest
                        {
                            GroupId = destination.ObjectId,
                            UserIds = destinationUserIds,
                            RunId = syncJob.RunId,
                            SyncJob = syncJob
                        });
                }
            }
        }

        private void TrackUsersNotFoundEvent(Guid? runId, int usersNotFoundCount, Guid groupId)
        {
            var usersNotFoundEvent = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() },
                { "TargetGroupId", groupId.ToString() },
                { "UsersNotFound", usersNotFoundCount.ToString() }
            };
            _telemetryClient.TrackEvent("UsersNotFoundCount", usersNotFoundEvent);
        }

        private void TrackSyncCompleteEvent(TaskOrchestrationContext context, SyncJob syncJob, SyncCompleteCustomEvent syncCompleteEvent, string successStatus)
        {
            var timeElapsedForJob = (context.CurrentUtcDateTime - syncJob.LastSuccessfulStartTime).TotalSeconds;
            _telemetryClient.TrackMetric(nameof(Metric.SyncJobTimeElapsedSeconds), timeElapsedForJob);

            syncCompleteEvent.SyncJobTimeElapsedSeconds = timeElapsedForJob.ToString();
            syncCompleteEvent.Result = successStatus;

            var syncCompleteDict = syncCompleteEvent.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(syncCompleteEvent, null));

            _telemetryClient.TrackEvent(nameof(Metric.SyncComplete), syncCompleteDict);
        }

        private JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(Guid jobId, SyncStatus syncStatus, int thresholdViolations, Guid runId, int? usersAdded = null, int? usersRemoved = null)
        {
            return new JobStatusUpdaterRequest
            {
                RunId = runId,
                JobId = jobId,
                Status = syncStatus,
                ThresholdViolations = thresholdViolations,
                UsersAdded = usersAdded ?? 0,
                UsersRemoved = usersRemoved ?? 0
            };
        }

        private GroupUpdaterRequest CreateGroupUpdaterRequest(SyncJob syncJob, ICollection<AzureADUser> members, RequestType type, bool isInitialSync, int? totalMembersCount)
        {
            return new GroupUpdaterRequest
            {
                SyncJob = syncJob,
                Members = members,
                Type = type,
                IsInitialSync = isInitialSync,
                TotalMemberCount = totalMembersCount
            };
        }

        private string GetUsersDataMessage(Guid targetGroupId, int membersToAdd, int membersToRemove)
        {
            return $"Synchronization for {targetGroupId} is now complete. " +
                   $"{membersToAdd} users have been added. " +
                   $"{membersToRemove} users have been removed.";
        }
    }
}
