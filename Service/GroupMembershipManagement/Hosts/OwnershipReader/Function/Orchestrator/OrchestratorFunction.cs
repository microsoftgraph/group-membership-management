// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Graph;
using Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.OwnershipReader
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
                                                    Message = $"Found invalid value for CurrentPart or TotalParts"
                                                });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = mainRequest.SyncJob.RunId });
                    return;
                }

                var queryParts = JArray.Parse(syncJob.Query);
                var currentPart = queryParts[mainRequest.CurrentPart - 1];
                var sources = currentPart.Value<JArray>("source").Values<string>()
                              .Where(x => x != null).Select(x => x.Trim()).ToHashSet();

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

                string query = null;
                string continuationToken = null;
                var syncJobs = new List<SyncJob>();

                do
                {
                    var segmentResponse = await context.CallActivityAsync<GetJobsSegmentedResponse>(nameof(GetJobsSegmentedFunction),
                                                                                                    new GetJobsSegmentedRequest
                                                                                                    {
                                                                                                        Query = query,
                                                                                                        ContinuationToken = continuationToken
                                                                                                    });

                    syncJobs.AddRange(segmentResponse.ResponsePage.Values);
                    query = segmentResponse.ResponsePage.Query;
                    continuationToken = segmentResponse.ResponsePage.ContinuationToken;

                } while (continuationToken != null);

                var filteredJobs = await context.CallActivityAsync<List<Guid>>(nameof(JobsFilterFunction),
                                                                               new JobsFilterRequest
                                                                               {
                                                                                   RunId = syncJob.RunId,
                                                                                   RequestedSources = sources,
                                                                                   SyncJobs = syncJobs.Select(x => new JobsFilterSyncJob
                                                                                   {
                                                                                       Query = x.Query,
                                                                                       TargetOfficeGroupId = x.TargetOfficeGroupId,
                                                                                   }).ToList()
                                                                               });

                if (!filteredJobs.Any())
                {
                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                   new LoggerRequest
                                                   {
                                                       Message = $"There are no jobs matching the requested sources {string.Join(",", sources)}",
                                                       Verbosity = VerbosityLevel.DEBUG
                                                   });

                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.CustomMembershipDataNotFound });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.CustomMembershipDataNotFound, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                    return;
                }

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
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
                                                                           Users = owners,
                                                                           CurrentPart = mainRequest.CurrentPart,
                                                                           Exclusionary = mainRequest.Exclusionary
                                                                       });

                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { SyncJob = syncJob, Message = $"Calling MembershipAggregator" });
                var content = new MembershipAggregatorHttpRequest
                {
                    FilePath = filePath,
                    PartNumber = mainRequest.CurrentPart,
                    PartsCount = mainRequest.TotalParts,
                    SyncJob = mainRequest.SyncJob
                };

                var request = new DurableHttpRequest(HttpMethod.Post,
                                    new Uri(_configuration["membershipAggregatorUrl"]),
                                    content: JsonConvert.SerializeObject(content),
                                    headers: new Dictionary<string, StringValues> { { "x-functions-key", _configuration["membershipAggregatorFunctionKey"] } },
                                    httpRetryOptions: new HttpRetryOptions(TimeSpan.FromSeconds(30), 3));

                var response = await context.CallHttpAsync(request);
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { SyncJob = syncJob, Message = $"MembershipAggregator response Code:{response.StatusCode}, Content: {response.Content}" });

                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Error });
                    await context.CallActivityAsync(nameof(TelemetryTrackerFunction), new TelemetryTrackerRequest { JobStatus = SyncStatus.Error, ResultStatus = ResultStatus.Failure, RunId = syncJob.RunId });
                }

            }
            catch (ServiceException ex)
            {
                if ((ex.StatusCode == HttpStatusCode.ServiceUnavailable || ex.StatusCode == HttpStatusCode.BadGateway)
                    && ((context.CurrentUtcDateTime - syncJob.LastSuccessfulRunTime).TotalHours < syncJob.Period + 2))
                {
                    syncJob.StartDate = context.CurrentUtcDateTime.AddMinutes(30);
                    var httpStatus = Enum.GetName(ex.StatusCode);
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { SyncJob = syncJob, Message = $"Rescheduling job at {syncJob.StartDate} due to {httpStatus} exception" });
                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { SyncJob = syncJob, Status = SyncStatus.Idle });
                    return;
                }
            }
            catch (Exception ex)
            {
                var message = $"Caught unexpected exception in Part# {mainRequest.CurrentPart}, marking sync job as errored. Exception:\n{ex}";
                var status = SyncStatus.Error;

                if (ex.GetType() == typeof(JsonReaderException))
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
