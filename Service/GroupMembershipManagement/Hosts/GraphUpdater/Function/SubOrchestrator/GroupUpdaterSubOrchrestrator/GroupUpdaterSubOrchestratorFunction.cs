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
using Models;

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
        public async Task<GroupUpdaterSubOrchestratorResponse> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var skip = 0;
            var request = context.GetInput<GroupUpdaterRequest>();
            var totalSuccessCount = 0;
            var allUsersNotFound = new List<AzureADUser>();

            if (request == null)
            {
                return new GroupUpdaterSubOrchestratorResponse();
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function started with batch size {_batchSize}", SyncJob = request.SyncJob, Verbosity = VerbosityLevel.INFO });

            var batch = request.Members?.Skip(skip).Take(_batchSize).ToList() ?? new List<AzureADUser>();

            while (batch.Count > 0)
            {
                var response = await context.CallActivityAsync<GroupUpdaterResponse>(nameof(GroupUpdaterFunction),
                                           new GroupUpdaterRequest
                                           {
                                               SyncJob = request.SyncJob,
                                               Members = batch,
                                               Type = request.Type,
                                               IsInitialSync = request.IsInitialSync
                                           });
                totalSuccessCount += response.SuccessCount;
                allUsersNotFound.AddRange(response.UsersNotFound);

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount}/{request.Members.Count} users so far.",
                                                    SyncJob = request.SyncJob
                                                });


                skip += _batchSize;
                batch = request.Members.Skip(skip).Take(_batchSize).ToList();
            }
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

            return new GroupUpdaterSubOrchestratorResponse()
            {
                Type = request.Type,
                SuccessCount = totalSuccessCount,
                UsersNotFound = allUsersNotFound
            };
        }
    }
}