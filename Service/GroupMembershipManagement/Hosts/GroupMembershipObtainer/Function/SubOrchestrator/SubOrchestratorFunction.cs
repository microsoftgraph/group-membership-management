// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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
                    var isExistingGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction), new GroupValidatorRequest { SyncJob = request.SyncJob, GroupId = request.GroupId, RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId });
                    if (!isExistingGroup)
                        return TextCompressor.Compress(JsonSerializer.Serialize(new SubOrchestratorResponse { Status = SyncStatus.SecurityGroupNotFound }));

                    var transitiveGroupCount = await context.CallActivityAsync<int>(nameof(GetTransitiveGroupCountFunction),
                                                                new GetTransitiveGroupCountRequest
                                                                {
                                                                    RunId = request.RunId,
                                                                    GroupId = request.SourceGroup.ObjectId
                                                                });

                    if (!context.IsReplaying)
                    {
                        if (request.SourceGroup.ObjectId != request.GroupId)
                        {
                            var nestedGroupEvent = new Dictionary<string, string>
                            {
                                { "SourceGroupObjectId", request.SourceGroup.ObjectId.ToString() },
                                { "Destination", $"[{{\"type\":\"{request.SyncJob.MembershipType}\",\"value\":{{\"objectId\":\"{request.GroupId}\"}}}}]" },
                                { "DestinationGroupObjectId", request.GroupId.ToString() },
                                { "NestedGroupCount", transitiveGroupCount.ToString() }
                            };
                            _telemetryClient.TrackEvent("NestedGroupCount", nestedGroupEvent);
                        }
                    }

                    if (transitiveGroupCount > 0 || !_deltaCachingConfig.DeltaCacheEnabled)
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                        await GetTransitiveMembers(context, request);
                        await ProcessGroupMembershipChangesAsync(context, request);
                        return TextCompressor.Compress(JsonSerializer.Serialize(new SubOrchestratorResponse
                        {
                            Status = SyncStatus.InProgress,
                            QueryType = QueryType.Transitive
                        }));
                    }
                    else
                    {
                        // first check if delta file exists in cache folder
                        var deltaFilePath = $"cache/delta_{request.SourceGroup.ObjectId}";
                        var compressedDeltaFileContent = await GetFileDownloaderFunction(context, deltaFilePath, request.SyncJob, true);
                        var deltaFileContent = TextCompressor.Decompress(compressedDeltaFileContent);

                        // check if cache file exists in cache folder
                        var cacheFilePath = $"cache/{request.SourceGroup.ObjectId}";
                        var compressedCacheFileContent = await GetFileDownloaderFunction(context, cacheFilePath, request.SyncJob, true);
                        var cacheFileContent = TextCompressor.Decompress(compressedCacheFileContent);

                        if (string.IsNullOrEmpty(deltaFileContent) || string.IsNullOrEmpty(cacheFileContent))
                        {
                            try
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                                var deltaLink = await GetInitialDeltaUsers(context, request);
                                await ProcessGroupMembershipChangesAsync(context, request, deltaLink);
                                return TextCompressor.Compress(JsonSerializer.Serialize(new SubOrchestratorResponse
                                {
                                    Status = SyncStatus.InProgress,
                                    QueryType = QueryType.Delta
                                }));
                            }
                            catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                            {
                                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                                allUsers.Clear();
                                allNonUserGraphObjects.Clear();

                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                                await GetTransitiveMembers(context, request);
                                await ProcessGroupMembershipChangesAsync(context, request);
                                return TextCompressor.Compress(JsonSerializer.Serialize(new SubOrchestratorResponse
                                {
                                    Status = SyncStatus.InProgress,
                                    QueryType = QueryType.Transitive
                                }));
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
                                var membership = JsonSerializer.Deserialize<GroupMembership>(cacheFileContent);
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
                                    var deltaLink = await GetInitialDeltaUsers(context, request);
                                    await ProcessGroupMembershipChangesAsync(context, request, deltaLink);

                                    // delete old cache files, only after new cache files are created
                                    if (shouldClearCache)
                                    {
                                        await ClearCacheFunction(context, cacheFilePath, request.SyncJob);
                                        await ClearCacheFunction(context, deltaFilePath, request.SyncJob);
                                    }
                                    return TextCompressor.Compress(JsonSerializer.Serialize(new SubOrchestratorResponse
                                    {
                                        Status = SyncStatus.InProgress,
                                        QueryType = QueryType.Delta
                                    }));
                                }
                                else if (countOfUsersFromAADGroup == countOfUsersFromCache)
                                {
                                    if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Number of users from {request.SourceGroup.ObjectId} ({countOfUsersFromAADGroup}) and cache ({countOfUsersFromCache}) are equal" });
                                }

                                if (request.SourceGroup.ObjectId != request.GroupId)
                                {
                                    allUsers.ForEach(x => x.SourceGroup = request.SourceGroup.ObjectId);
                                }

                                await GetDeltaUsersSenderFunction(context, request, allUsers, deltaResponse.DeltaUrl);
                            }
                            catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                            {
                                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query using delta link failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                                allUsers.Clear();

                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                                var deltaLink = await GetInitialDeltaUsers(context, request);
                                await ProcessGroupMembershipChangesAsync(context, request, deltaLink);
                                return TextCompressor.Compress(JsonSerializer.Serialize(new SubOrchestratorResponse
                                {
                                    Status = SyncStatus.InProgress,
                                    QueryType = QueryType.Delta
                                }));
                            }
                        }
                        _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"From group {request.SourceGroup.ObjectId}, read {allUsers.Count} users" });
                    }
                }
                _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

                return TextCompressor.Compress(JsonSerializer.Serialize(new SubOrchestratorResponse
                {
                    Users = allUsers,
                    Status = SyncStatus.InProgress,
                    QueryType = QueryType.DeltaLink
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

        public async Task<string> GetFileDownloaderFunction(IDurableOrchestrationContext context, string filePath, SyncJob syncJob, bool checkFileAge)
        {
            var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                              new FileDownloaderRequest
                                                              {
                                                                  FilePath = filePath,
                                                                  SyncJob = syncJob,
                                                                  CheckFileAge = checkFileAge
                                                              });
            return fileContent;
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
            var compressedUsers = TextCompressor.Compress(JsonSerializer.Serialize(allUsers));


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
        public async Task GetTransitiveMembers(IDurableOrchestrationContext context, GroupMembershipRequest request)
        {
            if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from 1st page using transitive members query for group {request.SourceGroup.ObjectId}" });
            var nextPageUrl = await context.CallActivityAsync<string>(nameof(MembersReaderFunction), new MembersReaderRequest { RunId = request.RunId, GroupId = request.SourceGroup.ObjectId, TargetGroupId = request.GroupId, CurrentPart = request.CurrentPart });
            while (!string.IsNullOrEmpty(nextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using transitive members query for group {request.SourceGroup.ObjectId}" });
                nextPageUrl = await context.CallActivityAsync<string>(nameof(SubsequentMembersReaderFunction), new SubsequentMembersReaderRequest { RunId = request.RunId,NextPageUrl = nextPageUrl, GroupId = request.SourceGroup.ObjectId, TargetGroupId = request.GroupId, CurrentPart = request.CurrentPart });
            }
        }

        /// <summary>
        /// Get Users
        /// </summary>
        /// <param name="context"></param>
        /// <param name="request"></param>
        /// <param name="deltaLink"></param>
        public async Task ProcessGroupMembershipChangesAsync(IDurableOrchestrationContext context, GroupMembershipRequest request, string deltaLink = null)
        {
            var membershipFilePath = await context.CallActivityAsync<string>(nameof(TransitiveAndDeltaUsersSenderFunction), new TransitiveAndDeltaUsersSenderRequest { SyncJob = request.SyncJob, GroupId = request.GroupId, RunId = request.RunId, CurrentPart = request.CurrentPart, Exclusionary = request.Exclusionary });
            await context.CallActivityAsync<string>(nameof(DeleteBlobFunction), new DeleteBlobRequest { GroupId = request.GroupId, RunId = request.RunId, CurrentPart = request.CurrentPart });

            if (!string.IsNullOrEmpty(deltaLink))
            {
                await context.CallActivityAsync(nameof(CacheUploaderFunction), new CacheUploaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId, FilePath = membershipFilePath });
                await context.CallActivityAsync(nameof(DeltaLinkUploaderFunction), new DeltaLinkUploaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId, DeltaLink = deltaLink });
            }
        }

        public async Task<string> GetInitialDeltaUsers(
                                                    IDurableOrchestrationContext context,
                                                    GroupMembershipRequest request)
        {
            var response = await context.CallActivityAsync<DeltaUrls>(nameof(DeltaUserReaderFunction), new DeltaUserReaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId, TargetGroupId = request.GroupId, CurrentPart = request.CurrentPart, PageCount = DELTAQUERY_PAGECOUNT });
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta query for group {request.SourceGroup.ObjectId}" });
                response = await context.CallActivityAsync<DeltaUrls>(nameof(SubsequentDeltaUserReaderFunction),
                    new SubsequentDeltaUserReaderRequest
                    {
                        RunId = request.RunId,
                        NextPageUrl = response.NextPageUrl,
                        ObjectId = request.SourceGroup.ObjectId,
                        TargetGroupId = request.GroupId,
                        CurrentPart = request.CurrentPart,
                        PageCount = DELTAQUERY_PAGECOUNT
                    });
            }

            return response.DeltaUrl;
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