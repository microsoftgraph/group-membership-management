// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using SqlMembershipObtainer.Entities;
using SqlMembershipObtainer.SubOrchestrator;
using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class OrchestratorFunction
    {
        private readonly IConfiguration _configuration;
        private readonly ILoggingRepository _loggingRepository;
        public OrchestratorFunction(IConfiguration configuration, ILoggingRepository loggingRepository)
        {
            _configuration = configuration;
            _loggingRepository = loggingRepository;
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            if (mainRequest == null || mainRequest.SyncJob == null) { return; }
            var syncJob = mainRequest.SyncJob;            

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });

            try
            {
                var queryParts = JsonNode.Parse(syncJob.Query).AsArray();
                var currentPart = queryParts[mainRequest.CurrentPart - 1];
                var currentQuery = currentPart.AsObject()["source"];
                var currentQueryAsString = Convert.ToString(currentQuery);

                if (string.IsNullOrWhiteSpace(currentQueryAsString))
                {
                    await context.CallActivityAsync(
                           nameof(LoggerFunction),
                           new LoggerRequest
                           {
                               SyncJob = syncJob,
                               Message = $"The job Id:{syncJob.Id} Part#{mainRequest.CurrentPart} does not have a valid query!",
                               Verbosity = VerbosityLevel.INFO
                           });

                    await context.CallActivityAsync(
                               nameof(JobStatusUpdaterFunction),
                               new JobStatusUpdaterRequest
                               {
                                   SyncJob = syncJob,
                                   Status = SyncStatus.QueryNotValid
                               });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId ?? Guid.Empty });
                    return;
                }

                else
                {
                    try
                    {
                        var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { Query = currentPart.ToString(), RunId = syncJob.RunId ?? Guid.Empty });
                        if (!hasValidJson)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob });
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        await context.CallActivityAsync(nameof(LoggerFunction),
                                new LoggerRequest
                                {
                                    SyncJob = syncJob,
                                    Message = $"Source query is not valid for job:{syncJob.Id}",
                                    Verbosity = VerbosityLevel.INFO
                                });

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                        return;
                    }
                }

                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), syncJob);
                if (groupId.Equals(Guid.Empty))
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Unable to get group id for job:{syncJob.Id}", SyncJob = syncJob, Verbosity = VerbosityLevel.INFO });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId ?? Guid.Empty });
                    return;
                }

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Group Id for job:{syncJob.Id} is {groupId}", SyncJob = syncJob, Verbosity = VerbosityLevel.INFO });
                var query = JsonSerializer.Deserialize<Query>(currentQueryAsString);

                var senderResponse = await context.CallSubOrchestratorAsync<MembershipFileResult>(
                            nameof(OrganizationProcessorFunction),
                            new OrganizationProcessorRequest
                            {
                                Query = query,
                                SyncJob = syncJob,
                                GroupId = groupId,
                                CurrentPart = mainRequest.CurrentPart,
                                Exclusionary = mainRequest.Exclusionary
                            });

                if (senderResponse.Status != SyncStatus.InProgress)
                {
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = senderResponse.Status, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId ?? Guid.Empty });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(senderResponse.FilePath))
                {
                    var content = new MembershipAggregatorHttpRequest
                    {
                        FilePath = senderResponse.FilePath,
                        PartNumber = mainRequest.CurrentPart,
                        PartsCount = mainRequest.TotalParts,
                        SyncJob = mainRequest.SyncJob,
                        IsDestinationPart = false
                    };

                    await context.CallActivityAsync(nameof(QueueMessageSenderFunction), content);
                }
                else
                {
                    await context.CallActivityAsync(
                        nameof(LoggerFunction),
                        new LoggerRequest
                        {
                            SyncJob = syncJob,
                            Message = $"Membership file path is not valid, marking sync job as {SyncStatus.FilePathNotValid}.",
                            Verbosity = VerbosityLevel.INFO
                        });

                    await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = SyncStatus.FilePathNotValid
                                });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.FilePathNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId ?? Guid.Empty });
                }

            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.ServiceUnavailable || ex.ResponseStatusCode == (int)HttpStatusCode.BadGateway)
            {
                syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                var httpStatus = ex.ResponseStatusCode == (int)HttpStatusCode.ServiceUnavailable ? "Service Unavailable" : "Bad Gateway";
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                {
                    SyncJob = syncJob,
                    Message = $"Rescheduling job at {syncJob.StartDate} due to {httpStatus} exception",
                    Verbosity = VerbosityLevel.INFO
                });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId ?? Guid.Empty });
                return;
            }
            catch (SqlException sqlEx)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Caught SqlException, marking sync job as errored. Exception:\n{sqlEx}", SyncJob = syncJob, Verbosity = VerbosityLevel.INFO });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                throw;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                var status = SyncStatus.Error;

                if (ex.GetType() == typeof(System.Text.Json.JsonException) || ex.GetType().Name == "JsonReaderException")
                {
                    message = $"The job Id:{syncJob.Id} Part#{mainRequest.CurrentPart} does not have a valid query!";
                    status = SyncStatus.QueryNotValid;
                }

                if (message.Contains("Internal .NET Framework Data Provider error 6")
                    && ((context.CurrentUtcDateTime - syncJob.LastSuccessfulRunTime).TotalHours < syncJob.Period + 2)
                    )
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest
                    {
                        SyncJob = syncJob,
                        Message = $"Rescheduling job at {syncJob.StartDate} due to Internal.NET Framework Data Provider error 6 exception",
                        Verbosity = VerbosityLevel.INFO
                    });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId ?? Guid.Empty });
                    return;
                }

                await context.CallActivityAsync(
                                 nameof(LoggerFunction),
                                 new LoggerRequest
                                 {
                                     SyncJob = syncJob,
                                     Message = $"{nameof(OrchestratorFunction)} failed\n {message}",
                                     Verbosity = VerbosityLevel.INFO
                                 });

                await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = status
                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = status, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId ?? Guid.Empty });
            }
            finally
            {
                if (syncJob != null && syncJob.RunId.HasValue)
                    _loggingRepository.RemoveSyncJobProperties(syncJob.RunId.Value);
            }

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });
        }
    }
}