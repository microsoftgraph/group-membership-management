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
using Models.ServiceBus;
using Microsoft.ApplicationInsights;
using Repositories.Contracts.InjectConfig;
using Microsoft.Graph;
using Models.Helpers;
using Models;
using Hosts.GroupMembershipObtainer;
using System.Net.Http;

namespace Hosts.GroupMembershipObtainer
{
    public class SubOrchestratorFunction
    {
        private const int DELTAQUERY_PAGECOUNT = 5;
        private const int DELTALINKQUERY_PAGECOUNT = 5;
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

        /// <summary>
        /// Run SubOrchestrator
        /// </summary>
        /// <param name="context"></param>
        /// <returns>Compressed serialized SubOrchestratorResponse</returns>
        [FunctionName(nameof(SubOrchestratorFunction))]
        public async Task<string> RunSubOrchestratorAsync([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var request = context.GetInput<GroupMembershipRequest>();
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            try
            {
                if (request != null && request.SyncJob != null)
                {
                    _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
                    var isExistingGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction), new GroupValidatorRequest { SyncJob = request.SyncJob, RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
                    if (!isExistingGroup)
                        return TextCompressor.Compress(JsonConvert.SerializeObject(new SubOrchestratorResponse { Status = SyncStatus.SecurityGroupNotFound }));

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
                                { "Destination", request.SyncJob.Destination },
                                { "NestedGroupCount", transitiveGroupCount.ToString() }
                            };
                            _telemetryClient.TrackEvent("NestedGroupCount", nestedGroupEvent);
                        }
                    }

                    if (transitiveGroupCount > 0 || !_deltaCachingConfig.DeltaCacheEnabled)
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                        // run exisiting code
                        var compressedResponse = await GetTransitiveMembers(context, request);
                        var response = JsonConvert.DeserializeObject<MembersReaderResponse>(TextCompressor.Decompress(compressedResponse));

                        allUsers.AddRange(response.Users);

                        if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                        {
                            allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                        }

                        response.NonUserGraphObjects.Where(x => !allNonUserGraphObjects.ContainsKey(x.Key)).ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));

