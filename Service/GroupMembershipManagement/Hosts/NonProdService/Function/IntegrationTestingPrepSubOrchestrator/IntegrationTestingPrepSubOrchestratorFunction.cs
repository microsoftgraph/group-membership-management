// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class IntegrationTestingPrepSubOrchestratorFunction
    {
        private readonly INonProdService _nonProdService = null;

        private enum GroupEnums
        {
            TestGroup1Member,
            TestGroup10Members,
            TestGroup100Members,
            TestGroup1kMembers,
            TestGroup10kMembers
        }

        private Hashtable _groupSizes = new Hashtable()
        {
            { GroupEnums.TestGroup1Member, 1 },
            { GroupEnums.TestGroup10Members, 10 },
            { GroupEnums.TestGroup100Members, 100 },
            { GroupEnums.TestGroup1kMembers, 1000},
            { GroupEnums.TestGroup10kMembers, 10000 }
        };

        public IntegrationTestingPrepSubOrchestratorFunction(INonProdService nonProdService)
        {
            _nonProdService = nonProdService ?? throw new ArgumentNullException(nameof(nonProdService));
        }

        [Function(nameof(IntegrationTestingPrepSubOrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<IntegrationTestingPrepSubOrchestratorRequest>();
            var runId = request.RunId;
            var tenantUserCount = request.TenantUserCount;

            var logger = context.CreateReplaySafeLogger("Hosts.NonProdService.IntegrationTestingPrepSubOrchestratorFunction");
            using var scope = logger.BeginRunIdScope(runId);

            logger.FunctionStarted(nameof(IntegrationTestingPrepSubOrchestratorFunction));

            var tenantUsersRequired = GetMinimumUsersRequiredForTenant();
            if (tenantUserCount < tenantUsersRequired)
            {
                logger.InsufficientUsersInTenant(tenantUserCount, tenantUsersRequired);
                throw new Exception($"Error occurred in the {nameof(IntegrationTestingPrepSubOrchestratorFunction)}, because {tenantUserCount} is less than the minimum requirement of {tenantUsersRequired} users.");
            }

            var tenantUsers = await context.CallActivityAsync<List<AzureADUser>>(
                nameof(TenantUserReaderFunction),
                new TenantUserReaderRequest
                {
                    MinimunTenantUserCount = tenantUsersRequired,
                    RunId = runId
                });

            if (tenantUsers == null)
            {
                logger.ErrorWithFunction(nameof(TenantUserReaderFunction));
                throw new Exception($"Error occurred in the {nameof(TenantUserReaderFunction)}.");
            }

            // Create and populate each group
            foreach (var groupName in _groupSizes.Keys)
            {
                logger.CreatingAndPopulatingGroup(groupName.ToString());

                var groupUserCount = (int)_groupSizes[groupName];
                var desiredMembership = tenantUsers.Take(groupUserCount).ToList();

                var groupResponse = await context.CallActivityAsync<GroupCreatorAndRetrieverResponse>(
                    nameof(GroupCreatorAndRetrieverFunction),
                    new GroupCreatorAndRetrieverRequest
                    {
                        GroupName = groupName.ToString(),
                        TestGroupType = TestGroupType.IntegrationTesting,
                        RetrieveMembers = true,
                        RunId = runId
                    });

                if (groupResponse == null)
                {
                    logger.ErrorWithFunction(nameof(GroupCreatorAndRetrieverFunction));

                    throw new Exception($"Error occurred in the  {nameof(GroupCreatorAndRetrieverFunction)}, possibly due to not enough users existing in the tenant not getting retrieved");
                }

                var membershipDifference = _nonProdService.GetMembershipDifference(groupResponse.Members, desiredMembership);

                logger.CalculatedMembershipDifference(groupName.ToString(), membershipDifference.UsersToAdd.Count, membershipDifference.UsersToRemove.Count);

                if (membershipDifference.UsersToAdd.Count > 0)
                    await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(
                        nameof(GroupUpdaterSubOrchestratorFunction),
                        new GroupUpdaterRequest
                        {
                            Type = RequestType.Add,
                            TargetGroup = groupResponse.TargetGroup,
                            Members = membershipDifference.UsersToAdd,
                            RunId = runId
                        });

                if (membershipDifference.UsersToRemove.Count > 0)
                    await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(
                        nameof(GroupUpdaterSubOrchestratorFunction),
                        new GroupUpdaterRequest
                        {
                            Type = RequestType.Remove,
                            TargetGroup = groupResponse.TargetGroup,
                            Members = membershipDifference.UsersToRemove,
                            RunId = runId
                        });
            }

            logger.FunctionCompleted(nameof(IntegrationTestingPrepSubOrchestratorFunction));
        }

        private int GetMinimumUsersRequiredForTenant()
        {
            var sizes = _groupSizes.Values.Cast<int>();
            return sizes.Max();
        }
    }
}
