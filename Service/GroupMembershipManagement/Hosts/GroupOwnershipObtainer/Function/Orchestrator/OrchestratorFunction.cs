// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Models;
using Repositories.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class OrchestratorFunction
    {
        private readonly IConfiguration _configuration;

        public OrchestratorFunction(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
         [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            var syncJob = mainRequest.SyncJob;

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });

            try
            {
                if (mainRequest.CurrentPart <= 0 || mainRequest.TotalParts <= 0)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    SyncJob = syncJob,
                                                    Message = $"Found invalid value for CurrentPart or TotalParts"
                                                });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = mainRequest.SyncJob.RunId });
                    return;
                }

                var queryParts = JsonNode.Parse(syncJob.Query).AsArray();
                var currentPart = queryParts[mainRequest.CurrentPart - 1];
                var sources = currentPart["source"].AsArray().Where(x => x != null).Select(x => x.GetValue<string>().Trim()).Distinct().ToHashSet();

                if (!sources.Any())
                {
                    await context.CallActivityAsync(
                           nameof(LoggerFunction),
                           new LoggerRequest
                           {
                               SyncJob = syncJob,
                               Message = $"The job RowKey:{syncJob.RowKey} Part#{mainRequest.CurrentPart} does not have a valid query!",
                           });

                    await context.CallActivityAsync(
                               nameof(JobStatusUpdaterFunction),
                               new JobStatusUpdaterRequest
                               {
                                   SyncJob = syncJob,
                                   Status = SyncStatus.QueryNotValid
                               });

                    await context.CallActivityAsync(
                        nameof(TelemetryTrackerFunction),
                        new TelemetryTrackerRequest
                        {
                            JobStatus = SyncStatus.QueryNotValid,
                            ResultStatus = ResultStatus.Failure,
                            RunId = syncJob.RunId
                        });

                    return;
                }

                else
                {
                    try
                    {
                        var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { Query = currentPart.ToString(), RunId = syncJob.RunId });
                        if (!hasValidJson)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob });
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        await context.CallActivityAsync(nameof(LoggerFunction),
                                new LoggerRequest
                                {
                                    SyncJob = syncJob,
                                    Message = $"Source query is not valid for job:{syncJob.Id}"
                                });

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob });
                        return;
                    }
                }
                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), syncJob);
                if (groupId.Equals(Guid.Empty))
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { SyncJob = syncJob, Message = $"Unable to get group id for job:{syncJob.Id}" });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob });
                    return;
                }
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { SyncJob = syncJob, Message = $"Group Id for job:{syncJob.Id} is {groupId}" });
                var syncJobs = new List<SyncJob>();
                var segmentResponse = await context.CallActivityAsync<List<SyncJob>>(nameof(GetJobsSegmentedFunction), new GetJobsSegmentedRequest { RunId = syncJob.RunId });
                syncJobs.AddRange(segmentResponse);

                var filteredJobs = await context.CallActivityAsync<List<Guid>>(nameof(JobsFilterFunction),
                                                                               new JobsFilterRequest
                                                                               {
                                                                                   RunId = syncJob.RunId,
                                                                                   RequestedSources = sources,
                                                                                   SyncJobs = syncJobs.Select(x => new JobsFilterSyncJob
                                                                                   {
                                                                                       Query = x.Query,
                                                                                       TargetOfficeGroupId = groupId
                                                                                   }).ToList()
                                                                               });

                if (!filteredJobs.Any())
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                   new LoggerRequest
                                                   {
                                                       SyncJob = syncJob,
                                                       Message = $"There are no jobs matching the requested sources {string.Join(",", sources)}",
                                                       Verbosity = VerbosityLevel.DEBUG
                                                   });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.MembershipDataNotFound });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.MembershipDataNotFound, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    SyncJob = syncJob,
                                                    Message = $"{nameof(OrchestratorFunction)} number of jobs in the syncJobs List: {filteredJobs.Count}",
                                                    Verbosity = VerbosityLevel.DEBUG
                                                });

                var owners = new List<Guid>();
                foreach (var idChunck in filteredJobs.Chunk(5))
                {
                    var ownerRetrievalTasks = GenerateOwnerRetrievalTasks(context, idChunck, syncJob);
                    var ownerResults = await Task.WhenAll(ownerRetrievalTasks);
                    owners.AddRange(ownerResults.SelectMany(x => x));
                }

                var filePath = await context.CallActivityAsync<string>(nameof(UsersSenderFunction),
                                                                       new UsersSenderRequest
                                                                       {
                                                                           SyncJob = syncJob,
                                                                           GroupId = groupId,
                                                                           Users = owners,
                                                                           CurrentPart = mainRequest.CurrentPart,
                                                                           Exclusionary = mainRequest.Exclusionary
                                                                       });

                var content = new MembershipAggregatorHttpRequest
                {
                    FilePath = filePath,
                    PartNumber = mainRequest.CurrentPart,
                    PartsCount = mainRequest.TotalParts,
                    SyncJob = mainRequest.SyncJob
                };


                await context.CallActivityAsync(nameof(QueueMessageSenderFunction), content);
            }
            catch (Exception ex)
            {
                var message = $"Caught unexpected exception in Part# {mainRequest.CurrentPart}, marking sync job as errored. Exception:\n{ex}";
                var status = SyncStatus.Error;

                if (ex.GetType() == typeof(JsonException) || ex.GetType().Name == "JsonReaderException")
                {
                    message = $"The job RowKey:{syncJob.RowKey} Part#{mainRequest.CurrentPart} does not have a valid query!";
                    status = SyncStatus.QueryNotValid;
                }

                if (ex.Message != null && ex.Message.Contains("The request timed out"))
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    message = $"Rescheduling job at {syncJob.StartDate} due to Graph API timeout at Part#{mainRequest.CurrentPart}.";
                    status = SyncStatus.Idle;
                }

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { SyncJob = syncJob, Message = message });
                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = status });

                if (status != SyncStatus.Idle)
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                                    new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", SyncJob = syncJob, Verbosity = VerbosityLevel.DEBUG });
        }

        private List<Task<List<Guid>>> GenerateOwnerRetrievalTasks(IDurableOrchestrationContext context, Guid[] groupIds, SyncJob syncJob)
        {
            var tasks = new List<Task<List<Guid>>>();
            foreach (var groupId in groupIds)
            {
                var task = context.CallActivityAsync<List<Guid>>(nameof(GetGroupOwnersFunction),
                                       new GetGroupOwnersRequest
                                       {
                                           GroupId = groupId,
                                           SyncJob = syncJob
                                       });

                tasks.Add(task);
            }

            return tasks;
        }
    }
}
