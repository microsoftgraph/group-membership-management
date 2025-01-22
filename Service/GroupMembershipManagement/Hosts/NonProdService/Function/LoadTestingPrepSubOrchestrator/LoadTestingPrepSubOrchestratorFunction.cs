// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
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

        [FunctionName(nameof(LoadTestingPrepSubOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
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

            // Determine how many groups of each size we still need to create
            var groupSizesAndCounts = calcResponse.GroupSizesAndCounts;
            var groupsToCreate = await context.CallActivityAsync<GroupDeltaCalculatorResponse>(
                nameof(GroupDeltaCalculatorFunction),
                new GroupDeltaCalculatorRequest
                {
                    GroupSizesAndCounts = groupSizesAndCounts,
                    RunId = runId
                });

            // Create and retrieve groups
            var groupSizesAndIds = new Dictionary<int, List<Guid>>();
            foreach (var groupSize in groupsToCreate.GroupsToCreate.Keys)
            {
                var groupCount = groupsToCreate.GroupsToCreate[groupSize];

                // Call the batch function to create and retrieve groups
                var batchResponse = await context.CallActivityAsync<List<GroupCreatorAndRetrieverBatchResponse>>(
                    nameof(GroupCreatorAndRetrieverBatchFunction),
                    new GroupCreatorAndRetrieverBatchRequest
                    {
                        BaseGroupName = $"LoadTesting_DestinationGroup_{groupSize}",
                        GroupCount = groupCount,
                        DestinationGroupOwnerId = options.DestinationGroupOwnerId,
                        RetrieveMembers = false,
                        RunId = runId
                    });

                var groupIds = batchResponse.Select(response => response.TargetGroup.ObjectId).ToList();
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
