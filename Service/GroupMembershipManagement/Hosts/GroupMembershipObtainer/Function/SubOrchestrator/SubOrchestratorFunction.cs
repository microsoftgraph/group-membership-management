// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs;
using Microsoft.DurableTask;
using Microsoft.Extensions.Azure;
using Microsoft.Graph;
using Models;
using Models.Helpers;
using Models.Notifications;
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
        [Function(nameof(SubOrchestratorFunction))]
        public async Task<SubOrchestratorResponse> RunSubOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
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
                        return new SubOrchestratorResponse { Status = SyncStatus.SecurityGroupNotFound };

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

                    if (request.SourceGroup.ObjectId == request.GroupId && transitiveGroupCount > 0)
                    {
                        var nestedGroups = await context.CallActivityAsync<List<AzureADGroup>>(nameof(LogNestedGroupsFunction), new LogNestedGroupsRequest { RunId = request.RunId, GroupId = request.GroupId });
                        var destinationName = await context.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), request.SyncJob);
                        var nestedGroupsInfo = string.Join("\n", nestedGroups.Select(g => $"- {g.Name} ({g.ObjectId})"));
                        var additionalContentParams = new[]
                        {
                            request.GroupId.ToString(),
                            destinationName.ToString(),
                            nestedGroups.Count.ToString(),
                            nestedGroupsInfo,
                            DisabledNotificationType.StatusDescriptions[NotificationMessageType.NestedGroupsFoundNotification]
                        };
                        await context.CallActivityAsync(nameof(EmailSenderFunction), new EmailSenderRequest
                        {
                            SyncJob = request.SyncJob,
                            NotificationType = NotificationMessageType.NestedGroupsFoundNotification,
                            AdditionalContentParams = additionalContentParams
                        });

                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.NestedGroupsFound, SyncJob = request.SyncJob });
                        return new SubOrchestratorResponse { Status = SyncStatus.NestedGroupsFound };
                    }

                    if (transitiveGroupCount > 0 || !_deltaCachingConfig.DeltaCacheEnabled)
                    {
                        if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                        await GetTransitiveMembers(context, request);
                        var membershipFilePath = await ProcessGroupMembershipChangesAsync(context, request);
                        return new SubOrchestratorResponse
                        {
                            Status = SyncStatus.InProgress,
                            FilePath = membershipFilePath
                        };
                    }
                    else
                    {
                        // first check if delta file exists in cache folder
                        var deltaFilePath = $"cache/delta_{request.SourceGroup.ObjectId}";
                        var compressedDeltaFileContent = await GetFileDownloaderFunction(context, deltaFilePath, request.SyncJob, true);
                        var deltaFileContent = TextCompressor.Decompress(compressedDeltaFileContent);

                        // check if cache file exists in cache folder
                        var cacheFilePath = CacheFileNaming.BuildCacheFileNamePrefix(request.SourceGroup.ObjectId);
                        var cacheFileResult = await context.CallActivityAsync<BlobResult>(nameof(BlobCheckerFunction),
                                                                                           new BlobCheckerRequest
                                                                                           {
                                                                                               RunId = request.RunId,
                                                                                               Prefix = cacheFilePath
                                                                                           });

                        // Convert cache from GroupMembership to TXT format if needed
                        if (cacheFileResult.BlobStatus == BlobStatus.Found
                            && cacheFileResult.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            await context.CallActivityAsync(nameof(CacheConverterFunction),
                            new CacheConverterRequest
                            {
                                RunId = request.RunId,
                                ObjectId = request.SourceGroup.ObjectId,
                                FilePath = cacheFileResult.Path
                            });

                            cacheFileResult = await context.CallActivityAsync<BlobResult>(nameof(BlobCheckerFunction),
                                                                        new BlobCheckerRequest
                                                                        {
                                                                            RunId = request.RunId,
                                                                            Prefix = cacheFilePath
                                                                        });
                        }

                        var fullCacheFilePath = cacheFileResult.Path;

                        if (string.IsNullOrEmpty(deltaFileContent) || cacheFileResult.BlobStatus == BlobStatus.NotFound)
                        {
                            try
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                                var deltaLink = await GetInitialDeltaUsers(context, request);
                                var membershipFilePath = await ProcessGroupMembershipChangesAsync(context, request, deltaLink);
                                return new SubOrchestratorResponse
                                {
                                    Status = SyncStatus.InProgress,
                                    FilePath = membershipFilePath
                                };
                            }
                            catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                            {
                                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                                allUsers.Clear();
                                allNonUserGraphObjects.Clear();

                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run transitive members query for group {request.SourceGroup.ObjectId}" });
                                await GetTransitiveMembers(context, request);
                                var membershipFilePath = await ProcessGroupMembershipChangesAsync(context, request);
                                return new SubOrchestratorResponse
                                {
                                    Status = SyncStatus.InProgress,
                                    FilePath = membershipFilePath
                                };
                            }
                        }
                        else
                        {
                            try
                            {
                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query using delta link for group {request.SourceGroup.ObjectId}" });

                                var shouldClearCache = false;
                                var deltaLink = await GetInitialDeltaLinkUsers(context, deltaFileContent, request);
                                var countOfUsersFromAADGroup = await GetUsersCountFunction(context, request.SourceGroup.ObjectId, request.RunId);

                                // If we're reading from the target group itself, store the before sync user count during delta link call
                                if (request.GroupId == request.SourceGroup.ObjectId)
                                {
                                    await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                        new JobStatusUpdaterRequest
                                        {
                                            SyncJob = request.SyncJob,
                                            Status = SyncStatus.InProgress,
                                            BeforeSyncUserCount = countOfUsersFromAADGroup
                                        });
                                }

                                var response = await ProcessCachedAndDeltaUsers(context, new ProcessCachedAndDeltaUsersRequest
                                {
                                    RunId = request.RunId,
                                    CacheFilePath = fullCacheFilePath,
                                    SourceGroupId = request.SourceGroup.ObjectId,
                                    TargetGroupId = request.GroupId,
                                    CountOfUsersFromAADGroup = countOfUsersFromAADGroup,
                                    CurrentPart = request.CurrentPart,
                                    SyncJob = request.SyncJob,
                                    DeltaUrl = deltaLink,
                                    Exclusionary = request.Exclusionary
                                });

                                if (!context.IsReplaying)
                                {
                                    TrackCachedUsersEvent(request.RunId, response.CacheCount, request.SourceGroup.ObjectId);
                                }

                                if (!response.CacheMatchesGroupCount)
                                {
                                    if(!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"{request.SourceGroup.ObjectId} has {countOfUsersFromAADGroup} users but cache {response.CacheCount} users. Running delta query..." });

                                    // clear cache
                                    shouldClearCache = true;
                                    deltaLink = await GetInitialDeltaUsers(context, request);
                                    var membershipFilePath = await ProcessGroupMembershipChangesAsync(context, request, deltaLink);

                                    // delete old cache files, only after new cache files are created
                                    if (shouldClearCache)
                                    {
                                        await ClearCacheFunction(context, cacheFilePath, request.SyncJob);
                                        await ClearCacheFunction(context, deltaFilePath, request.SyncJob);
                                    }
                                    return new SubOrchestratorResponse
                                    {
                                        Status = SyncStatus.InProgress,
                                        FilePath = membershipFilePath
                                    };
                                }
                                else
                                {
                                    return new SubOrchestratorResponse
                                    {
                                        Status = SyncStatus.InProgress,
                                        FilePath = response.MembershipFilePath
                                    };
                                }

                            }
                            catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                            {
                                _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"delta query using delta link failed for group {request.SourceGroup.ObjectId}: {e.Message}" });
                                allUsers.Clear();

                                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Run delta query for group {request.SourceGroup.ObjectId}" });
                                var deltaLink = await GetInitialDeltaUsers(context, request);
                                var membershipFilePath = await ProcessGroupMembershipChangesAsync(context, request, deltaLink);
                                return new SubOrchestratorResponse
                                {
                                    Status = SyncStatus.InProgress,
                                    FilePath = membershipFilePath
                                };
                            }
                        }
                    }
                }
                _ = _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubOrchestratorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

                return new SubOrchestratorResponse
                {
                    Status = SyncStatus.InProgress
                };
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
            finally
            {
                if (!context.IsReplaying)
                {
                    _ = _log.LogMessageAsync(new LogMessage
                    {
                        Message = $"{nameof(SubOrchestratorFunction)} function completed",
                        RunId = request?.RunId
                    }, VerbosityLevel.DEBUG);
                }
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

        public async Task<string> GetFileDownloaderFunction(TaskOrchestrationContext context, string filePath, SyncJob syncJob, bool checkFileAge)
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

        public async Task ClearCacheFunction(TaskOrchestrationContext context, string filePath, SyncJob syncJob)
        {
            await context.CallActivityAsync(nameof(FileDeleterFunction),
                                            new FileDeleterRequest
                                            {
                                                FilePath = filePath,
                                                SyncJob = syncJob
                                            });
        }

        public async Task<int> GetUsersCountFunction(TaskOrchestrationContext context, Guid groupId, Guid runId)
        {
            return await context.CallActivityAsync<int>(nameof(GetUserCountFunction),
                                            new GetUserCountRequest
                                            {
                                                RunId = runId,
                                                GroupId = groupId
                                            });
        }

        public async Task<ProcessCachedAndDeltaUsersResponse> ProcessCachedAndDeltaUsers(TaskOrchestrationContext context, ProcessCachedAndDeltaUsersRequest request)
        {
            var response = await context.CallActivityAsync<ProcessCachedAndDeltaUsersResponse>(nameof(ProcessCachedAndDeltaUsersFunction), request);

            return response;
        }

        /// <summary>
        /// Get Members
        /// </summary>
        /// <param name="context"></param>
        /// <param name="request"></param>
        public async Task GetTransitiveMembers(TaskOrchestrationContext context, GroupMembershipRequest request)
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
        /// <returns>Membership file path</returns>
        public async Task<string> ProcessGroupMembershipChangesAsync(TaskOrchestrationContext context, GroupMembershipRequest request, string deltaLink = null)
        {
            var membershipFileResult = await context.CallActivityAsync<GroupMembershipFileResult>(nameof(TransitiveAndDeltaUsersSenderFunction), new TransitiveAndDeltaUsersSenderRequest { SyncJob = request.SyncJob, ObjectId = request.SourceGroup.ObjectId, GroupId = request.GroupId, RunId = request.RunId, CurrentPart = request.CurrentPart, Exclusionary = request.Exclusionary });
            await context.CallActivityAsync<string>(nameof(DeleteBlobFunction), new DeleteBlobRequest { GroupId = request.GroupId, RunId = request.RunId, CurrentPart = request.CurrentPart });

            if (!string.IsNullOrEmpty(deltaLink))
            {
                await context.CallActivityAsync(nameof(CacheUploaderFunction), new CacheUploaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId, MembershipFileResult = membershipFileResult });
                await context.CallActivityAsync(nameof(DeltaLinkUploaderFunction), new DeltaLinkUploaderRequest { RunId = request.RunId, ObjectId = request.SourceGroup.ObjectId, DeltaLink = deltaLink });
            }

            return membershipFileResult.FilePath;
        }

        public async Task<string> GetInitialDeltaUsers(
                                                    TaskOrchestrationContext context,
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

        public async Task<string> GetInitialDeltaLinkUsers(TaskOrchestrationContext context, string fileContent, GroupMembershipRequest request)
        {
            var response = await context.CallActivityAsync<DeltaUrls>(nameof(DeltaLinkUserReaderFunction), 
                new DeltaLinkUserReaderRequest { 
                    RunId = request.RunId, 
                    GroupId = request.SourceGroup.ObjectId, 
                    TargetGroupId = request.GroupId, 
                    CurrentPart = request.CurrentPart, 
                    DeltaLink = fileContent,
                    NumberOfPages = DELTALINKQUERY_PAGECOUNT
                });
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                if (!context.IsReplaying) _ = _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Getting results from next page using delta link members query for group {request.SourceGroup.ObjectId}" });
                response = await context.CallActivityAsync<DeltaUrls>(nameof(SubsequentDeltaLinkUserReaderFunction), 
                    new SubsequentDeltaLinkUserReaderRequest 
                    { 
                        RunId = request.RunId, 
                        NextPageUrl = response.NextPageUrl, 
                        GroupId = request.SourceGroup.ObjectId, 
                        TargetGroupId = request.GroupId, 
                        CurrentPart = request.CurrentPart,
                        PageCount = DELTALINKQUERY_PAGECOUNT
                    });
            }
            return response.DeltaUrl;
        }
    }
}