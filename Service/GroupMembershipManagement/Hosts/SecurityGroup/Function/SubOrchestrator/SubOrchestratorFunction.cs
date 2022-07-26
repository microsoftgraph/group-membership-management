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
using Newtonsoft.Json;
using Entities.ServiceBus;

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
        public async Task<(List<AzureADUser> Users, SyncStatus Status)> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<SecurityGroupRequest>();
            var allUsers = new List<AzureADUser>();
            var deltaUsersToAdd = new List<AzureADUser>();
            var deltaUsersToRemove = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            if (request != null)
            {
                _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
                var isExistingGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction), new GroupValidatorRequest { SyncJob = request.SyncJob, RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
                if (!isExistingGroup) { return (null, SyncStatus.SecurityGroupNotFound); }
                var count = await context.CallActivityAsync<int>(nameof(GroupsReaderFunction),
                                                            new GroupsReaderRequest {
                                                                RunId = request.RunId,
                                                                GroupId = request.SourceGroup.ObjectId
                                                            });

                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"{request.SourceGroup.ObjectId} has {count} nested groups" });

                if (count > 0)
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                    // run exisiting code
                    var response = await context.CallActivityAsync<GroupInformation>(nameof(MembersReaderFunction), new MembersReaderRequest { RunId = request.RunId, GroupId = request.SourceGroup.ObjectId });
                    allUsers.AddRange(response.Users);
                    response.NonUserGraphObjects.ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));
                    while (!string.IsNullOrEmpty(response.NextPageUrl))
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using transitive members query for group {request.SourceGroup.ObjectId}" });
                        response = await context.CallActivityAsync<GroupInformation>(nameof(SubsequentMembersReaderFunction), new SubsequentMembersReaderRequest { RunId = request.RunId, NextPageUrl = response.NextPageUrl, GroupMembersPage = response.UsersFromGroup });
                        allUsers.AddRange(response.Users);
                        response.NonUserGraphObjects.ToList().ForEach(x =>
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
                else {
                    if (count <= 0)
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                        // first check if delta file exists in cache folder
                        var filePath = $"cache/delta_{request.SourceGroup.ObjectId}";
                        var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                                            new FileDownloaderRequest
                                                                            {
                                                                                FilePath = filePath,
                                                                                SyncJob = request.SyncJob
                                                                            });
                        if (string.IsNullOrEmpty(fileContent))
                        {
                            var response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(UsersReaderFunction), new UsersReaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
                            allUsers.AddRange(response.UsersToAdd);
                            while (!string.IsNullOrEmpty(response.NextPageUrl))
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta query for group {request.SourceGroup.ObjectId}" });
                                response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(SubsequentUsersReaderFunction), new SubsequentUsersReaderRequest { RunId = request.RunId, NextPageUrl = response.NextPageUrl, GroupUsersPage = response.UsersFromGroup });
                                allUsers.AddRange(response.UsersToAdd);
                            }

                            await context.CallActivityAsync(nameof(DeltaUsersSenderFunction),
                                                    new DeltaUsersSenderRequest
                                                    {
                                                        RunId = request.RunId,
                                                        SyncJob = request.SyncJob,
                                                        ObjectId = request.SourceGroup.ObjectId,
                                                        Users = allUsers,
                                                        DeltaLink = response.DeltaUrl
                                                    });
                        }
                        else
                        {
                            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query using delta link for group {request.SourceGroup.ObjectId}" });
                            var deltaResponse = await context.CallActivityAsync<DeltaGroupInformation>(nameof(DeltaUsersReaderFunction), new DeltaUsersReaderRequest { DeltaLink = fileContent });
                            deltaUsersToAdd.AddRange(deltaResponse.UsersToAdd);
                            deltaUsersToRemove.AddRange(deltaResponse.UsersToRemove);
                            while (!string.IsNullOrEmpty(deltaResponse.NextPageUrl))
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta link for group {request.SourceGroup.ObjectId}" });
                                deltaResponse = await context.CallActivityAsync<DeltaGroupInformation>(nameof(SubsequentDeltaUsersReaderFunction), new SubsequentDeltaUsersReaderRequest { RunId = request.RunId, NextPageUrl = deltaResponse.NextPageUrl, GroupUsersPage = deltaResponse.UsersFromGroup });
                                deltaUsersToAdd.AddRange(deltaResponse.UsersToAdd);
                                deltaUsersToRemove.AddRange(deltaResponse.UsersToRemove);
                            }

                            filePath = $"cache/{request.SourceGroup.ObjectId}";
                            fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                                                new FileDownloaderRequest
                                                                                {
                                                                                    FilePath = filePath,
                                                                                    SyncJob = request.SyncJob
                                                                                });

                            var json = JsonConvert.DeserializeObject<GroupMembership>(fileContent);
                            var sourceMembers = json.SourceMembers.Distinct().ToList();
                            sourceMembers.AddRange(deltaUsersToAdd);
                            var newUsers = sourceMembers.Except(deltaUsersToRemove).ToList();
                            allUsers.AddRange(newUsers);

                            await context.CallActivityAsync(nameof(DeltaUsersSenderFunction),
                                                    new DeltaUsersSenderRequest
                                                    {
                                                        SyncJob = request.SyncJob,
                                                        ObjectId = request.SourceGroup.ObjectId,
                                                        Users = newUsers,
                                                        DeltaLink = deltaResponse.DeltaUrl
                                                    });
                        }

                        _ = _log.LogMessageAsync(new LogMessage
                        {
                            RunId = request.RunId,
                            Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users"
                        });
                    }
                }
            }
            _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return (allUsers, SyncStatus.InProgress);
        }
    }
}