// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Graph;
using System;
using System.Linq;

namespace Hosts.SecurityGroup
{
    public class SubOrchestratorFunction
    {
        private readonly ILoggingRepository _log;

        public SubOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _log = loggingRepository;
        }

        [FunctionName(nameof(SubOrchestratorFunction))]
        public async Task<(List<AzureADUser> Users, SyncStatus Status)> RunSubOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<SecurityGroupRequest>();
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            if (request != null)
            {
                _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function started", RunId = request.RunId });
                var isExistingGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction), new GroupValidatorRequest { SyncJob = request.SyncJob, RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
                if (!isExistingGroup) { return (null, SyncStatus.SecurityGroupNotFound); }
                var response = await context.CallActivityAsync<(List<AzureADUser> users,
                                                                Dictionary<string, int> nonUserGraphObjects,
                                                                string nextPageUrl,
                                                                IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)>(nameof(UsersReaderFunction), new UsersReaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
                allUsers.AddRange(response.users);
                response.nonUserGraphObjects.ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));
                while (!string.IsNullOrEmpty(response.nextPageUrl))
                {
                    response = await context.CallActivityAsync<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)>(nameof(SubsequentUsersReaderFunction), new SubsequentUsersReaderRequest { RunId = request.RunId, NextPageUrl = response.nextPageUrl, GroupMembersPage = response.usersFromGroup });
                    allUsers.AddRange(response.users);
                    response.nonUserGraphObjects.ToList().ForEach(x =>
                    {
                        if (allNonUserGraphObjects.ContainsKey(x.Key))
                            allNonUserGraphObjects[x.Key] += x.Value;
                        else
                            allNonUserGraphObjects[x.Key] = x.Value;
                    });
                }
                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, allNonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                _ = _log.LogMessageAsync(new LogMessage
                {
                    RunId = request.RunId,
                    Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users " +
                                $"and the following other directory objects:\n{nonUserGraphObjectsSummary}\n"
                });
            }
            _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = request.RunId });
            return (allUsers, SyncStatus.InProgress);
        }
    }
}