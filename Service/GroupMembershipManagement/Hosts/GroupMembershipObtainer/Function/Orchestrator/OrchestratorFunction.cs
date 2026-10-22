// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class OrchestratorFunction
    {
        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            if (mainRequest != null && mainRequest.SyncJob != null)
            {
                var syncJob = mainRequest.SyncJob;
                var logger = context.CreateReplaySafeLogger("GroupMembershipObtainer.OrchestratorFunction");
                string filePath = null;
                List<string> identityTypes = null;

                using (logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
                {
                    ["CurrentPart"] = mainRequest.CurrentPart,
                    ["TotalParts"] = mainRequest.TotalParts
                }))
                {
                    try
                    {
                        if (mainRequest.CurrentPart <= 0 || mainRequest.TotalParts <= 0)
                        {
                            logger.InvalidPartValues();
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            return;
                        }

                        logger.FunctionStarted(nameof(OrchestratorFunction));
                        var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), new GetGroupRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                        if (groupId.Equals(Guid.Empty))
                        {
                            logger.UnableToGetGroupId(syncJob.Id);
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            return;
                        }
                        logger.GroupIdRetrieved(syncJob.Id, groupId);
                        var response = await context.CallActivityAsync<GroupReaderResponse>(nameof(GroupReaderFunction),
                                                                                            new GroupReaderRequest
                                                                                            {
                                                                                                SyncJob = syncJob,
                                                                                                GroupId = groupId,
                                                                                                CurrentPart = mainRequest.CurrentPart,
                                                                                                TotalParts = mainRequest.TotalParts,
                                                                                                IsDestinationPart = mainRequest.IsDestinationPart
                                                                                            });

                        if (response.SourceGroup.ObjectId == Guid.Empty)
                        {
                            logger.InvalidSourceGroupId(mainRequest.CurrentPart, syncJob.Query, SyncStatus.QueryNotValid.ToString());

                            var destinationName = await context.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), new DestinationNameReaderRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            if (destinationName == null)
                            {
                                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = SyncStatus.Error,
                                    CurrentPart = mainRequest.CurrentPart,
                                    TotalParts = mainRequest.TotalParts
                                });
                                return;
                            }
                            var additionalContentParams = new[]
                            {
                                destinationName.ToString(),
                                groupId.ToString(),
                                response.SourceGroupId.ToString(),
                                DisabledNotificationType.StatusDescriptions[NotificationMessageType.NotValidSourceNotification]
                            };
                            await context.CallActivityAsync(nameof(EmailSenderFunction), new EmailSenderRequest {
                                                            SyncJob = syncJob,
                                                            NotificationType = NotificationMessageType.NotValidSourceNotification,
                                                            AdditionalContentParams = additionalContentParams,
                                                            CurrentPart = mainRequest.CurrentPart,
                                                            TotalParts = mainRequest.TotalParts
                                                            });
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.QueryNotValid, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            return;
                        }
                        else
                        {
                            if (!mainRequest.IsDestinationPart)
                            {
                                try
                                {
                                    using var queryDocument = JsonDocument.Parse(syncJob.Query);
                                    var currentPart = queryDocument.RootElement
                                        .EnumerateArray()
                                        .ElementAt(mainRequest.CurrentPart - 1);
                                    var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { Query = currentPart.GetRawText(), SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                                    if (!hasValidJson)
                                    {
                                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                                        return;
                                    }

                                    if (!GroupIdentitySelection.TryParse(
                                        currentPart,
                                        out _,
                                        out identityTypes,
                                        out _))
                                    {
                                        await context.CallActivityAsync(
                                            nameof(JobStatusUpdaterFunction),
                                            new JobStatusUpdaterRequest
                                            {
                                                Status = SyncStatus.SchemaError,
                                                SyncJob = syncJob,
                                                CurrentPart = mainRequest.CurrentPart,
                                                TotalParts = mainRequest.TotalParts
                                            });
                                        return;
                                    }
                                }
                                catch (JsonException)
                                {
                                    logger.InvalidSourceQuery(syncJob.Id);
                                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                                    return;
                                }
                            }

                            var sgResponse = await context.CallSubOrchestratorAsync<SubOrchestratorResponse>(nameof(SubOrchestratorFunction),
                                                                                                                            new GroupMembershipRequest
                                                                                                                            {
                                                                                                                                SyncJob = syncJob,
                                                                                                                                GroupId = groupId,
                                                                                                                                SourceGroup = response.SourceGroup,
                                                                                                                                CurrentPart = mainRequest.CurrentPart,
                                                                                                                                TotalParts = mainRequest.TotalParts,
                                                                                                                                Exclusionary = mainRequest.Exclusionary,
                                                                                                                                IdentityTypes = identityTypes,
                                                                                                                                IsDestinationPart = mainRequest.IsDestinationPart
                                                                                                                            });

                            if (sgResponse.Status == SyncStatus.SourceGroupNotFound || sgResponse.Status == SyncStatus.NestedGroupsFound)
                            {
                                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = sgResponse.Status, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = sgResponse.Status, ResultStatus = ResultStatus.Success, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                                return;
                            }

                            filePath = sgResponse.FilePath;

                            var content = new MembershipAggregatorHttpRequest
                            {
                                FilePath = filePath,
                                PartNumber = mainRequest.CurrentPart,
                                PartsCount = mainRequest.TotalParts,
                                SyncJob = syncJob,
                                IsDestinationPart = mainRequest.IsDestinationPart
                            };

                            await context.CallActivityAsync(nameof(QueueMessageSenderFunction), content);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message != null && ex.Message.Contains("The request timed out"))
                        {
                            syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                            logger.ReschedulingJobDueToTimeout(syncJob.StartDate);
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            return;
                        }

                        logger.OrchestratorUnexpectedException(ex, mainRequest.CurrentPart);

                        // Retry exhaustion (Graph still throttled or failing after the retry owner's attempts) is
                        // transient: nothing is published, and JobTrigger picks the job up again on its next tick.
                        var failureStatus = IsRetryExhaustion(ex) ? SyncStatus.TransientError : SyncStatus.Error;
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = failureStatus, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = failureStatus, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });

                        throw;
                    }

                    logger.FunctionCompleted(nameof(OrchestratorFunction));
                }
            }
        }

        /// <summary>
        /// The error type the Functions host records for an activity that ran past <c>functionTimeout</c> (measured
        /// locally with the isolated worker and Durable Functions, 2026-10-21): the activity's token is cancelled and the
        /// orchestrator receives a <see cref="TaskFailedException"/> with this error type, without redelivery.
        /// </summary>
        internal const string FunctionTimeoutErrorType = "Microsoft.Azure.WebJobs.Host.FunctionTimeoutException";

        /// <summary>
        /// True when the failure is, or was caused at any depth by, <see cref="GraphRetriesExhaustedException"/> or an
        /// activity that ran past the function timeout (the backstop for a Graph wait the timeout cut short). Both are
        /// treated as retry exhaustion: nothing is published and the job is set to TransientError. A failed
        /// sub-orchestration or activity reaches this orchestrator as <see cref="TaskFailedException"/>, so its failure
        /// details chain is walked by exception type name.
        /// </summary>
        internal static bool IsRetryExhaustion(Exception exception)
        {
            var exhaustedType = typeof(GraphRetriesExhaustedException).FullName;
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is GraphRetriesExhaustedException)
                {
                    return true;
                }

                if (current is TaskFailedException taskFailed)
                {
                    for (var details = taskFailed.FailureDetails; details != null; details = details.InnerFailure)
                    {
                        if (string.Equals(details.ErrorType, exhaustedType, StringComparison.Ordinal) ||
                            string.Equals(details.ErrorType, FunctionTimeoutErrorType, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}