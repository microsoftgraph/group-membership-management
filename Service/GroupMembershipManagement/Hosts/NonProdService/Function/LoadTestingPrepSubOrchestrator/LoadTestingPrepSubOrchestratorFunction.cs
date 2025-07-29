// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
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
            
            int batchSize = 200; 
            foreach (var groupSize in groupsToCreate.Keys)
            {
                // this section needs to be updated once we transition this function to isolated-worker model. 
                // In Isolated, we no longer need to batch these calls and we can just send the entire group count at once.
                var totalGroupCount = groupsToCreate[groupSize];
                var groupIds = new List<Guid>();
                var cumulativeCreatedCount = 0; //We will not need this variable once we transition to isolated-worker model.

                for (int i = 0; i < totalGroupCount; i += batchSize)
                {
                    int currentBatchSize = Math.Min(batchSize, totalGroupCount - i);

                    var batchResponse = await context.CallActivityAsync<List<GroupCreatorAndRetrieverBatchResponse>>(
                        nameof(GroupCreatorAndRetrieverBatchFunction),
                        new GroupCreatorAndRetrieverBatchRequest
                        {
                            BaseGroupName = $"LoadTesting_DestinationGroup_{groupSize}",
                            GroupCount = currentBatchSize,
                            GroupOwnersIds = new List<Guid> { destinationGroupOwnerId },
                            RetrieveMembers = false,
                            RunId = runId,
                            ExistingGroupNames = allGroupNames.GroupNames,
                            StartingIndex = cumulativeCreatedCount // We will not need this parameter once we transition to isolated-worker model.
                        });

                    groupIds.AddRange(batchResponse.Select(response => response.TargetGroup.ObjectId));
                    cumulativeCreatedCount += currentBatchSize; // We will not need this variable once we transition to isolated-worker model.
                }
            }

            // Retrieve existing SyncJobs
            var syncJobsResponse = await context.CallActivityAsync<LoadTestingSyncJobRetrieverResponse>(
                nameof(LoadTestingSyncJobRetrieverFunction),
                new LoadTestingSyncJobRetrieverRequest
                {
                    RunId = runId
                });

            var targetGroupIds = syncJobsResponse.SyncJobs.Where(x => x.MembershipType == MembershipTypes.GroupMembership.ToString()).Select(x => x.Group.GroupId).ToList();

            var syncJobCheckerResponse = await context.CallActivityAsync<SyncJobCheckerResponse>(nameof(SyncJobCheckerFunction), new SyncJobCheckerRequest
            {
                RunId = runId,
                TargetGroupIds = targetGroupIds,
                ExpectedTargetDistribution = calcResponse.ExpectedTargetDistribution
            });

            var groupSizesAndIds = syncJobCheckerResponse.GroupSizesAndIds;

            if (groupSizesAndIds.Count == 0)
            {
                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = "No groups to create sync jobs for", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
            }
            else
            {
                // When we transition to isolated-worker model, we can remove this batching logic and just send the entire dictionary at once.  
                batchSize = 500;

                foreach (var batch in BatchGroupSizesAndIds(groupSizesAndIds, batchSize))
                {
                    await context.CallActivityAsync(
                        nameof(LoadTestingSyncJobCreatorFunction),
                        new LoadTestingSyncJobCreatorRequest
                        {
                            GroupSizesAndIds = batch,
                            TargetGroupIds = targetGroupIds,
                            RunId = runId
                        });
                }
            }

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(LoadTestingPrepSubOrchestratorFunction)} function completed", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
        }

        public static IEnumerable<Dictionary<int, List<Guid>>> BatchGroupSizesAndIds(Dictionary<int, List<Guid>> fullDict, int batchSize)
        {
            var allEntries = fullDict.SelectMany(kvp => kvp.Value.Select(id => new { kvp.Key, Id = id })).ToList();

            for (int i = 0; i < allEntries.Count; i += batchSize)
            {
                var batch = allEntries.Skip(i).Take(batchSize);

                var dict = new Dictionary<int, List<Guid>>();
                foreach (var item in batch)
                {
                    if (!dict.ContainsKey(item.Key))
                        dict[item.Key] = new List<Guid>();

                    dict[item.Key].Add(item.Id);
                }

                yield return dict;
            }
        }
    }
}
