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
using Newtonsoft.Json;
using Entities.ServiceBus;
using Microsoft.ApplicationInsights;
using Repositories.Contracts.InjectConfig;
using Microsoft.Graph;

namespace Hosts.SecurityGroup
{
    public class SubOrchestratorFunction
    {
        private readonly IDeltaCachingConfig _deltaCachingConfig;
        private readonly ILoggingRepository _log;
        private readonly TelemetryClient _telemetryClient;

        public SubOrchestratorFunction(
            IDeltaCachingConfig deltaCachingConfig,
            ILoggingRepository loggingRepository,
            TelemetryClient telemetryClient)
        {
            _deltaCachingConfig = deltaCachingConfig;
            _log = loggingRepository;
            _telemetryClient = telemetryClient;
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
                var transitiveGroupCount = await context.CallActivityAsync<int>(nameof(GetTransitiveGroupCountFunction),
                                                            new GetTransitiveGroupCountRequest
                                                            {
                                                                RunId = request.RunId,
                                                                GroupId = request.SourceGroup.ObjectId
                                                            });

                if (!context.IsReplaying)
                {
                    if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                    {
                        var nestedGroupEvent = new Dictionary<string, string>
                        {
                            { "SourceGroupObjectId", request.SourceGroup.ObjectId.ToString() },
                            { "DestinationGroupObjectId", request.SyncJob.TargetOfficeGroupId.ToString() },
                            { "NestedGroupCount", transitiveGroupCount.ToString() }
                        };
                        _telemetryClient.TrackEvent("NestedGroupCount", nestedGroupEvent);
                    }
                }

                if (transitiveGroupCount > 0 || !_deltaCachingConfig.DeltaCacheEnabled)
                {
                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                    // run exisiting code
                    var response = await GetMembersReaderFunction(context, allUsers, allNonUserGraphObjects, request);
                    allUsers = response.allUsers;
                    if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                    {
                        allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                    }
                    allNonUserGraphObjects = response.allNonUserGraphObjects;
                    var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, allNonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                    _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                }
                else
                {
                    // first check if delta file exists in cache folder
                    var filePath = $"cache/delta_{request.SourceGroup.ObjectId}";
                    var fileContent = await GetFileDownloaderFunction(context, filePath, request.SyncJob);
                    if (string.IsNullOrEmpty(fileContent))
                    {
                        try
                        {
                            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                            var response = await GetUsersReaderFunction(context, allUsers, request);
                            allUsers = response.allUsers;
                            if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                            {
                                allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                            }
                            await GetDeltaUsersSenderFunction(context, request, allUsers, response.deltaUrl);
                        }
                        catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                        {
                            _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                            allUsers.Clear();
                            allNonUserGraphObjects.Clear();

                            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                            // run exisiting code
                            var response = await GetMembersReaderFunction(context, allUsers, allNonUserGraphObjects, request);
                            allUsers = response.allUsers;
                            if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                            {
                                allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                            }
                            allNonUserGraphObjects = response.allNonUserGraphObjects;
                            var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, allNonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                            _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                        }
                    }
                    else
                    {
                        try
                        {
                            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query using delta link for group {request.SourceGroup.ObjectId}" });
                            var deltaResponse = await GetDeltaUsersReaderFunction(context, fileContent, deltaUsersToAdd, deltaUsersToRemove, request);
                            deltaUsersToAdd = deltaResponse.deltaUsersToAdd;
                            deltaUsersToRemove = deltaResponse.deltaUsersToRemove;
                            filePath = $"cache/{request.SourceGroup.ObjectId}";
                            fileContent = await GetFileDownloaderFunction(context, filePath, request.SyncJob);
                            var json = JsonConvert.DeserializeObject<GroupMembership>(fileContent);
                            var sourceMembers = json.SourceMembers.Distinct().ToList();
                            if (!context.IsReplaying) { TrackCachedUsersEvent(request.RunId, sourceMembers.Count, request.SourceGroup.ObjectId); }
                            sourceMembers.AddRange(deltaUsersToAdd);
                            var newUsers = sourceMembers.Except(deltaUsersToRemove).ToList();
                            allUsers.AddRange(newUsers);

                            // verify the user count from group & cache
                            var countOfUsersFromAADGroup = await GetUsersCountFunction(context, request.SourceGroup.ObjectId, request.RunId);
                            var countOfUsersFromCache = allUsers.Count;

                            if (countOfUsersFromAADGroup != countOfUsersFromCache)
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"{request.SourceGroup.ObjectId} has {countOfUsersFromAADGroup} users but cache has {countOfUsersFromCache} users. Running delta query..." });
                                var response = await GetUsersReaderFunction(context, allUsers, request);
                                allUsers = response.allUsers;
                            }
                            else if (countOfUsersFromAADGroup == countOfUsersFromCache)
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Number of users from {request.SourceGroup.ObjectId} ({countOfUsersFromAADGroup}) and cache ({countOfUsersFromCache}) are equal" });
                            }

