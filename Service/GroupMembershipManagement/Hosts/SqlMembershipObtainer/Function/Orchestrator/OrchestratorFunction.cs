// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Models;
using Repositories.Contracts.Helpers;
using SqlMembershipObtainer.Entities;
using SqlMembershipObtainer.SubOrchestrator;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class OrchestratorFunction
    {
        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            if (mainRequest == null || mainRequest.SyncJob == null) { return; }
            var syncJob = mainRequest.SyncJob;
            var currentPart = mainRequest.CurrentPart;
            var totalParts = mainRequest.TotalParts;

            var logger = context.CreateReplaySafeLogger("SqlMembershipObtainer.OrchestratorFunction");
            using var scope = logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = currentPart,
                ["TotalParts"] = totalParts
            });

            logger.FunctionStarted(nameof(OrchestratorFunction));

            try
            {
                var queryParts = JsonNode.Parse(syncJob.Query).AsArray();
                var currentQueryPart = queryParts[currentPart - 1];
                var currentQuery = currentQueryPart.AsObject()["source"];
                var currentQueryAsString = Convert.ToString(currentQuery);

                if (string.IsNullOrWhiteSpace(currentQueryAsString))
                {
                    logger.QueryNotValid(syncJob.Id, currentPart);

                    await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = SyncStatus.QueryNotValid,
                                    CurrentPart = currentPart,
                                    TotalParts = totalParts
                                });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                    return;
                }

                else
                {
                    try
                    {
                        var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { Query = currentQueryPart.ToString(), SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                        if (!hasValidJson)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        logger.SourceQueryNotValid(syncJob.Id);

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                        return;
                    }
                }

                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), new GetGroupRequest { SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                if (groupId.Equals(Guid.Empty))
                {
                    logger.UnableToGetGroupId(syncJob.Id);
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                    return;
                }

                logger.GroupIdRetrieved(syncJob.Id, groupId);
                var query = JsonSerializer.Deserialize<Query>(currentQueryAsString);

                var senderResponse = await context.CallSubOrchestratorAsync<MembershipFileResult>(
                            nameof(OrganizationProcessorFunction),
                            new OrganizationProcessorRequest
                            {
                                Query = query,
                                SyncJob = syncJob,
                                GroupId = groupId,
                                CurrentPart = currentPart,
                                TotalParts = totalParts,
                                Exclusionary = mainRequest.Exclusionary
                            });

                if (senderResponse.Status != SyncStatus.InProgress)
                {
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = senderResponse.Status, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(senderResponse.FilePath))
                {
                    var content = new MembershipAggregatorHttpRequest
                    {
                        FilePath = senderResponse.FilePath,
                        PartNumber = currentPart,
                        PartsCount = totalParts,
                        SyncJob = mainRequest.SyncJob,
                        IsDestinationPart = false
                    };

                    await context.CallActivityAsync(nameof(QueueMessageSenderFunction), content);
                }
                else
                {
                    logger.FilePathNotValid(SyncStatus.FilePathNotValid.ToString());

                    await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = SyncStatus.FilePathNotValid,
                                    CurrentPart = currentPart,
                                    TotalParts = totalParts
                                });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.FilePathNotValid, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                }

            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.ServiceUnavailable || ex.ResponseStatusCode == (int)HttpStatusCode.BadGateway)
            {
                syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                var httpStatus = ex.ResponseStatusCode == (int)HttpStatusCode.ServiceUnavailable ? "Service Unavailable" : "Bad Gateway";
                logger.ReschedulingJob(syncJob.StartDate, httpStatus);
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle, CurrentPart = currentPart, TotalParts = totalParts });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                return;
            }
            catch (SqlException sqlEx)
            {
                logger.SqlExceptionCaught(sqlEx);
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error, CurrentPart = currentPart, TotalParts = totalParts });
                throw;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                var status = SyncStatus.Error;

                if (ex.GetType() == typeof(System.Text.Json.JsonException) || ex.GetType().Name == "JsonReaderException")
                {
                    message = $"The job Id:{syncJob.Id} Part#{currentPart} does not have a valid query!";
                    status = SyncStatus.QueryNotValid;
                }

                if (message.Contains("Internal .NET Framework Data Provider error 6")
                    && ((context.CurrentUtcDateTime - syncJob.LastSuccessfulRunTime).TotalHours < syncJob.Period + 2)
                    )
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    logger.ReschedulingJob(syncJob.StartDate, "Internal .NET Framework Data Provider error 6");
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle, CurrentPart = currentPart, TotalParts = totalParts });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
                    return;
                }

                logger.OrchestratorFailed(nameof(OrchestratorFunction), message);

                await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = status,
                                    CurrentPart = currentPart,
                                    TotalParts = totalParts
                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = status, ResultStatus = ResultStatus.Failure, SyncJob = syncJob, CurrentPart = currentPart, TotalParts = totalParts });
            }

            logger.FunctionCompleted(nameof(OrchestratorFunction));
        }
    }
}