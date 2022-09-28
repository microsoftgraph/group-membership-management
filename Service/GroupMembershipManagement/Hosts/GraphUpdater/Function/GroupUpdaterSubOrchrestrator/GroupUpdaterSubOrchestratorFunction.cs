// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.ApplicationInsights;
using GraphUpdater.Entities;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterSubOrchestratorFunction
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly int _batchSize = 100;

        public GroupUpdaterSubOrchestratorFunction(TelemetryClient telemetryClient, GraphUpdaterBatchSize batchSize)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
            _batchSize = batchSize.BatchSize;
        }

        [FunctionName(nameof(GroupUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var skip = 0;
            var request = context.GetInput<GroupUpdaterRequest>();
            var totalSuccessCount = 0;
            var allUsersNotFound = new List<AzureADUser>();

            if (request == null)
            {
                return;
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function started with batch size {_batchSize}", SyncJob = request.SyncJob, Verbosity = VerbosityLevel.INFO });

            var batch = request.Members?.Skip(skip).Take(_batchSize).ToList() ?? new List<AzureADUser>();

            while (batch.Count > 0)
            {
                var response = await context.CallActivityAsync<(int successCount, List<AzureADUser> usersNotFound)>(nameof(GroupUpdaterFunction),
                                           new GroupUpdaterRequest
                                           {
                                               SyncJob = request.SyncJob,
                                               Members = batch,
                                               Type = request.Type,
                                               IsInitialSync = request.IsInitialSync
                                           });
                totalSuccessCount += response.successCount;
                allUsersNotFound.AddRange(response.usersNotFound);

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount}/{request.Members.Count} users so far.",
                                                    SyncJob = request.SyncJob
                                                });


                skip += _batchSize;
                batch = request.Members.Skip(skip).Take(_batchSize).ToList();
            }

            if (!context.IsReplaying & allUsersNotFound.Count > 0) { TrackUsersNotFoundEvent(request.SyncJob.RunId, allUsersNotFound.Count, request.SyncJob.TargetOfficeGroupId); }

            _telemetryClient.TrackMetric(nameof(Services.Entities.Metric.MembersNotFound), request.Members.Count - totalSuccessCount);

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                     new LoggerRequest
                                                     {
                                                         Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount} users.",
                                                         SyncJob = request.SyncJob
                                                     });

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                      new LoggerRequest
                                                      {
                                                          Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function completed",
                                                          SyncJob = request.SyncJob,
                                                          Verbosity = VerbosityLevel.DEBUG
                                                      });
        }

        private void TrackUsersNotFoundEvent(Guid? runId, int usersNotFoundCount, Guid groupId)
        {
            var usersNotFoundEvent = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() },
                { "TargetGroupId", groupId.ToString() },
                { "UsersNotFound", usersNotFoundCount.ToString() }
            };
            _telemetryClient.TrackEvent("UsersNotFoundCount", usersNotFoundEvent);
        }
    }
}