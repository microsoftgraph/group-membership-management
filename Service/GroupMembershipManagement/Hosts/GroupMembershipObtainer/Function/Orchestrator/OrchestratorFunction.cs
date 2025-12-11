// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Microsoft.DurableTask;
using Microsoft.Extensions.Configuration;
using Models;
using Models.Notifications;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IConfiguration _configuration;
        private readonly SGMembershipCalculator _calculator;
        private readonly IEmailSenderRecipient _emailSenderRecipient;

        public OrchestratorFunction(
            ILoggingRepository loggingRepository,
            SGMembershipCalculator calculator,
            IConfiguration configuration,
            IEmailSenderRecipient emailSenderRecipient
            )
        {
            _log = loggingRepository;
            _calculator = calculator;
            _configuration = configuration;
            _emailSenderRecipient = emailSenderRecipient;
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            if (mainRequest != null && mainRequest.SyncJob != null)
            {
                var syncJob = mainRequest.SyncJob;
                var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
                string filePath = null;

                try
                {
                    if (mainRequest.CurrentPart <= 0 || mainRequest.TotalParts <= 0)
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Found invalid value for CurrentPart or TotalParts" });
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                        return;
                    }

                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started", RunId = runId }, VerbosityLevel.DEBUG);
                    var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), syncJob);
                    if (groupId.Equals(Guid.Empty))
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Unable to get group id for job:{syncJob.Id}", RunId = runId }, VerbosityLevel.DEBUG);
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                        return;
                    }
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Group Id for job:{syncJob.Id} is {groupId}", RunId = runId }, VerbosityLevel.DEBUG);
                    var response = await context.CallActivityAsync<GroupReaderResponse>(nameof(GroupReaderFunction),
                                                                                        new GroupReaderRequest
                                                                                        {
                                                                                            SyncJob = syncJob,
                                                                                            GroupId = groupId,
                                                                                            CurrentPart = mainRequest.CurrentPart,
                                                                                            IsDestinationPart = mainRequest.IsDestinationPart,
                                                                                            RunId = runId
                                                                                        });

                    if (response.SourceGroup.ObjectId == Guid.Empty)
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Source group id is not a valid, Part# {mainRequest.CurrentPart} {syncJob.Query}. Marking job as {SyncStatus.QueryNotValid}." });

                        var destinationName = await context.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), syncJob);
                        if (destinationName == null)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest
                            {
                                SyncJob = syncJob,
                                Status = SyncStatus.Error
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
                                                        AdditionalContentParams = additionalContentParams
                                                        });
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.QueryNotValid });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = runId });
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

                        var sgResponse = await context.CallSubOrchestratorAsync<SubOrchestratorResponse>(nameof(SubOrchestratorFunction),
                                                                                                                        new GroupMembershipRequest
                                                                                                                        {
                                                                                                                            SyncJob = syncJob,
                                                                                                                            GroupId = groupId,
                                                                                                                            SourceGroup = response.SourceGroup,
                                                                                                                            CurrentPart = mainRequest.CurrentPart,
                                                                                                                            RunId = runId,
                                                                                                                            Exclusionary = mainRequest.Exclusionary
                                                                                                                        });

                        if (sgResponse.Status == SyncStatus.SecurityGroupNotFound || sgResponse.Status == SyncStatus.NestedGroupsFound)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = sgResponse.Status });
                            await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = sgResponse.Status, ResultStatus = ResultStatus.Success, RunId = runId });
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
                        _ = _log.LogMessageAsync(new LogMessage { Message = $"Rescheduling job at {syncJob.StartDate} due to Graph API timeout.", RunId = runId });
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = runId });
                        return;
                    }

                    _ = _log.LogMessageAsync(new LogMessage { Message = $"Caught unexpected exception in Part# {mainRequest.CurrentPart}, marking sync job as errored. Exception:\n{ex}", RunId = runId });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });

                    // make sure this gets thrown to where App Insights will handle it
                    throw;
                }
                finally
                {
                    _log.RemoveSyncJobProperties(runId);
                }

                if (!context.IsReplaying)
                    _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId, DynamicProperties = syncJob.ToDictionary() }, VerbosityLevel.DEBUG);
            }
        }
    }
}