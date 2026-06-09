// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
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

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var mainRequest = context.GetInput<OrchestratorRequest>();
            var syncJob = mainRequest.SyncJob;

            var logger = context.CreateReplaySafeLogger("GroupOwnershipObtainer.OrchestratorFunction");
            using var scope = logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = mainRequest.CurrentPart,
                ["TotalParts"] = mainRequest.TotalParts
            });

            logger.FunctionStarted(nameof(OrchestratorFunction));

            try
            {
                if (mainRequest.CurrentPart <= 0 || mainRequest.TotalParts <= 0)
                {
                    logger.InvalidCurrentOrTotalPart();

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts, JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure });
                    return;
                }

                var queryParts = JsonNode.Parse(syncJob.Query).AsArray();
                var currentPart = queryParts[mainRequest.CurrentPart - 1];
                var sources = currentPart["source"].AsArray().Where(x => x != null).Select(x => x.GetValue<string>().Trim()).Distinct().ToHashSet();

                if (!sources.Any())
                {
                    logger.JobQueryNotValid(syncJob.Id, mainRequest.CurrentPart);

                    await context.CallActivityAsync(
                               nameof(JobStatusUpdaterFunction),
                               new JobStatusUpdaterRequest
                               {
                                   SyncJob = syncJob,
                                   Status = SyncStatus.QueryNotValid,
                                   CurrentPart = mainRequest.CurrentPart,
                                   TotalParts = mainRequest.TotalParts
                               });

                    await context.CallActivityAsync(
                        nameof(TelemetryTrackerFunction),
                        new TelemetryTrackerRequest
                        {
                            SyncJob = syncJob,
                            CurrentPart = mainRequest.CurrentPart,
                            TotalParts = mainRequest.TotalParts,
                            JobStatus = SyncStatus.QueryNotValid,
                            ResultStatus = ResultStatus.Failure
                        });

                    return;
                }

                else
                {
                    try
                    {
                        var hasValidJson = await context.CallActivityAsync<bool>(nameof(SchemaValidatorFunction), new SchemaValidatorRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts, Query = currentPart.ToString() });
                        if (!hasValidJson)
                        {
                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.SchemaError, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        logger.SourceQueryNotValid(syncJob.Id);

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.QueryNotValid, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                        return;
                    }
                }
                var groupId = await context.CallActivityAsync<Guid>(nameof(GetGroupFunction), new GetGroupRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                if (groupId.Equals(Guid.Empty))
                {
                    logger.UnableToGetGroupId(syncJob.Id);
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.Error, SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                    return;
                }
                logger.GroupIdRetrieved(syncJob.Id, groupId);
                var segmentResponse = await context.CallActivityAsync<List<SyncJob>>(nameof(GetJobsSegmentedFunction), new GetJobsSegmentedRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });

                var groupDestinationSyncJobs = segmentResponse.Where(x => x.MembershipType == MembershipTypes.GroupMembership.ToString()).ToList();

                var filteredJobs = await context.CallActivityAsync<List<Guid>>(nameof(JobsFilterFunction),
                                                                               new JobsFilterRequest
                                                                               {
                                                                                   SyncJob = syncJob,
                                                                                   CurrentPart = mainRequest.CurrentPart,
                                                                                   TotalParts = mainRequest.TotalParts,
                                                                                   RequestedSources = sources,
                                                                                   SyncJobs = groupDestinationSyncJobs.Select(x => new JobsFilterSyncJob
                                                                                   {
                                                                                       Query = x.Query,
                                                                                       TargetOfficeGroupId = x.Group.GroupId
                                                                                   }).ToList()
                                                                               });

                if (!filteredJobs.Any())
                {
                    logger.NoJobsMatchingRequestedSources(string.Join(",", sources));

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.MembershipDataNotFound, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts, JobStatus = SyncStatus.MembershipDataNotFound, ResultStatus = ResultStatus.Failure });
                    return;
                }

                logger.OrchestratorJobCount(nameof(OrchestratorFunction), filteredJobs.Count);

                var owners = new List<Guid>();
                foreach (var idChunck in filteredJobs.Chunk(5))
                {
                    var ownerRetrievalTasks = GenerateOwnerRetrievalTasks(context, idChunck, syncJob, mainRequest);
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
                                                                           TotalParts = mainRequest.TotalParts,
                                                                           Exclusionary = mainRequest.Exclusionary
                                                                       });

                var content = new MembershipAggregatorHttpRequest
                {
                    FilePath = filePath,
                    PartNumber = mainRequest.CurrentPart,
                    PartsCount = mainRequest.TotalParts,
                    SyncJob = mainRequest.SyncJob,
                    IsDestinationPart = false
                };


                await context.CallActivityAsync(nameof(QueueMessageSenderFunction), content);
            }
            catch (Exception ex)
            {
                var status = SyncStatus.Error;
                var isTimeout = ex.Message != null && ex.Message.Contains("The request timed out");
                var isJsonException = ex.GetType() == typeof(JsonException) || ex.GetType().Name == "JsonReaderException";

                if (isTimeout)
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    logger.ReschedulingJobDueToTimeout(syncJob.StartDate, mainRequest.CurrentPart);
                    status = SyncStatus.Idle;
                }
                else if (isJsonException)
                {
                    logger.JobQueryNotValid(syncJob.Id, mainRequest.CurrentPart);
                    status = SyncStatus.QueryNotValid;
                }
                else
                {
                    logger.OrchestratorException(ex, mainRequest.CurrentPart);
                }

                await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = status, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts });

                if (status != SyncStatus.Idle)
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction),
                                                    new TelemetryTrackerRequest { SyncJob = syncJob, CurrentPart = mainRequest.CurrentPart, TotalParts = mainRequest.TotalParts, JobStatus = status, ResultStatus = ResultStatus.Failure });
            }

            logger.FunctionCompleted(nameof(OrchestratorFunction));
        }

        private List<Task<List<Guid>>> GenerateOwnerRetrievalTasks(TaskOrchestrationContext context, Guid[] groupIds, SyncJob syncJob, OrchestratorRequest mainRequest)
        {
            var tasks = new List<Task<List<Guid>>>();
            foreach (var groupId in groupIds)
            {
                var task = context.CallActivityAsync<List<Guid>>(nameof(GetGroupOwnersFunction),
                                       new GetGroupOwnersRequest
                                       {
                                           GroupId = groupId,
                                           SyncJob = syncJob,
                                           CurrentPart = mainRequest.CurrentPart,
                                           TotalParts = mainRequest.TotalParts
                                       });

                tasks.Add(task);
            }

            return tasks;
        }
    }
}
