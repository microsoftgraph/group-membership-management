// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Models;
using NonProdService.LoadTestingPrepSubOrchestrator;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

            var logger = context.CreateReplaySafeLogger("Hosts.NonProdService.LoadTestingPrepSubOrchestratorFunction");
            using var scope = logger.BeginRunIdScope(runId);

            logger.FunctionStarted(nameof(LoadTestingPrepSubOrchestratorFunction));

            var allGroupNames = await context.CallActivityAsync<GetAllGroupNamesResponse>(nameof(GetAllGroupNamesFunction), new GetAllGroupNamesRequest { RunId = runId });

            // Determine how many groups of each size are needed
            var calcResponse = await context.CallActivityAsync<LoadTestingGroupCalculatorResponse>(
                nameof(LoadTestingGroupCalculatorFunction),
                new LoadTestingGroupCalculatorRequest
                {
                    NumberOfGroups = options.GroupCount,
                    NumberOfUsers = tenantUserCount,
                    RunId = runId,
                    ExistingGroupNames = allGroupNames.Groups.Values.ToList()
                });

            var groupsToCreate = calcResponse.GroupSizesAndCounts;

            // Phase 1: Create groups in batches of 5,000 to survive host recycling (~75 min cycles)
            const int groupCreationBatchSize = 5000;
            var allBatchResponses = new List<GroupCreatorAndRetrieverBatchResponse>();
            var existingNames = allGroupNames.Groups.Values.Where(v => v != null).ToList();

            foreach (var groupSize in groupsToCreate.Keys.OrderBy(k => k))
            {
                var totalCount = groupsToCreate[groupSize];

                for (int offset = 0; offset < totalCount; offset += groupCreationBatchSize)
                {
                    var chunkCount = Math.Min(groupCreationBatchSize, totalCount - offset);

                    logger.CreatingGroupsBatch(groupSize, offset, chunkCount, totalCount);

                    var batchResponse = await context.CallActivityAsync<List<GroupCreatorAndRetrieverBatchResponse>>(
                        nameof(GroupCreatorAndRetrieverBatchFunction),
                        new GroupCreatorAndRetrieverBatchRequest
                        {
                            BaseGroupName = $"LoadTesting_DestinationGroup_{groupSize}",
                            GroupCount = chunkCount,
                            RetrieveMembers = false,
                            RunId = runId,
                            ExistingGroupNames = existingNames,
                            StartingIndex = offset
                        });

                    allBatchResponses.AddRange(batchResponse);
                }
            }

            var allCreatedGroupIds = allBatchResponses
                .Where(r => r.TargetGroup != null)
                .Select(r => r.TargetGroup.ObjectId)
                .ToList();

            // Phase 2: Wait for Graph API replication before ownership assignment
            if (allCreatedGroupIds.Count > 0)
            {
                logger.WaitingForGraphReplication(allCreatedGroupIds.Count);
                await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(30), CancellationToken.None);
            }

            // Phase 3: Ensure ownership of all managed groups in batches
            var managedGroupIds = allGroupNames.Groups
                .Where(kvp => kvp.Value != null && kvp.Value.StartsWith("LoadTesting_DestinationGroup_", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();
            managedGroupIds.AddRange(allCreatedGroupIds);
            managedGroupIds = managedGroupIds.Distinct().ToList();

            if (managedGroupIds.Count > 0)
            {
                const int ownershipBatchSize = 5000;
                var ownershipBatches = managedGroupIds.Chunk(ownershipBatchSize).ToList();
                var aggregatedOwnership = new GroupOwnershipResult
                {
                    TotalManagedGroups = managedGroupIds.Count,
                    FailedGroupIds = new List<Guid>()
                };

                for (int batchIndex = 0; batchIndex < ownershipBatches.Count; batchIndex++)
                {
                    var batch = ownershipBatches[batchIndex];

                    logger.EnsuringOwnershipBatch(batchIndex + 1, ownershipBatches.Count, batch.Length);

                    var ownershipResult = await context.CallActivityAsync<GroupOwnershipResult>(
                        nameof(EnsureGroupOwnershipFunction),
                        new EnsureGroupOwnershipRequest
                        {
                            ManagedGroupIds = batch.ToList(),
                            OwnerAppId = destinationGroupOwnerId,
                            RunId = runId
                        });

                    aggregatedOwnership.GroupsAlreadyOwned += ownershipResult.GroupsAlreadyOwned;
                    aggregatedOwnership.GroupsNewlyOwned += ownershipResult.GroupsNewlyOwned;
                    aggregatedOwnership.GroupsFailed += ownershipResult.GroupsFailed;
                    if (ownershipResult.FailedGroupIds != null)
                        aggregatedOwnership.FailedGroupIds.AddRange(ownershipResult.FailedGroupIds);
                }

                logger.OwnershipEnsured(aggregatedOwnership.GroupsAlreadyOwned, aggregatedOwnership.GroupsNewlyOwned, aggregatedOwnership.GroupsFailed);
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
                logger.NoGroupsToCreateSyncJobsFor();
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

            logger.FunctionCompleted(nameof(LoadTestingPrepSubOrchestratorFunction));
        }
    }
}
