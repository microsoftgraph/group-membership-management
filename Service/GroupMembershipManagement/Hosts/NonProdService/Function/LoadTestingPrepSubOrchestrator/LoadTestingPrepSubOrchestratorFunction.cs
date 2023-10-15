// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class LoadTestingPrepSubOrchestratorFunction
    {
        public LoadTestingPrepSubOrchestratorFunction()
        {
        }

        [FunctionName(nameof(LoadTestingPrepSubOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<LoadTestingPrepSubOrchestratorRequest>();
            var runId = request.RunId;
            var tenantUserCount = request.TenantUserCount;

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(LoadTestingPrepSubOrchestratorFunction)} function started", RunId = runId, Verbosity = VerbosityLevel.DEBUG });

            // lol this doesn't work, tf mate.
            var calcResponse = await context.CallActivityAsync<LoadTestingGroupCalculatorResponse>(
                nameof(LoadTestingGroupCalculatorFunction),
                new LoadTestingGroupCalculatorRequest
                {
                    NumberOfGroups = 2000,
                    NumberOfUsers = tenantUserCount,
                    RunId = runId
                });

            // create and retrieve groups
            var groupSizesAndIds = new Dictionary<int, List<Guid>>();
            var groupSizesAndCounts = calcResponse.GroupSizesAndCounts;
            foreach (var groupSize in groupSizesAndCounts.Keys)
            {
                var groupCount = groupSizesAndCounts[groupSize];
                var groupIds = new List<Guid>();
                // create however many groups are needed for the group size
                for (var i = 0; i < groupCount; i++)
                {
                    var groupCreateResponse = await context.CallActivityAsync<GroupCreatorAndRetrieverResponse>(
                        nameof(GroupCreatorAndRetrieverFunction),
                        new GroupCreatorAndRetrieverRequest
                        {
                            GroupName = $"LoadTesting_DestinationGroup_{groupSize}_{i+1}",
                            TestGroupType = TestGroupType.LoadTesting,
                            RetrieveMembers = false,
                            RunId = runId
                        });

                    groupIds.Add(groupCreateResponse.TargetGroup.ObjectId);
                }
                groupSizesAndIds.Add(groupSize, groupIds);
            }

            // Add "gmm" (graph app) as owner to the new groups.
            // Create sync jobs.

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(LoadTestingPrepSubOrchestratorFunction)} function completed", RunId = runId, Verbosity = VerbosityLevel.DEBUG });
        }

        
    }
}
