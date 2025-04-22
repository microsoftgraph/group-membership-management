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
            var destinationGroupOwnerId = options.DestinationGroupOwnerId;

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(LoadTestingPrepSubOrchestratorFunction)} function started", RunId = runId, Verbosity = VerbosityLevel.DEBUG });

            var allGroupNames = await context.CallActivityAsync<GetAllGroupNamesResponse>(nameof(GetAllGroupNamesFunction), new GetAllGroupNamesRequest { RunId = runId });
            
            // Determine how many groups of each size are needed
            var calcResponse = await context.CallActivityAsync<LoadTestingGroupCalculatorResponse>(
                nameof(LoadTestingGroupCalculatorFunction),
                new LoadTestingGroupCalculatorRequest
                {
                    NumberOfGroups = options.GroupCount,
                    NumberOfUsers = tenantUserCount,
                    RunId = runId,
                    ExistingGroupNames = allGroupNames.GroupNames
                });

            var groupsToCreate = calcResponse.GroupSizesAndCounts;

            // Create and retrieve groups
            var groupSizesAndIds = new Dictionary<int, List<Guid>>();
            foreach (var groupSize in groupsToCreate.Keys)
            {
                var groupCount = groupsToCreate[groupSize];

                // Call the batch function to create and retrieve groups
                var batchResponse = await context.CallActivityAsync<List<GroupCreatorAndRetrieverBatchResponse>>(
                    nameof(GroupCreatorAndRetrieverBatchFunction),
                    new GroupCreatorAndRetrieverBatchRequest
                    {
                        BaseGroupName = $"LoadTesting_DestinationGroup_{groupSize}",
                        GroupCount = groupCount,
                        GroupOwnersIds = new List<Guid> { destinationGroupOwnerId },
                        RetrieveMembers = false,
                        RunId = runId,
                        ExistingGroupNames = allGroupNames.GroupNames
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

            var targetGroupIds = syncJobsResponse.SyncJobs.Select(x => x.Group.GroupId).ToList();

            // If all groups exist, make sure they all have a sync job.
            if (groupsToCreate.Count == 0)
            {
                var syncJobCheckerResponse = await context.CallActivityAsync<SyncJobCheckerResponse>(nameof(SyncJobCheckerFunction), new SyncJobCheckerRequest
                {
                    RunId = runId,
                    TargetGroupIds = targetGroupIds
                });

                groupSizesAndIds = syncJobCheckerResponse.GroupSizesAndIds;
            }

            if (groupSizesAndIds.Count == 0)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = "No groups to create sync jobs for", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
            }
            else
            {
                await context.CallActivityAsync(
                    nameof(LoadTestingSyncJobCreatorFunction),
                    new LoadTestingSyncJobCreatorRequest
                    {
                        GroupSizesAndIds = groupSizesAndIds,
                        TargetGroupIds = targetGroupIds,
                        RunId = runId
                    });
            }

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(LoadTestingPrepSubOrchestratorFunction)} function completed", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
        }
    }
}
