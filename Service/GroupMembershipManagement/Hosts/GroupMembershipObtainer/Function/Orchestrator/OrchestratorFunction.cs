// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                                    var queryParts = JsonNode.Parse(syncJob.Query).AsArray();
                                    var currentPart = queryParts[mainRequest.CurrentPart - 1];
                                    var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { Query = currentPart.ToString(), SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                                    if (!hasValidJson)
                                    {
                                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
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
                                                                                                                                Exclusionary = mainRequest.Exclusionary
                                                                                                                            });

                            if (sgResponse.Status == SyncStatus.SecurityGroupNotFound || sgResponse.Status == SyncStatus.NestedGroupsFound)
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

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });

                        throw;
                    }

                    logger.FunctionCompleted(nameof(OrchestratorFunction));
                }
            }
        }
    }
}