                            if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                            {
                                allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                            }
                            await GetDeltaUsersSenderFunction(context, request, allUsers, deltaResponse.deltaUrl);
                        }
                        catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                        {
                            _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query using delta link failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                            allUsers.Clear();

                            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                            var response = await GetUsersReaderFunction(context, allUsers, request);
                            allUsers = response.allUsers;
                            if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                            {
                                allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                            }
                            await GetDeltaUsersSenderFunction(context, request, allUsers, response.deltaUrl);
                        }
                    }
                    _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users" });
                }
            }
            _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return (allUsers, SyncStatus.InProgress);
        }

        private void TrackCachedUsersEvent(Guid runId, int cachedUsersCount, Guid groupId)
        {
            var cachedUsersEvent = new Dictionary<string, string>
            {
                { "RunId", runId.ToString() },
                { "GroupObjectId", groupId.ToString() },
                { "UsersInCache", cachedUsersCount.ToString() }
            };
            _telemetryClient.TrackEvent("UsersInCacheCount", cachedUsersEvent);
        }

        public async Task<string> GetFileDownloaderFunction(IDurableOrchestrationContext context, string filePath, SyncJob syncJob)
        {
            return await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                            new FileDownloaderRequest
                                                            {
                                                                FilePath = filePath,
                                                                SyncJob = syncJob
                                                            });
        }

        public async Task<int> GetUsersCountFunction(IDurableOrchestrationContext context, Guid groupId, Guid runId)
        {
            return await context.CallActivityAsync<int>(nameof(GetUserCountFunction),
                                            new GetUserCountRequest
                                            {
                                                RunId = runId,
                                                GroupId = groupId
                                            });
        }

        public async Task GetDeltaUsersSenderFunction(IDurableOrchestrationContext context, SecurityGroupRequest request, List<AzureADUser> allUsers, string deltaUrl)
        {
            await context.CallActivityAsync(nameof(DeltaUsersSenderFunction),
                                                    new DeltaUsersSenderRequest
                                                    {
                                                        RunId = request.RunId,
                                                        SyncJob = request.SyncJob,
                                                        ObjectId = request.SourceGroup.ObjectId,
                                                        Users = allUsers,
                                                        DeltaLink = deltaUrl
                                                    });
        }

        public async Task<(List<AzureADUser> allUsers, Dictionary<string, int> allNonUserGraphObjects)> GetMembersReaderFunction(
                                                                            IDurableOrchestrationContext context,
                                                                            List<AzureADUser> allUsers,
                                                                            Dictionary<string, int> allNonUserGraphObjects,
                                                                            SecurityGroupRequest request)
        {
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
            return (allUsers, allNonUserGraphObjects);
        }

        public async Task<(List<AzureADUser> allUsers, string deltaUrl)> GetUsersReaderFunction(
                                                    IDurableOrchestrationContext context,
                                                    List<AzureADUser> allUsers,
                                                    SecurityGroupRequest request)
        {
            allUsers.Clear();
            var response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(UsersReaderFunction), new UsersReaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
            allUsers.AddRange(response.UsersToAdd);
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta query for group {request.SourceGroup.ObjectId}" });
                response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(SubsequentUsersReaderFunction), new SubsequentUsersReaderRequest { RunId = request.RunId, NextPageUrl = response.NextPageUrl, GroupUsersPage = response.UsersFromGroup });
                allUsers.AddRange(response.UsersToAdd);
            }
            return (allUsers, response.DeltaUrl);
        }

        public async Task<(List<AzureADUser> deltaUsersToAdd, List<AzureADUser> deltaUsersToRemove, string deltaUrl)> GetDeltaUsersReaderFunction(
                                                                                        IDurableOrchestrationContext context,
                                                                                        string fileContent,
                                                                                        List<AzureADUser> deltaUsersToAdd,
                                                                                        List<AzureADUser> deltaUsersToRemove,
                                                                                        SecurityGroupRequest request)
        {
            var response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(DeltaUsersReaderFunction), new DeltaUsersReaderRequest { DeltaLink = fileContent });
            deltaUsersToAdd.AddRange(response.UsersToAdd);
            deltaUsersToRemove.AddRange(response.UsersToRemove);
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta link for group {request.SourceGroup.ObjectId}" });
                response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(SubsequentDeltaUsersReaderFunction), new SubsequentDeltaUsersReaderRequest { RunId = request.RunId, NextPageUrl = response.NextPageUrl, GroupUsersPage = response.UsersFromGroup });
                deltaUsersToAdd.AddRange(response.UsersToAdd);
                deltaUsersToRemove.AddRange(response.UsersToRemove);
            }
            return (deltaUsersToAdd, deltaUsersToRemove, response.DeltaUrl);
        }
    }
}