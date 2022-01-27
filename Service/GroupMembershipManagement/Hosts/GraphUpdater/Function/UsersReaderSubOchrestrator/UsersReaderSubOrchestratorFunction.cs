// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class UsersReaderSubOrchestratorFunction
    {
        public UsersReaderSubOrchestratorFunction()
        {
        }

        [FunctionName(nameof(UsersReaderSubOrchestratorFunction))]
        public async Task<List<AzureADUser>> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<UsersReaderRequest>();
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            if (request != null && request.SyncJob != null)
            {

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = $"{nameof(UsersReaderSubOrchestratorFunction)} function started", SyncJob = request.SyncJob });

                var response = await context.CallActivityAsync<UsersPageResponse>(nameof(UsersReaderFunction), request);
                allUsers.AddRange(response.Members);
                response.NonUserGraphObjects.ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));

                while (!string.IsNullOrEmpty(response.NextPageUrl))
                {
                    response = await context.CallActivityAsync<UsersPageResponse>(nameof(SubsequentUsersReaderFunction),
                                                new SubsequentUsersReaderRequest
                                                {
                                                    RunId = request.SyncJob.RunId.GetValueOrDefault(),
                                                    NextPageUrl = response.NextPageUrl,
                                                    GroupMembersPage = response.MembersPage
                                                });

                    allUsers.AddRange(response.Members);
                    response.NonUserGraphObjects.ToList().ForEach(x =>
                    {
                        if (allNonUserGraphObjects.ContainsKey(x.Key))
                            allNonUserGraphObjects[x.Key] += x.Value;
                        else
                            allNonUserGraphObjects[x.Key] = x.Value;
                    });

                    await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest { Message = $"Read {allUsers.Count} users from group {request.SyncJob.TargetOfficeGroupId} so far", SyncJob = request.SyncJob });
                }

                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, allNonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));

                await context.CallActivityAsync(nameof(LoggerFunction),
                                                new LoggerRequest
                                                {
                                                    Message = $"From group {request.SyncJob.TargetOfficeGroupId}, read {allUsers.Count} users " +
                                                                $"and the following other directory objects:\n{nonUserGraphObjectsSummary}\n",
                                                    SyncJob = request.SyncJob
                                                });

            }

            await context.CallActivityAsync(nameof(LoggerFunction),
                                                    new LoggerRequest { Message = $"{nameof(UsersReaderSubOrchestratorFunction)} function completed", SyncJob = request.SyncJob });

            return allUsers;
        }
    }
}