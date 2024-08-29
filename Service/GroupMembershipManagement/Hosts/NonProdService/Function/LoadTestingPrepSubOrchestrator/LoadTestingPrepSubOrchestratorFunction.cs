// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Microsoft.DurableTask;
using Microsoft.Extensions.Options;
using Models;
using NonProdService.LoadTestingPrepSubOrchestrator;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class LoadTestingPrepSubOrchestratorFunction
    {
        private IOptions<LoadTestingPrepSubOrchestratorOptions> _options;
        public LoadTestingPrepSubOrchestratorFunction(IOptions<LoadTestingPrepSubOrchestratorOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        [Function(nameof(LoadTestingPrepSubOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<LoadTestingPrepSubOrchestratorRequest>();
            var runId = request.RunId;
            var tenantUserCount = request.TenantUserCount;
            var options = _options.Value;

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(LoadTestingPrepSubOrchestratorFunction)} function started", RunId = runId, Verbosity = VerbosityLevel.DEBUG });

            // Determine how many groups of each size are needed
            var calcResponse = await context.CallActivityAsync<LoadTestingGroupCalculatorResponse>(
                nameof(LoadTestingGroupCalculatorFunction),
                new LoadTestingGroupCalculatorRequest
                {
                    NumberOfGroups = options.GroupCount,
                    NumberOfUsers = tenantUserCount,
                    RunId = runId
                });

            // Create and retrieve groups
            var groupSizesAndIds = new Dictionary<int, List<Guid>>();
            var groupSizesAndCounts = calcResponse.GroupSizesAndCounts;
            foreach (var groupSize in groupSizesAndCounts.Keys)
            {
                var groupCount = groupSizesAndCounts[groupSize];
                var groupIds = new List<Guid>();

                // Process groups in batches
                const int batchSize = 10;
                for (var i = 0; i < groupCount; i += batchSize)
                {
                    var batchTasks = new List<Task<GroupCreatorAndRetrieverResponse>>();
                    for (var j = 0; j < batchSize && (i + j) < groupCount; j++)
                    {
                        batchTasks.Add(context.CallActivityAsync<GroupCreatorAndRetrieverResponse>(
                            nameof(GroupCreatorAndRetrieverFunction),
                            new GroupCreatorAndRetrieverRequest
                            {
                                GroupName = $"LoadTesting_DestinationGroup_{groupSize}_{i + j + 1}",
                                TestGroupType = TestGroupType.LoadTesting,
                                GroupOwnersIds = new List<Guid>() { options.DestinationGroupOwnerId },
                                RetrieveMembers = false,
                                RunId = runId
                            }));
                    }

                    var batchResults = await Task.WhenAll(batchTasks);
                    groupIds.AddRange(batchResults.Select(result => result.TargetGroup.ObjectId));
                }

                groupSizesAndIds.Add(groupSize, groupIds);
            }

            // Retrieve existing SyncJobs
            var syncJobsResponse = await context.CallActivityAsync<LoadTestingSyncJobRetrieverResponse>(
                nameof(LoadTestingSyncJobRetrieverFunction),
                new LoadTestingSyncJobRetrieverRequest
                {
                    RunId = runId
                });

            // Create sync jobs for the groups, if they don't already exist.
            await context.CallActivityAsync(
                nameof(LoadTestingSyncJobCreatorFunction),
                new LoadTestingSyncJobCreatorRequest
                {
                    GroupSizesAndIds = groupSizesAndIds,
                    SyncJobs = syncJobsResponse.SyncJobs,
                    RunId = runId
                });

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(LoadTestingPrepSubOrchestratorFunction)} function completed", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
        }

        
    }
}
