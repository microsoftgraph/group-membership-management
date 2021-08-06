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
        private readonly ILoggingRepository _loggingRepository;

        public UsersReaderSubOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(UsersReaderSubOrchestratorFunction))]
        public async Task<List<AzureADUser>> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<UsersReaderRequest>();
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            if (request != null)
            {
                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderSubOrchestratorFunction)} function started", RunId = request.RunId });

                var response = await context.CallActivityAsync<UsersPageResponse>(nameof(UsersReaderFunction), request);
                allUsers.AddRange(response.Members);
                response.NonUserGraphObjects.ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));

                while (!string.IsNullOrEmpty(response.NextPageUrl))
                {
                    response = await context.CallActivityAsync<UsersPageResponse>(nameof(SubsequentUsersReaderFunction),
                                                new SubsequentUsersReaderRequest { RunId = request.RunId, NextPageUrl = response.NextPageUrl, GroupMembersPage = response.MembersPage });

                    allUsers.AddRange(response.Members);
                    response.NonUserGraphObjects.ToList().ForEach(x =>
                    {
                        if (allNonUserGraphObjects.ContainsKey(x.Key))
                            allNonUserGraphObjects[x.Key] += x.Value;
                        else
                            allNonUserGraphObjects[x.Key] = x.Value;
                    });

                    if (!context.IsReplaying)
                        _ = _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            RunId = request.RunId,
                            Message = $"Read {allUsers.Count} users from group {request.GroupId} so far"
                        });
                }

                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, allNonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));

                _ = _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"From group {request.GroupId}, read {allUsers.Count} users " +
                                $"and the following other directory objects:\n{nonUserGraphObjectsSummary}\n"
                });
            }

            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderSubOrchestratorFunction)} function completed", RunId = request.RunId });
            return allUsers;
        }
    }
}