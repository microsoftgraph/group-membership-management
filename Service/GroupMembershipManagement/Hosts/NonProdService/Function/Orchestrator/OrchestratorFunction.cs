// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class OrchestratorFunction
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

        public OrchestratorFunction(INonProdService nonProdService)
        {
            _nonProdService = nonProdService ?? throw new ArgumentNullException(nameof(nonProdService));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var runId = Guid.NewGuid();

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", RunId = runId });

            var tenantUsersRequired = GetMinimumUsersRequiredForTenant();
            var tenantUsers = await context.CallActivityAsync<List<AzureADUser>>(
                nameof(TenantUserReaderFunction),
                new TenantUserReaderRequest
                {
                    MinimunTenantUserCount = tenantUsersRequired,
                    RunId = runId
                });

            if(tenantUsers == null)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LogMessage { Message = $"Error with {nameof(TenantUserReaderFunction)}, check exception" });

                throw new Exception($"Error occurred in the {nameof(TenantUserReaderFunction)}, because there are not enough users in tenant to meet minimum requirement of {tenantUsersRequired}");
            }

            // Create and populate each group
            foreach (var groupName in _groupSizes.Keys)
            {
                await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Creating, if nonexistent, and populating, if not properly populated, group with name {groupName}", RunId = runId });

                var groupUserCount = (int)_groupSizes[groupName];
                var desiredMembership = tenantUsers.Take(groupUserCount).ToList();

                var groupResponse = await context.CallActivityAsync<GroupCreatorAndRetrieverResponse>(
                    nameof(GroupCreatorAndRetrieverFunction),
                    new GroupCreatorAndRetrieverRequest
                    {
                        GroupName = groupName.ToString(),
                        RunId = runId
                    });

                if(groupResponse == null)
                {
                    await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"Error with {nameof(GroupCreatorAndRetrieverFunction)}, check exception" });

                    throw new Exception($"Error occurred in the  {nameof(GroupCreatorAndRetrieverFunction)}, possibly due to not enough users existing in the tenant not getting retrieved");
                }

                var membershipDifference = _nonProdService.GetMembershipDifference(groupResponse.TargetGroup, groupResponse.Members, desiredMembership, runId);

                await context.CallActivityAsync(nameof(LoggerFunction), new LogMessage
                {
                    Message = $"Calculated membership difference for {groupName}: Must add {membershipDifference.UsersToAdd.Count} users and remove {membershipDifference.UsersToRemove.Count} users.",
                    RunId = runId
                });

                if (membershipDifference.UsersToAdd.Count > 0)
                    await context.CallSubOrchestratorAsync<GraphUpdaterStatus>(
                        nameof(GroupUpdaterSubOrchestratorFunction),
                        new GroupUpdaterRequest
                        {
                            Type = RequestType.Add,
                            TargetGroup = groupResponse.TargetGroup,
                            Members = membershipDifference.UsersToAdd,
                            RunId=runId
                        });

                if(membershipDifference.UsersToRemove.Count > 0)
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

            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = runId });
        }

        private int GetMinimumUsersRequiredForTenant()
        {
            var sizes = _groupSizes.Values.Cast<int>();
            return sizes.Max();
        }
    }
}
