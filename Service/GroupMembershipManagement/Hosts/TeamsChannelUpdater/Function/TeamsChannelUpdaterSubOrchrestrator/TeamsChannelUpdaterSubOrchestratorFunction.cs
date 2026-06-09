// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.ApplicationInsights;
using Models.Entities;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;

namespace Hosts.TeamsChannelUpdater
{
    public class TeamsChannelUpdaterSubOrchestratorFunction
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly int _batchSize = 100;

        public TeamsChannelUpdaterSubOrchestratorFunction(TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [Function(nameof(TeamsChannelUpdaterSubOrchestratorFunction))]
        public async Task<TeamsChannelUpdaterSubOrchestratorResponse> RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var skip = 0;
            var request = context.GetInput<TeamsChannelUpdaterSubOrchestratorRequest>();
            var totalSuccessCount = 0;
            var allUsersNotFound = new List<AzureADTeamsUser>();

            if (request == null)
            {
                return new TeamsChannelUpdaterSubOrchestratorResponse();
            }

            var logger = context.CreateReplaySafeLogger("TeamsChannelUpdater.TeamsChannelUpdaterSubOrchestratorFunction");
            using var scope = logger.BeginSyncJobScope(request.SyncJob);

            logger.SubOrchestratorStarted(nameof(TeamsChannelUpdaterSubOrchestratorFunction), _batchSize);

            var batch = request.Members?.Skip(skip).Take(_batchSize).ToList() ?? new List<AzureADTeamsUser>();

            var retryMembers = new List<AzureADTeamsUser>();

            // The orchestrator will stop trying to retry user operations once the retry count exceeds the original member count
            while (batch.Count > 0)
            {
                var response = await context.CallActivityAsync<TeamsUpdaterResponse>(nameof(TeamsUpdaterFunction),
                                           new TeamsUpdaterRequest
                                           {
                                               Type = request.Type,
                                               Members = batch,
                                               TeamsChannelInfo = request.TeamsChannelInfo,
                                               SyncJob = request.SyncJob
                                           });
                totalSuccessCount += response.SuccessCount;

                logger.BatchProgress(request.Type == RequestType.Add ? "Added" : "Removed", totalSuccessCount, request.Members.Count);

                skip += batch.Count;

                batch = request.Members.Skip(skip).Take(_batchSize).ToList();
                retryMembers.AddRange(response.UsersToRetry);
                allUsersNotFound.AddRange(response.UsersNotFound);
            }

            skip = 0;
            var retryBatch = retryMembers?.Skip(skip).Take(_batchSize).ToList() ?? new List<AzureADTeamsUser>();
            var userFailures = new List<AzureADTeamsUser>();

            if (retryBatch.Count > 0)
            {
                logger.RetryingUsers(retryBatch.Count);

                while (retryBatch.Count > 0)
                {
                    var response = await context.CallActivityAsync<TeamsUpdaterResponse>(nameof(TeamsUpdaterFunction),
                                               new TeamsUpdaterRequest
                                               {
                                                   Type = request.Type,
                                                   Members = batch,
                                                   TeamsChannelInfo = request.TeamsChannelInfo,
                                                   SyncJob = request.SyncJob
                                               });
                    totalSuccessCount += response.SuccessCount;

                    logger.BatchProgress(request.Type == RequestType.Add ? "Added" : "Removed", totalSuccessCount, request.Members.Count);

                    skip += retryBatch.Count;

                    retryBatch = retryMembers.Skip(skip).Take(_batchSize).ToList();
                    userFailures.AddRange(response.UsersToRetry);
                    allUsersNotFound.AddRange(response.UsersNotFound);
                }
            }

            logger.SubOrchestratorSummary(request.Type == RequestType.Add ? "Added" : "Removed", totalSuccessCount, allUsersNotFound.Count, userFailures.Count);

            if (!context.IsReplaying)
            {
                _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersNotFound), request.Members.Count - totalSuccessCount);

                if (request.IsInitialSync)
                {
                    if (request.Type == RequestType.Add)
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersAddedFromOnboarding), totalSuccessCount);
                    else
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersRemovedFromOnboarding), totalSuccessCount);
                }
                else
                {
                    if (request.Type == RequestType.Add)
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersAdded), totalSuccessCount);
                    else
                        _telemetryClient.TrackMetric(nameof(Repositories.TeamsChannel.Metric.TeamsMembersRemoved), totalSuccessCount);
                }
            }

            logger.FunctionCompleted(nameof(TeamsChannelUpdaterSubOrchestratorFunction));

            return new TeamsChannelUpdaterSubOrchestratorResponse()
            {
                Type = request.Type,
                SuccessCount = totalSuccessCount,
                UsersNotFound = allUsersNotFound,
                UsersFailed = userFailures
            };
        }
    }
}
