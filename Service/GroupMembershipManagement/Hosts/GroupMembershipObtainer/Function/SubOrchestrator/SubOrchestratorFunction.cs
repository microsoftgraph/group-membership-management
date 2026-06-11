// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Models;
using Models.Helpers;
using Models.Notifications;
using Repositories.Contracts.Helpers;
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
        private readonly TelemetryClient _telemetryClient;

        public SubOrchestratorFunction(
            IDeltaCachingConfig deltaCachingConfig,
            TelemetryClient telemetryClient)
        {
            _deltaCachingConfig = deltaCachingConfig;
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
            var logger = context.CreateReplaySafeLogger("GroupMembershipObtainer.SubOrchestratorFunction");
            var allUsers = new List<AzureADUser>();
            var allNonUserGraphObjects = new Dictionary<string, int>();

            using (logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                try
                {
                    if (request != null && request.SyncJob != null)
                    {
                        logger.FunctionStarted(nameof(SubOrchestratorFunction));
                        var isExistingGroup = await context.CallActivityAsync<bool>(nameof(GroupValidatorFunction), new GroupValidatorRequest { SyncJob = request.SyncJob, GroupId = request.GroupId, CurrentPart = request.CurrentPart, TotalParts = request.TotalParts, ObjectId = request.SourceGroup.ObjectId });
                        if (!isExistingGroup)
                            return new SubOrchestratorResponse { Status = SyncStatus.SecurityGroupNotFound };

                        var transitiveGroupCount = await context.CallActivityAsync<int>(nameof(GetTransitiveGroupCountFunction),
                                                                    new GetTransitiveGroupCountRequest
                                                                    {
                                                                        SyncJob = request.SyncJob,
                                                                        CurrentPart = request.CurrentPart,
                                                                        TotalParts = request.TotalParts,
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
                            var nestedGroups = await context.CallActivityAsync<List<AzureADGroup>>(nameof(LogNestedGroupsFunction), new LogNestedGroupsRequest { SyncJob = request.SyncJob, CurrentPart = request.CurrentPart, TotalParts = request.TotalParts, GroupId = request.GroupId });
                            var destinationName = await context.CallActivityAsync<string>(nameof(DestinationNameReaderFunction), new DestinationNameReaderRequest { SyncJob = request.SyncJob, CurrentPart = request.CurrentPart, TotalParts = request.TotalParts });
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
                                CurrentPart = request.CurrentPart,
                                TotalParts = request.TotalParts,
                                NotificationType = NotificationMessageType.NestedGroupsFoundNotification,
                                AdditionalContentParams = additionalContentParams
                            });

                            await context.CallActivityAsync(nameof(JobStatusUpdaterFunction), new JobStatusUpdaterRequest { Status = SyncStatus.NestedGroupsFound, SyncJob = request.SyncJob, CurrentPart = request.CurrentPart, TotalParts = request.TotalParts });
                            return new SubOrchestratorResponse { Status = SyncStatus.NestedGroupsFound };
                        }

                        if (transitiveGroupCount > 0 || !_deltaCachingConfig.DeltaCacheEnabled)
                        {
                            logger.RunTransitiveMembersQuery(request.SourceGroup.ObjectId);
                            await GetTransitiveMembers(context, request, logger);
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
                            var compressedDeltaFileContent = await GetFileDownloaderFunction(context, deltaFilePath, request, true);
                            var deltaFileContent = TextCompressor.Decompress(compressedDeltaFileContent);

                            // check if cache file exists in cache folder
                            var cacheFilePath = CacheFileNaming.BuildCacheFileNamePrefix(request.SourceGroup.ObjectId);
                            var cacheFileResult = await context.CallActivityAsync<BlobResult>(nameof(BlobCheckerFunction),
                                                                                                new BlobCheckerRequest
                                                                                                {
                                                                                                    SyncJob = request.SyncJob,
                                                                                                    CurrentPart = request.CurrentPart,
                                                                                                    TotalParts = request.TotalParts,
                                                                                                    Prefix = cacheFilePath
                                                                                                });

                            // Convert cache from GroupMembership to TXT format if needed
                            if (cacheFileResult.BlobStatus == BlobStatus.Found
                                && cacheFileResult.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            {
                                await context.CallActivityAsync(nameof(CacheConverterFunction),
                                new CacheConverterRequest
                                {
                                    SyncJob = request.SyncJob,
                                    CurrentPart = request.CurrentPart,
                                    TotalParts = request.TotalParts,
                                    ObjectId = request.SourceGroup.ObjectId,
                                    FilePath = cacheFileResult.Path
                                });

                                cacheFileResult = await context.CallActivityAsync<BlobResult>(nameof(BlobCheckerFunction),
                                                                            new BlobCheckerRequest
                                                                            {
                                                                                SyncJob = request.SyncJob,
                                                                                CurrentPart = request.CurrentPart,
                                                                                TotalParts = request.TotalParts,
                                                                                Prefix = cacheFilePath
                                                                            });
                            }

                            var fullCacheFilePath = cacheFileResult.Path;

                            if (string.IsNullOrEmpty(deltaFileContent) || cacheFileResult.BlobStatus == BlobStatus.NotFound)
                            {
                                try
                                {
                                    logger.RunDeltaQuery(request.SourceGroup.ObjectId);
                                    var deltaLink = await GetInitialDeltaUsers(context, request, logger);
                                    var membershipFilePath = await ProcessGroupMembershipChangesAsync(context, request, deltaLink);
                                    return new SubOrchestratorResponse
                                    {
                                        Status = SyncStatus.InProgress,
                                        FilePath = membershipFilePath
                                    };
                                }
                                catch (Exception e) when (e is KeyNotFoundException || e is ServiceException)
                                {
                                    logger.DeltaQueryFailed(request.SourceGroup.ObjectId, e.Message);
                                    allUsers.Clear();
                                    allNonUserGraphObjects.Clear();

                                    logger.RunTransitiveMembersQuery(request.SourceGroup.ObjectId);
                                    await GetTransitiveMembers(context, request, logger);
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
                                    logger.RunDeltaLinkQuery(request.SourceGroup.ObjectId);

                                    var shouldClearCache = false;
                                    var deltaLink = await GetInitialDeltaLinkUsers(context, deltaFileContent, request, logger);
                                    var countOfUsersFromAADGroup = await GetUsersCountFunction(context, request);

                                    // If we're reading from the target group itself, store the before sync user count during delta link call.
                                    // Status is intentionally not set on this request: this call's intent is solely to persist BeforeSyncUserCount on
                                    // SyncJobHistory. This prevents overwriting a terminal status that may have been set by another concurrent operation.
                                    if (request.GroupId == request.SourceGroup.ObjectId)
                                    {
                                        await context.CallActivityAsync(nameof(JobStatusUpdaterFunction),
                                            new JobStatusUpdaterRequest
                                            {
                                                SyncJob = request.SyncJob,
                                                CurrentPart = request.CurrentPart,
                                                TotalParts = request.TotalParts,
                                                BeforeSyncUserCount = countOfUsersFromAADGroup
                                            });
                                    }

                                    var response = await ProcessCachedAndDeltaUsers(context, new ProcessCachedAndDeltaUsersRequest
                                    {
                                        SyncJob = request.SyncJob,
                                        TotalParts = request.TotalParts,
                                        CacheFilePath = fullCacheFilePath,
                                        SourceGroupId = request.SourceGroup.ObjectId,
                                        TargetGroupId = request.GroupId,
                                        CountOfUsersFromAADGroup = countOfUsersFromAADGroup,
                                        CurrentPart = request.CurrentPart,
                                        DeltaUrl = deltaLink,
                                        Exclusionary = request.Exclusionary
                                    });

                                    if (!context.IsReplaying)
                                    {
                                        TrackCachedUsersEvent(request.SyncJob.RunId.GetValueOrDefault(), response.CacheCount, request.SourceGroup.ObjectId);
                                    }

                                    if (!response.CacheMatchesGroupCount)
                                    {
                                        logger.CacheMismatchRunningDelta(request.SourceGroup.ObjectId, countOfUsersFromAADGroup, response.CacheCount);

                                        // clear cache
                                        shouldClearCache = true;
                                        deltaLink = await GetInitialDeltaUsers(context, request, logger);
                                        var membershipFilePath = await ProcessGroupMembershipChangesAsync(context, request, deltaLink);

                                        // delete old cache files, only after new cache files are created
                                        if (shouldClearCache)
                                        {
                                            await ClearCacheFunction(context, cacheFilePath, request);
                                            await ClearCacheFunction(context, deltaFilePath, request);
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
                                    logger.DeltaLinkQueryFailed(request.SourceGroup.ObjectId, e.Message);
                                    allUsers.Clear();

                                    logger.RunDeltaQuery(request.SourceGroup.ObjectId);
                                    var deltaLink = await GetInitialDeltaUsers(context, request, logger);
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
                    logger.FunctionCompleted(nameof(SubOrchestratorFunction));

                    return new SubOrchestratorResponse
                    {
                        Status = SyncStatus.InProgress
                    };
                }
                catch (HttpRequestException httpEx)
                {
                    logger.SubOrchestratorHttpException(httpEx);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.SubOrchestratorUnexpectedException(ex);
                    throw;
                }
                finally
                {
                    logger.FunctionCompleted(nameof(SubOrchestratorFunction));
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

        public async Task<string> GetFileDownloaderFunction(TaskOrchestrationContext context, string filePath, GroupMembershipRequest request, bool checkFileAge)
        {
            var fileContent = await context.CallActivityAsync<string>(nameof(FileDownloaderFunction),
                                                              new FileDownloaderRequest
                                                              {
                                                                  FilePath = filePath,
                                                                  SyncJob = request.SyncJob,
                                                                  CurrentPart = request.CurrentPart,
                                                                  TotalParts = request.TotalParts,
                                                                  CheckFileAge = checkFileAge
                                                              });
            return fileContent;
        }

        public async Task ClearCacheFunction(TaskOrchestrationContext context, string filePath, GroupMembershipRequest request)
        {
            await context.CallActivityAsync(nameof(FileDeleterFunction),
                                            new FileDeleterRequest
                                            {
                                                FilePath = filePath,
                                                SyncJob = request.SyncJob,
                                                CurrentPart = request.CurrentPart,
                                                TotalParts = request.TotalParts
                                            });
        }

        public async Task<int> GetUsersCountFunction(TaskOrchestrationContext context, GroupMembershipRequest request)
        {
            return await context.CallActivityAsync<int>(nameof(GetUserCountFunction),
                                            new GetUserCountRequest
                                            {
                                                SyncJob = request.SyncJob,
                                                CurrentPart = request.CurrentPart,
                                                TotalParts = request.TotalParts,
                                                GroupId = request.SourceGroup.ObjectId
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
        /// <param name="logger"></param>
        public async Task GetTransitiveMembers(TaskOrchestrationContext context, GroupMembershipRequest request, ILogger logger)
        {
            logger.LogInformation("Getting results from 1st page using transitive members query for group {GroupId}", request.SourceGroup.ObjectId);
            var nextPageUrl = await context.CallActivityAsync<string>(nameof(MembersReaderFunction), new MembersReaderRequest { SyncJob = request.SyncJob, TotalParts = request.TotalParts, GroupId = request.SourceGroup.ObjectId, TargetGroupId = request.GroupId, CurrentPart = request.CurrentPart });
            while (!string.IsNullOrEmpty(nextPageUrl))
            {
                logger.LogInformation("Getting results from next page using transitive members query for group {GroupId}", request.SourceGroup.ObjectId);
                nextPageUrl = await context.CallActivityAsync<string>(nameof(SubsequentMembersReaderFunction), new SubsequentMembersReaderRequest { SyncJob = request.SyncJob, TotalParts = request.TotalParts, NextPageUrl = nextPageUrl, GroupId = request.SourceGroup.ObjectId, TargetGroupId = request.GroupId, CurrentPart = request.CurrentPart });
            }
        }

        /// <summary>
        /// Get Users
        /// </summary>
        /// <param name="context"></param>
        /// <param name="request"></param>
        /// <param name="logger"></param>
        /// <returns>Membership file path</returns>
        public async Task<string> ProcessGroupMembershipChangesAsync(TaskOrchestrationContext context, GroupMembershipRequest request, string deltaLink = null)
        {
            var membershipFileResult = await context.CallActivityAsync<GroupMembershipFileResult>(nameof(TransitiveAndDeltaUsersSenderFunction), new TransitiveAndDeltaUsersSenderRequest { SyncJob = request.SyncJob, ObjectId = request.SourceGroup.ObjectId, GroupId = request.GroupId, TotalParts = request.TotalParts, CurrentPart = request.CurrentPart, Exclusionary = request.Exclusionary });
            await context.CallActivityAsync<string>(nameof(DeleteBlobFunction), new DeleteBlobRequest { GroupId = request.GroupId, SyncJob = request.SyncJob, TotalParts = request.TotalParts, CurrentPart = request.CurrentPart });

            if (!string.IsNullOrEmpty(deltaLink))
            {
                await context.CallActivityAsync(nameof(CacheUploaderFunction), new CacheUploaderRequest { SyncJob = request.SyncJob, CurrentPart = request.CurrentPart, TotalParts = request.TotalParts, ObjectId = request.SourceGroup.ObjectId, MembershipFileResult = membershipFileResult });
                await context.CallActivityAsync(nameof(DeltaLinkUploaderFunction), new DeltaLinkUploaderRequest { SyncJob = request.SyncJob, CurrentPart = request.CurrentPart, TotalParts = request.TotalParts, ObjectId = request.SourceGroup.ObjectId, DeltaLink = deltaLink });
            }

            return membershipFileResult.FilePath;
        }

        public async Task<string> GetInitialDeltaUsers(
                                                    TaskOrchestrationContext context,
                                                    GroupMembershipRequest request,
                                                    ILogger logger)
        {
            var response = await context.CallActivityAsync<DeltaUrls>(nameof(DeltaUserReaderFunction), new DeltaUserReaderRequest { SyncJob = request.SyncJob, TotalParts = request.TotalParts, ObjectId = request.SourceGroup.ObjectId, TargetGroupId = request.GroupId, CurrentPart = request.CurrentPart, PageCount = DELTAQUERY_PAGECOUNT });
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                logger.LogInformation("Getting results from next page using delta query for group {GroupId}", request.SourceGroup.ObjectId);
                response = await context.CallActivityAsync<DeltaUrls>(nameof(SubsequentDeltaUserReaderFunction),
                    new SubsequentDeltaUserReaderRequest
                    {
                        SyncJob = request.SyncJob,
                        TotalParts = request.TotalParts,
                        NextPageUrl = response.NextPageUrl,
                        ObjectId = request.SourceGroup.ObjectId,
                        TargetGroupId = request.GroupId,
                        CurrentPart = request.CurrentPart,
                        PageCount = DELTAQUERY_PAGECOUNT
                    });
            }

            return response.DeltaUrl;
        }

        public async Task<string> GetInitialDeltaLinkUsers(TaskOrchestrationContext context, string fileContent, GroupMembershipRequest request, ILogger logger)
        {
            var response = await context.CallActivityAsync<DeltaUrls>(nameof(DeltaLinkUserReaderFunction), 
                new DeltaLinkUserReaderRequest { 
                    SyncJob = request.SyncJob, 
                    TotalParts = request.TotalParts, 
                    GroupId = request.SourceGroup.ObjectId, 
                    TargetGroupId = request.GroupId, 
                    CurrentPart = request.CurrentPart, 
                    DeltaLink = fileContent,
                    NumberOfPages = DELTALINKQUERY_PAGECOUNT
                });
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                logger.LogInformation("Getting results from next page using delta link members query for group {GroupId}", request.SourceGroup.ObjectId);
                response = await context.CallActivityAsync<DeltaUrls>(nameof(SubsequentDeltaLinkUserReaderFunction), 
                    new SubsequentDeltaLinkUserReaderRequest 
                    { 
                        SyncJob = request.SyncJob, 
                        TotalParts = request.TotalParts, 
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