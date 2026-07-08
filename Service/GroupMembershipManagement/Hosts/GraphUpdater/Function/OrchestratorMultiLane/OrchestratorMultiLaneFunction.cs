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
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Net.Http;
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
        private readonly IDeltaCachingConfig _deltaCachingConfig = null;
        private readonly RunLimiterSettings _runLimiterSettings;

        public OrchestratorMultiLaneFunction(
            TelemetryClient telemetryClient,
            IGraphUpdaterService graphUpdaterService,
            IEmailSenderRecipient emailSenderAndRecipients,
            IGMMResources gmmResources,
            IDeltaCachingConfig deltaCachingConfig,
            RunLimiterSettings runLimiterSettings)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
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
            var logger = context.CreateReplaySafeLogger("GraphUpdater.OrchestratorMultiLaneFunction");
            var additionalProperties = new Dictionary<string, object>
            {
                ["MessageIndex"] = groupMembership.MessageIndex,
                ["TotalMessageCount"] = groupMembership.TotalMessageCount,
                ["Instance"] = request.SubscriptionName ?? string.Empty
            };
            using var scope = logger.BeginSyncJobScope(groupMembership.SyncJob, additionalProperties);

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
                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), new GetGroupRequest { SyncJob = groupMembership.SyncJob });
                if (groupId.Equals(Guid.Empty))
                {
                    logger.UnableToGetGroupId(groupMembership.SyncJob.Id);
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(groupMembership.SyncJob, SyncStatus.Error, groupMembership.SyncJob.ThresholdViolations));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = groupMembership.SyncJob });

                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Failed;
                }
                logger.GroupIdRetrieved(groupMembership.SyncJob.Id, groupId);

                jobTrackerEntityId = new EntityInstanceId(nameof(JobTrackerEntity), $"{groupId}_{runId}");

                syncJob = await context.CallActivityAsync<SyncJob>(nameof(JobReaderFunction),
                                                       new JobReaderRequest
                                                       {
                                                           SyncJob = groupMembership.SyncJob
                                                       });

                if (syncJob.Status != SyncStatus.InProgress.ToString() && syncJob.Status != SyncStatus.StuckInProgress.ToString())
                {
                    logger.SyncJobStatusSkipping(syncJob.Status);

                    if (groupMembership.TotalMessageCount > 1
                        && !string.Equals(request.LaneSize, "large", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only clean up remaining messages for non-session subscriptions.
                        await context.CallActivityAsync(nameof(MessageRemoverFunction), new MessageRemoverRequest
                        {
                            SyncJob = syncJob,
                            SubscriptionName = request.SubscriptionName,
                            TopicName = request.TopicName
                        });
                    }

                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Completed;
                }

                var sourceTypeCounts = JsonParser.GetQueryTypes(syncJob.Query);
                var destination = JsonParser.GetDestination(syncJob);

                logger.FunctionStarted(nameof(OrchestratorMultiLaneFunction));
                logger.ReceivedMembershipMultiLane(groupMembership.MessageIndex, groupMembership.TotalMessageCount, groupMembership.SourceMembers.Distinct().Count());

                // Read only the validity flag (never the whole JobState), so establishing group
                // validity can't clobber the accumulated message totals.
                var isValidGroup = await context.Entities.CallEntityAsync<bool?>(
                    jobTrackerEntityId.Value, nameof(JobTrackerEntity.GetIsValidGroup));

                if (isValidGroup == null)
                {
                    var validatedGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction),
                                              new GroupValidatorRequest
                                              {
                                                  SyncJob = syncJob,
                                                  GroupId = groupMembership.Destination.ObjectId
                                              });

                    // First-writer-wins set of only IsValidGroup; returns the effective value.
                    isValidGroup = await context.Entities.CallEntityAsync<bool>(
                        jobTrackerEntityId.Value, nameof(JobTrackerEntity.SetIsValidGroupIfUnset), validatedGroup);
                }

                if (!isValidGroup.Value)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(syncJob,
                                                                    SyncStatus.DestinationGroupNotFound, syncJob.ThresholdViolations));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationGroupNotFound, ResultStatus = ResultStatus.Success, SyncJob = syncJob });
                    logger.MultiLaneDidNotComplete();

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

                // Atomic fold-and-check: accumulate this message's results and learn whether the run
                // is complete. Replaces the split GetState/increment/SetState that lost accumulated
                // state when the entity idled past the extended-session timeout (see JobTrackerEntity).
                var registration = new JobTrackerMessageRegistration
                {
                    MessageIndex = groupMembership.MessageIndex,
                    TotalMessageCount = groupMembership.TotalMessageCount,
                    MembersToAdd = membersToAdd.Count,
                    MembersToRemove = membersToRemove.Count,
                    MembersAdded = membersAddedResponse.SuccessCount,
                    MembersRemoved = membersRemovedResponse.SuccessCount,
                    MembersToAddNotFound = sourceUsersNotFound.Count,
                    MembersToAddAlreadyExist = membersAddedResponse.UsersAlreadyExist.Count,
                    MembersToRemoveNotFound = destinationUsersNotFound.Count
                };

                var updateResult = await context.Entities.CallEntityAsync<JobTrackerUpdateResult>(
                    jobTrackerEntityId.Value,
                    nameof(JobTrackerEntity.RegisterMessageAndCheckComplete),
                    registration);

                var jobState = updateResult.State;

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

                logger.MultiLaneProgressUpdate(jobState.TotalMembersAdded, groupMembership.TotalMembersToAdd ?? 0, jobState.TotalMembersRemoved, groupMembership.TotalMembersToRemove ?? 0);

                if (membersAddedResponse.Status == GraphUpdaterStatus.GuestError)
                {
                    logger.GuestUsersCannotBeAdded();

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(syncJob,
                                                                        SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup, syncJob.ThresholdViolations));

                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest
                    {
                        JobStatus = SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup,
                        ResultStatus = ResultStatus.Success,
                        SyncJob = syncJob
                    });

                    var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                    new GroupNameReaderRequest { SyncJob = syncJob, GroupId = groupMembership.Destination.ObjectId });

                    var additionalContent = new[]
                    {
                                groupMembership.Destination.ObjectId.ToString(),
                                groupName,
                                jobState.TotalMembersAdded.ToString(),
                                jobState.TotalMembersRemoved.ToString(),
                                DisabledNotificationType.StatusDescriptions[NotificationMessageType.GuestUserFailureNotification],
                                context.CurrentUtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                    };

                    await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                    new EmailSenderRequest
                                                    {
                                                        SyncJob = syncJob,
                                                        NotificationType = NotificationMessageType.GuestUserFailureNotification,
                                                        AdditionalContentParams = additionalContent
                                                    });

                    if (!context.IsReplaying)
                    {
                        SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(_telemetryClient, syncCompleteEvent, context.CurrentUtcDateTime, syncJob.LastSuccessfulStartTime, "Failure");
                    }

                    logger.FunctionCompleted(nameof(OrchestratorMultiLaneFunction));

                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Completed;
                }

                // Finalize exactly once, gated on the entity's single-writer IsComplete claim (not a
                // mutable counter), so a state-loss miscount can't finalize as a false Error. The
                // Error path below is scoped to a genuine lost part only — the ordered large lane with
                // an actual shortfall — so a duplicate/replayed terminal message or a small-lane
                // message never stamps a false Error.
                if (updateResult.IsComplete)
                {
                    if (isInitialSync)
                    {
                        var groupName = await context.CallActivityAsync<string>(nameof(GroupNameReaderFunction),
                                                        new GroupNameReaderRequest { SyncJob = syncJob, GroupId = groupMembership.Destination.ObjectId });

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

                    var message = GetUsersDataMessage(groupMembership.Destination.ObjectId, jobState.TotalMembersToAdd, jobState.TotalMembersToRemove);
                    logger.UsersDataInfo(message);
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(syncJob,
                                                                      syncStatus, 0, jobState.TotalMembersAdded, jobState.TotalMembersRemoved));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                        new TelemetryTrackerRequest { JobStatus = syncStatus, ResultStatus = resultStatus, SyncJob = syncJob });

                    if (!context.IsReplaying)
                    {
                        if (jobState.TotalMembersAdded + jobState.TotalMembersToAddNotFound + jobState.TotalMembersToAddAlreadyExist == jobState.TotalMembersToAdd &&
                             jobState.TotalMembersRemoved + jobState.TotalMembersToRemoveNotFound == jobState.TotalMembersToRemove)
                        {
                            SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(_telemetryClient, syncCompleteEvent, context.CurrentUtcDateTime, syncJob.LastSuccessfulStartTime, "Success");
                        }
                        else
                        {
                            SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(_telemetryClient, syncCompleteEvent, context.CurrentUtcDateTime, syncJob.LastSuccessfulStartTime, "PartialSuccess");
                        }

                        _telemetryClient.TrackMetric(nameof(Services.Entities.Metric.MembersNotFound), jobState.TotalMembersToAddNotFound + jobState.TotalMembersToRemoveNotFound);
                    }

                    if (_deltaCachingConfig.DeltaCacheEnabled)
                        await UpdateCachesAsync(context, sourceUsersNotFound, destinationUsersNotFound, syncJob, groupMembership.SourceMembers);

                    shouldEmitCompletion = true;
                }
                else if (isLargeLane
                         && groupMembership.IsLastMessage
                         && updateResult.MessagesProcessed < updateResult.TotalMessageCount)
                {
                    // Genuine lost part: only the ordered large lane guarantees the terminal chunk
                    // arrives after every earlier part, so a shortfall here means a part was lost.
                    // MessagesProcessed < TotalMessageCount is a real shortfall; a duplicate/replayed
                    // terminal message instead returns IsComplete=false with all parts present and
                    // never reaches here. Emit the detection signal (EventId 20054) and stamp Error so
                    // the incompletion stays visible; no completion email (a "completed" email on an
                    // incomplete sync would mislead).
                    logger.NotAllMessagesProcessed(updateResult.MessagesProcessed, updateResult.TotalMessageCount);

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        CreateJobStatusUpdaterRequest(syncJob,
                                                                      SyncStatus.Error, 0, jobState.TotalMembersAdded, jobState.TotalMembersRemoved));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                        new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob });

                    if (!context.IsReplaying)
                    {
                        SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(_telemetryClient, syncCompleteEvent, context.CurrentUtcDateTime, syncJob.LastSuccessfulStartTime, "Failure");
                    }

                    shouldEmitCompletion = true;
                }

                logger.FunctionCompleted(nameof(OrchestratorMultiLaneFunction));

                return OrchestrationRuntimeStatus.Completed;
            }
            catch (HttpRequestException httpEx)
            {
                logger.OrchestratorHttpException(httpEx);
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(syncJob, SyncStatus.TransientError, syncJob.ThresholdViolations));
                throw;
            }
            catch (MsalClientException msalEx)
            {
                if (msalEx.ErrorCode == "MULTIPLE_MATCHING_TOKENS_DETECTED")
                {
                    logger.OrchestratorMsalException(msalEx);
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), CreateJobStatusUpdaterRequest(syncJob, SyncStatus.TransientError, syncJob.ThresholdViolations));
                }
                throw;
            }
            catch (Exception ex)
            {
                logger.OrchestratorUnexpectedException(ex);

                if (syncJob == null)
                {
                    logger.SyncJobIsNull();
                    shouldEmitCompletion = true;
                    return OrchestrationRuntimeStatus.Failed;
                }

                if (syncJob != null && groupMembership != null && groupMembership.SyncJobId != Guid.Empty)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                    CreateJobStatusUpdaterRequest(syncJob,
                                                                    SyncStatus.Error, syncJob.ThresholdViolations));
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob });
                }

                if (!context.IsReplaying)
                {
                    SyncCompleteTelemetryHelper.TrackSyncCompleteEventAndMetric(_telemetryClient, syncCompleteEvent, context.CurrentUtcDateTime, syncJob.LastSuccessfulStartTime, "Failure");
                }

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
                    catch (Exception ex)
                    {
                        // Non-cancellation heartbeat failure (e.g., LeaseRenewSender activity failed).
                        // Log but don't rethrow — we must still emit the completion signal below
                        // to release the RunLimiter lease.
                        var heartbeatLogger = context.CreateReplaySafeLogger("GraphUpdater.OrchestratorMultiLaneFunction");
                        heartbeatLogger.HeartbeatFinallyException(ex);
                    }
                }

                if (shouldEmitCompletion && isRunLimiterEnabled)
                {
                    await EmitMessageSplitterCompletionOnceAsync(context, runId, request.LaneSize, jobTrackerEntityId);
                }
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
                // Single-writer claim: only the first finalizer to atomically flip CompletionSent
                // sends, closing the check-then-act window of the old lock/GetState-then-mark pair.
                // The claim is in durable history, so the send is retried after a crash (idempotent
                // via RunLimiter.Release) — no leaked lease.
                var claimedCompletion = await context.Entities.CallEntityAsync<bool>(
                    jobTrackerEntityId.Value, nameof(JobTrackerEntity.TryMarkCompletionSent));
                if (!claimedCompletion)
                {
                    return;
                }
            }

            await context.CallActivityAsync(nameof(MessageSplitterCompletionSenderFunction), new Models.ServiceBus.MessageSplitterCompletionSignal(runId, laneSize));
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

        private static JobStatusUpdaterRequest CreateJobStatusUpdaterRequest(SyncJob syncJob, SyncStatus syncStatus, int thresholdViolations, int usersAdded = 0, int usersRemoved = 0)
        {
            return new JobStatusUpdaterRequest
            {
                SyncJob = syncJob,
                Status = syncStatus,
                ThresholdViolations = thresholdViolations,
                UsersAdded = usersAdded,
                UsersRemoved = usersRemoved
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
