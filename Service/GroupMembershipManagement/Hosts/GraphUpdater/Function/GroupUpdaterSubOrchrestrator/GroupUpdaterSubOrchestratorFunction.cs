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
        private readonly ILoggingRepository _loggingRepository;
        private readonly TelemetryClient _telemetryClient;

        public GroupUpdaterSubOrchestratorFunction(ILoggingRepository loggingRepository, TelemetryClient telemetryClient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [FunctionName(nameof(GroupUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var skip = 0;
            var batchSize = 100;
            var request = context.GetInput<GroupUpdaterRequest>();
            var totalSuccessCount = 0;

            if (request == null)
            {
                return;
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function started", RunId = request.RunId });

            var batch = request.Members?.Skip(skip).Take(batchSize).ToList() ?? new List<AzureADUser>();

            while (batch.Count > 0)
            {
                totalSuccessCount += await context.CallActivityAsync<int>(nameof(GroupUpdaterFunction),
                                           new GroupUpdaterRequest
                                           {
                                               RunId = request.RunId,
                                               DestinationGroupId = request.DestinationGroupId,
                                               Members = batch,
                                               Type = request.Type,
                                               IsInitialSync = request.IsInitialSync
                                           });

                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount}/{request.Members.Count} users so far.",
                        RunId = request.RunId
                    });

                skip += batchSize;
                batch = request.Members.Skip(skip).Take(batchSize).ToList();
            }

            _telemetryClient.TrackMetric(nameof(Services.Entities.Metric.MembersNotFound), request.Members.Count - totalSuccessCount);

            _ = _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount} users.",
                RunId = request.RunId
            });

            _ = _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function completed",
                RunId = request.RunId
            });
        }
    }
}