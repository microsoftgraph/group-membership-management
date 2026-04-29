// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using JobTrigger.Activity.EmailSender;
using JobTrigger.Activity.SchemaValidator;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Models.Notifications;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class SubOrchestratorFunction
    {

        private readonly TelemetryClient _telemetryClient = null;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly IGMMResources _gmmResources;

        public SubOrchestratorFunction(TelemetryClient telemetryClient,
                                       IEmailSenderRecipient emailSenderAndRecipients,
                                       IGMMResources gmmResources)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _gmmResources = gmmResources ?? throw new ArgumentNullException(nameof(gmmResources));
            _emailSenderAndRecipients = emailSenderAndRecipients;
        }

        [Function(nameof(SubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var syncJob = context.GetInput<SyncJob>();
            var logger = context.CreateReplaySafeLogger($"JobTrigger.{nameof(SubOrchestratorFunction)}");
            using var scope = logger.BeginSyncJobScope(syncJob);

            try
            {
                if (!string.IsNullOrEmpty(syncJob.Status) && syncJob.Status == SyncStatus.StuckInProgress.ToString())
                {
                    logger.JobStuckInProgress();

                    await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.ErroredDueToStuckInProgress, SyncJob = syncJob });
                    return;
                }

                // Atomic claim — prevent duplicate processing
                var statusValue = syncJob.Status == SyncStatus.Idle.ToString() ? SyncStatus.InProgress : SyncStatus.StuckInProgress;
                var claimed = await context.CallActivityAsync<bool>(nameof(ClaimJobFunction), new ClaimJobRequest { Status = statusValue, SyncJob = syncJob });
                if (!claimed)
                {
                    logger.SubOrchestratorJobAlreadyClaimed(syncJob.Id);
                    return;
                }

                if (!context.IsReplaying) { TrackJobsStartedEvent(syncJob.RunId); }

                logger.FunctionStarted(nameof(SubOrchestratorFunction));

                var frequency = await context.CallActivityAsync<int>(nameof(JobTrackerFunction), syncJob);

                var groupId = Guid.Empty;
                var channelId = "";

                try
                {
                    var parsedAndValidatedDestination = await context.CallActivityAsync<ParsedAndValidateDestinationResponse>(nameof(ParseAndValidateDestinationFunction), syncJob);

                    if (!parsedAndValidatedDestination.IsValid)
                    {
                        logger.DestinationQueryEmpty(syncJob.Id);

                        await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.DestinationQueryNotValid, SyncJob = syncJob });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationQueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                        return;
                    }

                    if (syncJob.MembershipType == "GroupMembership")
                    {
                        var group = syncJob.Group ?? await context.CallActivityAsync<Group>(nameof(GetGroupFunction), syncJob);
                        if (group == null)
                        {
                            logger.GroupNotFound(syncJob.Id);
                            await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                            return;
                        }
                        groupId = group.GroupId;
                        syncJob.Group = group;
                        syncJob.Destination = $"[{{\"type\":\"{syncJob.MembershipType}\",\"value\":{{\"objectId\":\"{groupId}\"}}}}]";
                    }
                    else if (syncJob.MembershipType == "TeamsChannelMembership")
                    {
                        var channel = syncJob.Channel ?? await context.CallActivityAsync<Channel>(nameof(GetChannelFunction), syncJob);
                        if (channel == null)
                        {
                            logger.ChannelNotFound(syncJob.Id);
                            await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                            return;
                        }
                        groupId = channel.GroupId;
                        channelId = channel.ChannelId;
                        syncJob.Channel = channel;
                        syncJob.Destination = $"[{{\"type\":\"{syncJob.MembershipType}\",\"value\":{{\"objectId\":\"{groupId}\",\"channelId\":\"{channelId}\"}}}}]";
                    }

                    // Updates the job with the standardized destination only — avoids overwriting Status and LastSuccessfulStartTime
                    await context.CallActivityAsync(nameof(DestinationUpdaterFunction), new DestinationUpdaterRequest { JobId = syncJob.Id, Destination = syncJob.Destination });

                }
                catch (JsonException)
                {
                    logger.DestinationQueryNotValid(syncJob.Id);

                    await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.DestinationQueryNotValid, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationQueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }


                if (!context.IsReplaying)
                {
                    if (syncJob.Status == SyncStatus.Idle.ToString())
                    {
                        TrackIdleJobsEvent(frequency, groupId);
                    }
                    else if (syncJob.Status == SyncStatus.InProgress.ToString())
                    {
                        TrackInProgressJobsEvent(frequency, groupId, syncJob.RunId);
                    }
                }

                if (!string.IsNullOrWhiteSpace(syncJob.Query))
                {
                    try
                    {
                        // Make sure the query is valid JSON.
                        var query = JsonDocument.Parse(syncJob.Query);

                        var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), syncJob);
                        if (!hasValidJson)
                        {
                            await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                                            new TelemetryTrackerRequest
                                                            {
                                                                JobStatus = SyncStatus.SchemaError,
                                                                ResultStatus = ResultStatus.Failure,
                                                                RunId = syncJob.RunId
                                                            });

                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        logger.SourceQueryNotValid(syncJob.Id);

                        await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                        return;
                    }
                }
                else
                {
                    logger.SourceQueryEmpty(syncJob.Id);

                    await context.CallActivityAsync(nameof(JobUpdaterFunction), new JobUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }

                if (!context.IsReplaying)
                {
                    TrackExclusionaryEvent(syncJob);
                }

                var verifierResult = await context.CallActivityAsync<DestinationVerifierResult>(nameof(DestinationVerifierFunction), syncJob);

                var destinationName = await context.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), syncJob);

                if (verifierResult == DestinationVerifierResult.NotFound)
                {
                    if (destinationName == "")
                        destinationName = "NAME NOT FOUND";

                    await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                    new EmailSenderRequest
                                                    {
                                                        SyncJob = syncJob,
                                                        NotificationType = NotificationMessageType.DestinationNotExistNotification,
                                                        AdditionalContentParams = new[]
                                                        {
                                                        groupId.ToString(),
                                                        destinationName,
                                                        DisabledNotificationType.StatusDescriptions[NotificationMessageType.DestinationNotExistNotification]
                                                        }
                                                    });

                    await context.CallActivityAsync(nameof(JobUpdaterFunction),
                                                    new JobUpdaterRequest { Status = SyncStatus.DestinationGroupNotFound, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                                    new TelemetryTrackerRequest { JobStatus = SyncStatus.DestinationGroupNotFound, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                    return;
                }

                if (verifierResult == DestinationVerifierResult.NotOwnedByGMM)
                {
                    await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                    new EmailSenderRequest
                                                    {
                                                        SyncJob = syncJob,
                                                        NotificationType = NotificationMessageType.NotOwnerNotification,
                                                        AdditionalContentParams = new[]
                                                        {
                                                        groupId.ToString(),
                                                        destinationName,
                                                        DisabledNotificationType.StatusDescriptions[NotificationMessageType.NotOwnerNotification]
                                                        }
                                                    });

                    await context.CallActivityAsync(nameof(JobUpdaterFunction),
                                                    new JobUpdaterRequest { Status = SyncStatus.NotOwnerOfDestinationGroup, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                                    new TelemetryTrackerRequest { JobStatus = SyncStatus.NotOwnerOfDestinationGroup, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                    return;
                }

                if (syncJob.LastRunTime == SqlDateTime.MinValue.Value)
                    await context.CallActivityAsync(nameof(EmailSenderFunction),
                                                    new EmailSenderRequest
                                                    {
                                                        SyncJob = syncJob,
                                                        NotificationType = NotificationMessageType.SyncStartedNotification,
                                                        AdditionalContentParams = new[]
                                                        {
                                                            groupId.ToString(),
                                                            destinationName,
                                                            _emailSenderAndRecipients.SupportEmailAddresses,
                                                            _gmmResources.LearnMoreAboutGMMUrl,
                                                            syncJob.Requestor
                                                        }

                                                    });

                var latestSyncJob = await context.CallActivityAsync<SyncJob>(nameof(GetSyncJobFunction), syncJob.Id);
                if (latestSyncJob != null)
                {
                    latestSyncJob.RunId = syncJob.RunId;
                    await context.CallActivityAsync(nameof(TopicMessageSenderFunction), latestSyncJob);
                }
                else
                {
                    logger.FailedToRetrieveLatestJobState(syncJob.Id);
                    await context.CallActivityAsync(nameof(TopicMessageSenderFunction), syncJob);
                }

            }
            catch (Exception ex)
            {
                logger.SubOrchestratorException(ex);

                await context.CallActivityAsync(nameof(JobUpdaterFunction),
                    new JobUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });

                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                return;
            }
            finally
            {
                logger.FunctionCompleted(nameof(SubOrchestratorFunction));
            }
        }

        private void TrackJobsStartedEvent(Guid? runId)
        {
            var jobsStartedEvent = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() }
            };
            _telemetryClient.TrackEvent("NumberOfJobsStarted", jobsStartedEvent);
        }

        private void TrackIdleJobsEvent(int frequency, Guid destinationGroupObjectId)
        {
            var jobStarted = frequency >= 1 ? 1 : 0;

            var idleJobsEvent = new Dictionary<string, string>
            {
                { "DestinationGroupObjectId", destinationGroupObjectId.ToString() },
                { "Frequency", frequency.ToString() },
                { "JobStarted", jobStarted.ToString() }
            };

            _telemetryClient.TrackEvent("IdleJobsTracker", idleJobsEvent);
        }

        private void TrackInProgressJobsEvent(int frequency, Guid destinationGroupObjectId, Guid? runId)
        {
            var inProgressJobsEvent = new Dictionary<string, string>
            {
                { "DestinationGroupObjectId", destinationGroupObjectId.ToString() },
                { "Frequency", frequency.ToString() },
                { "RunId", runId.ToString() }
            };

            _telemetryClient.TrackEvent("InProgressJobsTracker", inProgressJobsEvent);
        }

        private void TrackExclusionaryEvent(SyncJob syncJob)
        {
            var parsedQuery = JsonNode.Parse(syncJob.Query).AsArray();
            var queryTypes = parsedQuery.Select(x => new
            {
                exclusionary = x["exclusionary"] != null ? (bool)x["exclusionary"] : false
            }).ToList();

            var groupId = syncJob.MembershipType == "GroupMembership" ? syncJob.Group.GroupId.ToString() : syncJob.Channel.GroupId.ToString();

            var exclusionaryEvent = new Dictionary<string, string>
            {
                { "Destination", $"[{{\"type\":\"{syncJob.MembershipType}\",\"value\":{{\"objectId\":\"{groupId}\"}}}}]" },
                { "DestinationGroupObjectId", groupId },
                { "TotalNumberOfSourceParts", queryTypes.Count.ToString() },
                { "NumberOfExclusionarySourceParts", queryTypes.Where(g => g.exclusionary).Count().ToString() }
            };
            _telemetryClient.TrackEvent("ExclusionarySourcePartsCount", exclusionaryEvent);
        }
    }
}
