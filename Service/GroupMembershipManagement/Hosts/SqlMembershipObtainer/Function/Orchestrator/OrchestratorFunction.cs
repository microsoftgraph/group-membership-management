// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SqlMembershipObtainer.Entities;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Net;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Primitives;
using Repositories.Contracts;
using Microsoft.Graph;
using Models;
using SqlMembershipObtainer.SubOrchestrator;
using Microsoft.Data.SqlClient;

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

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context, ExecutionContext executionContext)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            if (mainRequest == null || mainRequest.SyncJob == null) { return; }
            var syncJob = mainRequest.SyncJob;

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });

            try
            {
                var queryParts = JArray.Parse(syncJob.Query);
                var currentPart = queryParts[mainRequest.CurrentPart - 1];
                var currentQuery = currentPart.Value<JObject>("source");
                var currentQueryAsString = Convert.ToString(currentQuery);

                if (string.IsNullOrWhiteSpace(currentQueryAsString) || currentQueryAsString.Contains("ids"))
                {
                    await context.CallActivityAsync(
                           nameof(LoggerFunction),
                           new LoggerRequest
                           {
                               SyncJob = syncJob,
                               Message = $"The job Id:{syncJob.Id} Part#{mainRequest.CurrentPart} does not have a valid query!",
                           });

                    await context.CallActivityAsync(
                               nameof(JobStatusUpdaterFunction),
                               new JobStatusUpdaterRequest
                               {
                                   SyncJob = syncJob,
                                   Status = SyncStatus.QueryNotValid
                               });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }

                var query = JsonConvert.DeserializeObject<Query>(currentQueryAsString);
                var graphProfilesResponse = await context.CallSubOrchestratorAsync<GraphProfileInformationResponse>(
                            nameof(OrganizationProcessorFunction),
                            new OrganizationProcessorRequest
                            {
                                Query = query,
                                SyncJob = syncJob
                            });

                await context.CallActivityAsync(
                               nameof(LoggerFunction),
                               new LoggerRequest
                               {
                                   SyncJob = syncJob,
                                   Message = $"Retrieved {graphProfilesResponse.GraphProfileCount} total profiles from SqlMembershipObtainer",
                               });

                var senderResponse = await context.CallActivityAsync<(SyncStatus Status, string FilePath)>(
                                    nameof(GroupMembershipSenderFunction),
                                    new GroupMembershipSenderRequest
                                    {
                                        SyncJob = syncJob,
                                        Profiles = graphProfilesResponse.GraphProfiles,
                                        CurrentPart = mainRequest.CurrentPart,
                                        Exclusionary = mainRequest.Exclusionary,
                                        AdaptiveCardTemplateDirectory = executionContext.FunctionAppDirectory
                                    });

                if (senderResponse.Status != SyncStatus.InProgress)
                {
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = senderResponse.Status, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(senderResponse.FilePath))
                {
                    var content = new MembershipAggregatorHttpRequest
                    {
                        FilePath = senderResponse.FilePath,
                        PartNumber = mainRequest.CurrentPart,
                        PartsCount = mainRequest.TotalParts,
                        SyncJob = mainRequest.SyncJob
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
                        });

                    await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = SyncStatus.FilePathNotValid
                                });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.FilePathNotValid, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
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
                });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                return;
            }
            catch (SqlException sqlEx)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Caught SqlException, marking sync job as errored. Exception:\n{sqlEx}", SyncJob = syncJob });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                throw;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                var status = SyncStatus.Error;

                if (ex.GetType() == typeof(JsonReaderException))
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
                    });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = syncJob.RunId });
                    return;
                }

                await context.CallActivityAsync(
                                 nameof(LoggerFunction),
                                 new LoggerRequest
                                 {
                                     SyncJob = syncJob,
                                     Message = $"{nameof(OrchestratorFunction)} failed\n {message}",
                                 });

                await context.CallActivityAsync(
                                nameof(JobStatusUpdaterFunction),
                                new JobStatusUpdaterRequest
                                {
                                    SyncJob = syncJob,
                                    Status = status
                                });
                await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = status, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
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