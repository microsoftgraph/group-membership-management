// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Models;

namespace Hosts.NonProdService
{
    public class GroupUpdaterSubOrchestratorFunction
    {
        public GroupUpdaterSubOrchestratorFunction()
        {
        }

        [Function(nameof(GroupUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var skip = 0;
            var batchSize = 100;
            var request = context.GetInput<GroupUpdaterRequest>();
            var totalSuccessCount = 0;

            if (request == null)
            {
                return;
            }

            var logger = context.CreateReplaySafeLogger("Hosts.NonProdService.GroupUpdaterSubOrchestratorFunction");
            using var scope = logger.BeginRunIdScope(request.RunId);

            logger.FunctionStarted(nameof(GroupUpdaterSubOrchestratorFunction));

            var batch = request.Members?.Skip(skip).Take(batchSize).ToList() ?? new List<AzureADUser>();

            while (batch.Count > 0)
            {
                totalSuccessCount += await context.CallActivityAsync<int>(nameof(GroupUpdaterFunction),
                                           new GroupUpdaterRequest
                                           {
                                               TargetGroup = request.TargetGroup,
                                               Members = batch,
                                               Type = request.Type,
                                               RunId = request.RunId
                                           });

                var actionType = request.Type == RequestType.Add ? "Added" : "Removed";
                logger.GroupUpdaterProgress(actionType, totalSuccessCount, request.Members.Count);

                skip += batchSize;
                batch = request.Members.Skip(skip).Take(batchSize).ToList();
            }

            var finalActionType = request.Type == RequestType.Add ? "Added" : "Removed";
            logger.GroupUpdaterCompleted(finalActionType, totalSuccessCount);

            logger.FunctionCompleted(nameof(GroupUpdaterSubOrchestratorFunction));
        }
    }
}