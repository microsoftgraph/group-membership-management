// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterSubOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository;

        public GroupUpdaterSubOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(GroupUpdaterSubOrchestratorFunction))]
        public async Task RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var skip = 0;
            var batchSize = 100;
            var request = context.GetInput<GroupUpdaterRequest>();

            if (request == null)
            {
                return;
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function started", RunId = request.RunId });

            var batch = request.Members?.Skip(skip).Take(batchSize).ToList() ?? new List<AzureADUser>();

            while (batch.Count > 0)
            {
                await context.CallActivityAsync(nameof(GroupUpdaterFunction),
                                           new GroupUpdaterRequest
                                           {
                                               RunId = request.RunId,
                                               DestinationGroupId = request.DestinationGroupId,
                                               Members = batch,
                                               Type = request.Type
                                           });

                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {skip}/{request.Members.Count} users so far.",
                        RunId = request.RunId
                    });

                skip += batchSize;
                batch = request.Members.Skip(skip).Take(batchSize).ToList();
            }

            _ = _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{(request.Type == RequestType.Add ? "Added" : "Removed")} {request.Members.Count} users.",
                RunId = request.RunId
            });

            _ = _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"{nameof(GroupUpdaterSubOrchestratorFunction)} function completed",
                RunId = request.RunId
            });
        }
    }
}