                        var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, allNonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                        _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                    }
                    else
                    {
                        // first check if delta file exists in cache folder
                        var deltaFilePath = $"cache/delta_{request.SourceGroup.ObjectId}";
                        var compressedDeltaFileContent = await GetFileDownloaderFunction(context, deltaFilePath, request.SyncJob);
                        var deltaFileContent = TextCompressor.Decompress(compressedDeltaFileContent);

                        if (string.IsNullOrEmpty(deltaFileContent))
                        {
                            try
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                                var compressedResponse = await GetInitialDeltaUsers(context, request);
                                var response = JsonConvert.DeserializeObject<DeltaUsersReaderResponse>(TextCompressor.Decompress(compressedResponse));

                                allUsers.AddRange(response.Users);

                                if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                                {
                                    allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                                }

                                await GetDeltaUsersSenderFunction(context, request, allUsers, response.DeltaUrl);
                            }
                            catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                            {
                                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                                allUsers.Clear();
                                allNonUserGraphObjects.Clear();

                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                                // run exisiting code
                                var compressedResponse = await GetTransitiveMembers(context, request);
                                var response = JsonConvert.DeserializeObject<MembersReaderResponse>(TextCompressor.Decompress(compressedResponse));

                                allUsers.AddRange(response.Users);

                                if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                                {
                                    allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                                }

                                response.NonUserGraphObjects.Where(x => !allNonUserGraphObjects.ContainsKey(x.Key)).ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));

                                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, allNonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                            }
                        }
                        else
                        {
                            try
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query using delta link for group {request.SourceGroup.ObjectId}" });
                                var deltaResponse = await GetDeltaLinkUsers(context, deltaFileContent, request);
                                var shouldClearCache = false;

                                var deltaUsersToAdd = deltaResponse.UsersToAdd;
                                var deltaUsersToRemove = deltaResponse.UsersToRemove;
                                var filePath = $"cache/{request.SourceGroup.ObjectId}";
                                var compressedCacheFileContent = await GetFileDownloaderFunction(context, filePath, request.SyncJob);
                                var cacheFileContent = TextCompressor.Decompress(compressedCacheFileContent);
                                var membership = JsonConvert.DeserializeObject<GroupMembership>(cacheFileContent);
                                var sourceMembers = membership.SourceMembers.Distinct().ToList();
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

                                    // clear cache
                                    shouldClearCache = true;

                                    var compressedResponse = await GetInitialDeltaUsers(context, request);
                                    var response = JsonConvert.DeserializeObject<DeltaUsersReaderResponse>(TextCompressor.Decompress(compressedResponse));
                                    allUsers = response.Users;
                                }
                                else if (countOfUsersFromAADGroup == countOfUsersFromCache)
                                {
                                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Number of users from {request.SourceGroup.ObjectId} ({countOfUsersFromAADGroup}) and cache ({countOfUsersFromCache}) are equal" });
                                }

                                if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                                {
                                    allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                                }

                                await GetDeltaUsersSenderFunction(context, request, allUsers, deltaResponse.DeltaUrl);

                                if (shouldClearCache)
                                {
                                    // delete old cache files, only after new cache file is created
                                    await ClearCacheFunction(context, filePath, request.SyncJob);
                                    await ClearCacheFunction(context, deltaFilePath, request.SyncJob);
                                }
                            }
                            catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                            {
                                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query using delta link failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                                allUsers.Clear();

                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                                var compressedResponse = await GetInitialDeltaUsers(context, request);
                                var response = JsonConvert.DeserializeObject<DeltaUsersReaderResponse>(TextCompressor.Decompress(compressedResponse));

                                if (response.Users.Any())
                                    allUsers.AddRange(response.Users);

                                if (request.SourceGroup.ObjectId != request.SyncJob.TargetOfficeGroupId)
                                {
                                    allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                                }

                                await GetDeltaUsersSenderFunction(context, request, allUsers, response.DeltaUrl);
                            }
                        }
                        _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users" });
                    }
                }
                _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

                return TextCompressor.Compress(JsonConvert.SerializeObject(new SubOrchestratorResponse
                {
                    Users = allUsers,
                    Status = SyncStatus.InProgress
                }));
            }
            catch (HttpRequestException httpEx)
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Caught HttpRequestException, marking sync job status as transient error. Exception:\n{httpEx}", RunId = request.RunId });
                throw;
            }
            catch (Exception ex)
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { Message = $"Caught Exception, marking sync job status as error. Exception:\n{ex}", RunId = request.RunId });
                throw;
            }
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

        public async Task ClearCacheFunction(IDurableOrchestrationContext context, string filePath, SyncJob syncJob)
        {
            await context.CallActivityAsync(nameof(FileDeleterFunction),
                                            new FileDeleterRequest
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

        public async Task GetDeltaUsersSenderFunction(IDurableOrchestrationContext context, GroupMembershipRequest request, List<AzureADUser> allUsers, string deltaUrl)
        {
            var compressedUsers = TextCompressor.Compress(JsonConvert.SerializeObject(allUsers));


            await context.CallActivityAsync(nameof(DeltaUsersSenderFunction),
                                                    new DeltaUsersSenderRequest
                                                    {
                                                        RunId = request.RunId,
                                                        SyncJob = request.SyncJob,
                                                        ObjectId = request.SourceGroup.ObjectId,
                                                        CompressedUsers = compressedUsers,
                                                        DeltaLink = deltaUrl
                                                    });
        }

        /// <summary>
        /// Get Members
        /// </summary>
        /// <param name="context"></param>
        /// <param name="request"></param>
        /// <returns>Compressed serialized MembersReaderResponse</returns>
        public async Task<string> GetTransitiveMembers(IDurableOrchestrationContext context, GroupMembershipRequest request)
        {
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();


            var response = await context.CallActivityAsync<GroupInformation>(nameof(MembersReaderFunction), new MembersReaderRequest { RunId = request.RunId, GroupId = request.SourceGroup.ObjectId });
            allUsers.AddRange(response.Users);
            response.NonUserGraphObjects.ToList().ForEach(x => allNonUserGraphObjects.Add(x.Key, x.Value));
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using transitive members query for group {request.SourceGroup.ObjectId}" });
                response = await context.CallActivityAsync<GroupInformation>(nameof(SubsequentMembersReaderFunction), new SubsequentMembersReaderRequest { RunId = request.RunId, NextPageUrl = response.NextPageUrl });
                allUsers.AddRange(response.Users);
                response.NonUserGraphObjects.ToList().ForEach(x =>
                {
                    if (allNonUserGraphObjects.ContainsKey(x.Key))
                        allNonUserGraphObjects[x.Key] += x.Value;
                    else
                        allNonUserGraphObjects[x.Key] = x.Value;
                });
            }

            var membersResponse = new MembersReaderResponse
            {
                Users = allUsers,
                NonUserGraphObjects = allNonUserGraphObjects
            };

            return TextCompressor.Compress(JsonConvert.SerializeObject(membersResponse));
        }

        /// <summary>
        /// Get Users
        /// </summary>
        /// <param name="context"></param>
        /// <param name="request"></param>
        /// <returns>Compressed serialized UsersReaderResponse</returns>
        public async Task<string> GetInitialDeltaUsers(
                                                    IDurableOrchestrationContext context,
                                                    GroupMembershipRequest request)
        {
            var allUsers = new List<AzureADUser>();
            var response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(DeltaUserReaderFunction), new DeltaUserReaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
            allUsers.AddRange(response.UsersToAdd);
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta query for group {request.SourceGroup.ObjectId}" });
                response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(SubsequentDeltaUserReaderFunction),
                    new SubsequentDeltaUserReaderRequest { 
                        RunId = request.RunId,
                        NextPageUrl = response.NextPageUrl,
                        PageCount = DELTAQUERY_PAGECOUNT
                    });
                allUsers.AddRange(response.UsersToAdd);
            }

            var deltaUserReaderResponse = new DeltaUsersReaderResponse
            {
                Users = allUsers,
                DeltaUrl = response.DeltaUrl
            };

            return TextCompressor.Compress(JsonConvert.SerializeObject(deltaUserReaderResponse));
        }

        /// <summary>
        /// Get deltas for additions and removals
        /// </summary>
        /// <param name="context"></param>
        /// <param name="fileContent"></param>
        /// <param name="request"></param>
        /// <returns>Compressed serialized DeltaLinkUserReaderResponse</returns>
        public async Task<DeltaLinkUserReaderResponse> GetDeltaLinkUsers(
                                                                                        IDurableOrchestrationContext context,
                                                                                        string fileContent,
                                                                                        GroupMembershipRequest request)
        {

            var deltaLinkUsersToAdd = new List<AzureADUser>();
            var deltaLinkUsersToRemove = new List<AzureADUser>();

            var response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(DeltaLinkUserReaderFunction), new DeltaLinkUserReaderRequest { RunId = request.RunId, DeltaLink = fileContent });
            deltaLinkUsersToAdd.AddRange(response.UsersToAdd);
            deltaLinkUsersToRemove.AddRange(response.UsersToRemove);
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta link for group {request.SourceGroup.ObjectId}" });
                response = await context.CallActivityAsync<DeltaGroupInformation>(nameof(SubsequentDeltaLinkUserReaderFunction), 
                    new SubsequentDeltaLinkUserReaderRequest { 
                        RunId = request.RunId, 
                        NextPageUrl = response.NextPageUrl,
                        PageCount = DELTALINKQUERY_PAGECOUNT
                    });
                deltaLinkUsersToAdd.AddRange(response.UsersToAdd);
                deltaLinkUsersToRemove.AddRange(response.UsersToRemove);
            }

            var deltaLinkUserReaderResponse = new DeltaLinkUserReaderResponse
            {
                UsersToAdd = deltaLinkUsersToAdd,
                UsersToRemove = deltaLinkUsersToRemove,
                DeltaUrl = response.DeltaUrl
            };

            return deltaLinkUserReaderResponse;
        }
    }
}