// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Graph;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IConfiguration _configuration;
        private readonly SGMembershipCalculator _calculator;

        public OrchestratorFunction(
            ILoggingRepository loggingRepository,
            SGMembershipCalculator calculator,
            IConfiguration configuration)
        {
            _log = loggingRepository;
            _calculator = calculator;
            _configuration = configuration;
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            var syncJob = mainRequest.SyncJob;
            var runId = syncJob.RunId.GetValueOrDefault(Guid.Empty);
            List<AzureADUser> distinctUsers = null;

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
                var sourceGroup = await context.CallActivityAsync<AzureADGroup>(nameof(SourceGroupsReaderFunction),
                                                                                    new SourceGroupsReaderRequest
                                                                                    {
                                                                                        SyncJob = syncJob,
                                                                                        CurrentPart = mainRequest.CurrentPart,
                                                                                        IsDestinationPart = mainRequest.IsDestinationPart,
                                                                                        RunId = runId
                                                                                    });

                if (sourceGroup.ObjectId == Guid.Empty)
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = runId, Message = $"Source group id is not a valid, Part# {mainRequest.CurrentPart} {syncJob.Query}. Marking job as {SyncStatus.QueryNotValid}." });
                    await context.CallActivityAsync(nameof(EmailSenderFunction), new EmailSenderRequest { SyncJob = syncJob, RunId = runId });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.QueryNotValid });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.QueryNotValid, ResultStatus = ResultStatus.Failure, RunId = runId });
                    return;
                }
                else
                {
                    var sgResponse = await context.CallSubOrchestratorAsync<(List<AzureADUser> Users, SyncStatus Status)>(nameof(SubOrchestratorFunction),
                                                                                                                    new SecurityGroupRequest
                                                                                                                    {
                                                                                                                        SyncJob = syncJob,
                                                                                                                        SourceGroup = sourceGroup,
                                                                                                                        RunId = runId
                                                                                                                    });

                    if (sgResponse.Status == SyncStatus.SecurityGroupNotFound)
                    {
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.SecurityGroupNotFound });
                        await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.SecurityGroupNotFound, ResultStatus = ResultStatus.Success, RunId = runId });
                        return;
                    }

                    distinctUsers = sgResponse.Users;

                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage
                    {
                        RunId = runId,
                        Message = $"Read {distinctUsers.Count} users from source groups {syncJob.Query} to be synced into the destination group {syncJob.TargetOfficeGroupId}."
                    });

                    var filePath = await context.CallActivityAsync<string>(nameof(UsersSenderFunction),
                                                                            new UsersSenderRequest
                                                                            {
                                                                                SyncJob = syncJob,
                                                                                RunId = runId,
                                                                                Users = distinctUsers,
                                                                                CurrentPart = mainRequest.CurrentPart,
                                                                                Exclusionary = mainRequest.Exclusionary
                                                                            });

                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = "Calling MembershipAggregator", RunId = runId });
                    var content = new MembershipAggregatorHttpRequest
                    {
                        FilePath = filePath,
                        PartNumber = mainRequest.CurrentPart,
                        PartsCount = mainRequest.TotalParts,
                        SyncJob = syncJob,
                        IsDestinationPart = mainRequest.IsDestinationPart
                    };

                    var request = new DurableHttpRequest(HttpMethod.Post,
                                                            new Uri(_configuration["membershipAggregatorUrl"]),
                                                            content: JsonConvert.SerializeObject(content),
                                                            headers: new Dictionary<string, StringValues> { { "x-functions-key", _configuration["membershipAggregatorFunctionKey"] } },
                                                            httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(30), 3));

                    var response = await context.CallHttpAsync(request);
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"MembershipAggregator response Code: {response.StatusCode}, Content: {response.Content}", RunId = runId });

                    if (response.StatusCode != HttpStatusCode.NoContent)
                    {
                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                        if (!context.IsReplaying) await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });
                    }
                }
            }
            catch (ServiceException ex)
            {
                if ((ex.StatusCode == HttpStatusCode.ServiceUnavailable || ex.StatusCode == HttpStatusCode.BadGateway)
                    && ((context.CurrentUtcDateTime - syncJob.LastSuccessfulRunTime).TotalHours < syncJob.Period + 2))
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    var httpStatus = Enum.GetName(ex.StatusCode);

                    _ = _log.LogMessageAsync(new LogMessage { Message = $"Rescheduling job at {syncJob.StartDate} due to {httpStatus} exception", RunId = runId });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                    if (!context.IsReplaying) await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = runId });
                    return;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != null && ex.Message.Contains("The request timed out"))
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    _ = _log.LogMessageAsync(new LogMessage { Message = $"Rescheduling job at {syncJob.StartDate} due to Graph API timeout.", RunId = runId });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                    if (!context.IsReplaying) await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Idle, ResultStatus = ResultStatus.Success, RunId = runId });
                    return;
                }

                _ = _log.LogMessageAsync(new LogMessage { Message = $"Caught unexpected exception in Part# {mainRequest.CurrentPart}, marking sync job as errored. Exception:\n{ex}", RunId = runId });

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                if (!context.IsReplaying) await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = runId });

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