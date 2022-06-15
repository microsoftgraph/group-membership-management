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

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterSubOrchestratorFunction
    {
        private readonly TelemetryClient _telemetryClient;

        public GroupUpdaterSubOrchestratorFunction(TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(GroupUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var skip = 0;
            const int batchSize = 100;
            var request = context.GetInput<GroupUpdaterRequest>();
            var totalSuccessCount = 0;

            if (request == null)
            {
                return;
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function started", SyncJob = request.SyncJob, Verbosity = VerbosityLevel.DEBUG });

            var batch = request.Members?.Skip(skip).Take(batchSize).ToList() ?? new List<AzureADUser>();

            while (batch.Count > 0)
            {
                totalSuccessCount += await context.CallActivityAsync<int>(nameof(GroupUpdaterFunction),
                                           new GroupUpdaterRequest
                                           {
                                               SyncJob = request.SyncJob,
                                               Members = batch,
                                               Type = request.Type,
                                               IsInitialSync = request.IsInitialSync
                                           });

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount}/{request.Members.Count} users so far.",
                                                    SyncJob = request.SyncJob
                                                });


                skip += batchSize;
                batch = request.Members.Skip(skip).Take(batchSize).ToList();
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
        }
    }
}