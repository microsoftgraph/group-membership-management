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

            // Create and retrieve groups
            var groupSizesAndIds = new Dictionary<int, List<Guid>>();
            var groupSizesAndCounts = calcResponse.GroupSizesAndCounts;
            foreach (var groupSize in groupSizesAndCounts.Keys)
            {
                var groupCount = groupSizesAndCounts[groupSize];
                var groupIds = new List<Guid>();
                // For each group size, create however many groups are needed and put the ids into a list associated with the group size.
                for (var i = 0; i < groupCount; i++)
                {
                    var groupCreateResponse = await context.CallActivityAsync<GroupCreatorAndRetrieverResponse>(
                        nameof(GroupCreatorAndRetrieverFunction),
                        new GroupCreatorAndRetrieverRequest
                        {
                            GroupName = $"LoadTesting_DestinationGroup_{groupSize}_{i+1}",
                            TestGroupType = TestGroupType.LoadTesting,
                            GroupOwnersIds = new List<Guid>() { options.DestinationGroupOwnerId },
                            RetrieveMembers = false,
                            RunId = runId
                        });

                    groupIds.Add(groupCreateResponse.TargetGroup.ObjectId);
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
