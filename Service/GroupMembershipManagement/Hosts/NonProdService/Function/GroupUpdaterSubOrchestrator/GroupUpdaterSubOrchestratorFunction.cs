// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Microsoft.ApplicationInsights;

namespace Hosts.NonProdService
{
    public class GroupUpdaterSubOrchestratorFunction
    {
        public GroupUpdaterSubOrchestratorFunction()
        {
        }

        [FunctionName(nameof(GroupUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var skip = 0;
            var batchSize = 100;
            var request = context.GetInput<GroupUpdaterRequest>();
            var totalSuccessCount = 0;

            if (request == null)
            {
                return;
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function started", RunId = request.RunId });

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

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount}/{request.Members.Count} users so far.",
                                                    RunId = request.RunId
                                                });


                skip += batchSize;
                batch = request.Members.Skip(skip).Take(batchSize).ToList();
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                     new LoggerRequest
                                                     {
                                                         Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {totalSuccessCount} users.",
                                                         RunId = request.RunId
                                                     });

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                      new LoggerRequest
                                                      {
                                                          Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function completed",
                                                          RunId = request.RunId
                                                      });
        }
    }
}