// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using GraphUpdater.Entities;
using Models;
using Services.Entities;

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

        [Function(nameof(GroupUpdaterSubOrchestratorFunction))]
        public async Task<GroupUpdaterSubOrchestratorResponse> RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("Hosts.GraphUpdater.GroupUpdaterSubOrchestratorFunction");
            var skip = 0;
            var request = context.GetInput<GroupUpdaterRequest>();
            using var scope = logger.BeginGraphUpdaterScope(request);
            var totalSuccessCount = 0;
            var allUsersNotFound = new List<AzureADUser>();
            var allUsersAlreadyExist = new List<AzureADUser>();
            var responseStatus = GraphUpdaterStatus.Ok;

            if (request == null)
            {
                return new GroupUpdaterSubOrchestratorResponse();
            }

            logger.SubOrchestratorStartedWithBatchSize(nameof(GroupUpdaterSubOrchestratorFunction), _batchSize);

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
                allUsersAlreadyExist.AddRange(response.UsersAlreadyExist);

                logger.GroupUpdateProgress(
                    request.Type == RequestType.Add ? "Added" : "Removed",
                    totalSuccessCount,
                    request.Members.Count);

                if(response.Status != GraphUpdaterStatus.Ok)
                {
                    responseStatus = response.Status;
                }
                skip += _batchSize;
                batch = request.Members.Skip(skip).Take(_batchSize).ToList();
            }
            _telemetryClient.TrackMetric(nameof(Services.Entities.Metric.MembersNotFound), request.Members.Count - totalSuccessCount);

            logger.GroupUpdateComplete(
                request.Type == RequestType.Add ? "Added" : "Removed",
                totalSuccessCount);

            logger.FunctionCompleted(nameof(GroupUpdaterSubOrchestratorFunction));

            return new GroupUpdaterSubOrchestratorResponse()
            {
                Status = responseStatus,
                Type = request.Type,
                SuccessCount = totalSuccessCount,
                UsersNotFound = allUsersNotFound,
                UsersAlreadyExist = allUsersAlreadyExist
            };
        }
    }
}
