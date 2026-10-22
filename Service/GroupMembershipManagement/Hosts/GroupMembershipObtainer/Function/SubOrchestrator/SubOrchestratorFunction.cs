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
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class SubOrchestratorFunction
    {
        private const int DeltaQueryPageCount = 5;
        private const int DeltaLinkQueryPageCount = 5;
        private const string ReadPathTransitive = "Transitive";
        private const string ReadPathDelta = "Delta";

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
        /// Reads one source or destination group and returns the membership file produced for the job part.
        /// </summary>
        /// <param name="context">
        /// The durable orchestration context whose input is a <see cref="GroupMembershipRequest"/>.
        /// </param>
        /// <returns>
        /// The part status and membership file path. Missing or nested source groups return their existing
        /// terminal status; successful reads return <see cref="SyncStatus.InProgress"/>.
        /// </returns>
        [Function(nameof(SubOrchestratorFunction))]
        public async Task<SubOrchestratorResponse> RunSubOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var request = context.GetInput<GroupMembershipRequest>();
            if (request?.SyncJob == null)
            {
                return new SubOrchestratorResponse { Status = SyncStatus.InProgress };
            }

            var logger = context.CreateReplaySafeLogger(
                "GroupMembershipObtainer.SubOrchestratorFunction");

            using (logger.BeginSyncJobScope(
                request.SyncJob,
                new Dictionary<string, object>
                {
                    ["CurrentPart"] = request.CurrentPart,
                    ["TotalParts"] = request.TotalParts
                }))
            {
                try
                {
                    logger.FunctionStarted(nameof(SubOrchestratorFunction));

                    var isExistingGroup = await context.CallActivityAsync<bool>(
                        nameof(GroupValidatorFunction),
                        new GroupValidatorRequest
                        {
                            SyncJob = request.SyncJob,
                            GroupId = request.GroupId,
                            CurrentPart = request.CurrentPart,
                            TotalParts = request.TotalParts,
                            ObjectId = request.SourceGroup.ObjectId
                        });
                    if (!isExistingGroup)
                    {
                        return new SubOrchestratorResponse
                        {
                            Status = SyncStatus.SourceGroupNotFound
                        };
                    }

                    var transitiveGroupCount = await context.CallActivityAsync<int>(
                        nameof(GetTransitiveGroupCountFunction),
                        new GetTransitiveGroupCountRequest
                        {
                            SyncJob = request.SyncJob,
                            CurrentPart = request.CurrentPart,
                            TotalParts = request.TotalParts,
                            GroupId = request.SourceGroup.ObjectId
                        });

                    TrackNestedGroupCount(context, request, transitiveGroupCount);

                    if (request.SourceGroup.ObjectId == request.GroupId &&
                        transitiveGroupCount > 0)
                    {
                        return await HandleNestedDestinationGroupAsync(
                            context,
                            request);
                    }

                    // One clock per invocation: each read attempt in this execution gets its own timestamp.
                    var clock = new ReadAttemptClock();
                    GroupMembershipFileResult readResult;
                    if (transitiveGroupCount > 0 || !_deltaCachingConfig.DeltaCacheEnabled)
                    {
                        readResult = await RunFullReadAsync(context, clock, request, logger);
                    }
                    else
                    {
                        readResult = await RunIncrementalReadAsync(context, clock, request, logger);
                    }

                    return new SubOrchestratorResponse
                    {
                        Status = SyncStatus.InProgress,
                        FilePath = readResult.FilePath
                    };
                }
                catch (HttpRequestException httpException)
                {
                    logger.SubOrchestratorHttpException(httpException);
                    throw;
                }
                catch (TaskFailedException exception) when (OrchestratorFunction.IsRetryExhaustion(exception))
                {
                    // A reader or selection activity exhausted its retries or ran past the function timeout. Surface it as
                    // the top-level failure of this sub-orchestration, so the parent sets TransientError without depending
                    // on nested failure details.
                    logger.SubOrchestratorUnexpectedException(exception);
                    throw new GraphRetriesExhaustedException(
                        $"A Graph request for group {request.SourceGroup.ObjectId} still failed after the configured retries. No membership is published for this part.",
                        exception);
                }
                catch (Exception exception)
                {
                    logger.SubOrchestratorUnexpectedException(exception);
                    throw;
                }
                finally
                {
                    logger.FunctionCompleted(nameof(SubOrchestratorFunction));
                }
            }
        }

        private async Task<GroupMembershipFileResult> RunIncrementalReadAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger)
        {
            if (!GroupIdentitySelection.IsOneType(request.IdentityTypes))
            {
                return await RunIncrementalReadCoreAsync(context, clock, request, logger);
            }

            try
            {
                return await RunIncrementalReadCoreAsync(context, clock, request, logger);
            }
            finally
            {
                // Runs on every exit, including an activity failure that arrives as TaskFailedException (I6).
                await TryCleanupAsync(context, request, logger);
            }
        }

        private async Task<GroupMembershipFileResult> RunIncrementalReadCoreAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger)
        {
            var deltaFilePath = $"cache/delta_{request.SourceGroup.ObjectId}";
            var compressedDeltaFileContent = await GetFileDownloaderFunction(
                context,
                deltaFilePath,
                request,
                checkFileAge: true);
            var deltaFileContent = TextCompressor.Decompress(compressedDeltaFileContent);

            var cacheFilePath = CacheFileNaming.BuildCacheFileNamePrefix(
                request.SourceGroup.ObjectId);
            var cacheFileResult = await context.CallActivityAsync<BlobResult>(
                nameof(BlobCheckerFunction),
                new BlobCheckerRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    Prefix = cacheFilePath
                });

            if (cacheFileResult.BlobStatus == BlobStatus.Found &&
                cacheFileResult.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                await context.CallActivityAsync(
                    nameof(CacheConverterFunction),
                    new CacheConverterRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = request.CurrentPart,
                        TotalParts = request.TotalParts,
                        ObjectId = request.SourceGroup.ObjectId,
                        FilePath = cacheFileResult.Path,
                        FileTimestamp = clock.Next(context.CurrentUtcDateTime)
                    });

                cacheFileResult = await context.CallActivityAsync<BlobResult>(
                    nameof(BlobCheckerFunction),
                    new BlobCheckerRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = request.CurrentPart,
                        TotalParts = request.TotalParts,
                        Prefix = cacheFilePath
                    });
            }

            if (string.IsNullOrEmpty(deltaFileContent) ||
                cacheFileResult.BlobStatus == BlobStatus.NotFound)
            {
                try
                {
                    return await RunInitialDeltaReadAsync(context, clock, request, logger);
                }
                catch (Exception exception) when (
                    exception is KeyNotFoundException ||
                    exception is ServiceException)
                {
                    logger.DeltaQueryFailed(
                        request.SourceGroup.ObjectId,
                        exception.Message);
                    if (GroupIdentitySelection.IsOneType(request.IdentityTypes))
                    {
                        await CleanupBeforeReplacementReadAsync(context, request, logger);
                    }

                    return await RunFullReadAsync(context, clock, request, logger);
                }
            }

            try
            {
                return await RunDeltaLinkReadAsync(
                    context,
                    clock,
                    request,
                    logger,
                    deltaFileContent,
                    cacheFileResult.Path,
                    cacheFilePath,
                    deltaFilePath);
            }
            catch (Exception exception) when (
                exception is KeyNotFoundException ||
                exception is ServiceException)
            {
                logger.DeltaLinkQueryFailed(
                    request.SourceGroup.ObjectId,
                    exception.Message);
                if (GroupIdentitySelection.IsOneType(request.IdentityTypes))
                {
                    await CleanupBeforeReplacementReadAsync(context, request, logger);
                }

                return await RunInitialDeltaReadAsync(context, clock, request, logger);
            }
        }

        private async Task<GroupMembershipFileResult> RunFullReadAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger,
            bool writeCompletion = true)
        {
            logger.RunTransitiveMembersQuery(request.SourceGroup.ObjectId);
            await GetTransitiveMembers(context, request, logger);
            var result = await ProcessGroupMembershipChangesAsync(
                context,
                request,
                deltaLink: null,
                writeStaging: false,
                GroupMembershipReadKind.Full,
                readAttemptClock: clock);

            await TrackAgentMembersReadEventIfNeededAsync(
                context,
                request,
                ReadPathTransitive);
            if (writeCompletion)
            {
                GroupReadLogWriter.ReadCompleted(
                    logger,
                    result.MemberCount,
                    request.SourceGroup.ObjectId,
                    request.GroupId,
                    GroupMembershipReadKind.Full,
                    request.IdentityTypes);
            }

            return result;
        }

        private async Task<GroupMembershipFileResult> RunInitialDeltaReadAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger,
            bool trackAgentMembersRead = true,
            bool writeCompletion = true)
        {
            var completedRead = await RunInitialDeltaReadWithKindAsync(
                context,
                clock,
                request,
                logger,
                trackAgentMembersRead,
                writeCompletion);
            return completedRead.Result;
        }

        private async Task<CompletedReadResult> RunInitialDeltaReadWithKindAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger,
            bool trackAgentMembersRead = true,
            bool writeCompletion = true)
        {
            logger.RunDeltaQuery(request.SourceGroup.ObjectId);
            var deltaLink = await GetInitialDeltaUsers(context, request, logger);
            var oneTypeSelection = GroupIdentitySelection.IsOneType(request.IdentityTypes);
            var result = await ProcessGroupMembershipChangesAsync(
                context,
                request,
                deltaLink,
                oneTypeSelection,
                GroupMembershipReadKind.DeltaInitial,
                readAttemptClock: clock);

            if (oneTypeSelection)
            {
                return await CompleteSelectedIncrementalReadWithKindAsync(
                    context,
                    clock,
                    request,
                    logger,
                    result.FilePath,
                    GroupPartStoragePaths.BuildStagingPath(
                        request.GroupId,
                        request.SyncJob.RunId.GetValueOrDefault(),
                        request.CurrentPart),
                    GroupMembershipReadKind.DeltaInitial,
                    writeCompletion);
            }

            if (trackAgentMembersRead)
            {
                await TrackAgentMembersReadEventIfNeededAsync(
                    context,
                    request,
                    ReadPathDelta);
            }

            if (writeCompletion)
            {
                GroupReadLogWriter.ReadCompleted(
                    logger,
                    result.MemberCount,
                    request.SourceGroup.ObjectId,
                    request.GroupId,
                    GroupMembershipReadKind.DeltaInitial,
                    request.IdentityTypes);
            }
            return new CompletedReadResult(
                result,
                GroupMembershipReadKind.DeltaInitial);
        }

        private async Task<GroupMembershipFileResult> RunDeltaLinkReadAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger,
            string deltaFileContent,
            string cacheFilePath,
            string cachePrefix,
            string deltaFilePath)
        {
            logger.RunDeltaLinkQuery(request.SourceGroup.ObjectId);
            var oneTypeSelection = GroupIdentitySelection.IsOneType(request.IdentityTypes);
            int groupMemberCount;
            ProcessCachedAndDeltaUsersResponse response;
            try
            {
                var deltaLink = await GetInitialDeltaLinkUsers(
                    context,
                    deltaFileContent,
                    request,
                    logger);
                groupMemberCount = await GetUsersCountFunction(context, request);

                if (!oneTypeSelection &&
                    request.GroupId == request.SourceGroup.ObjectId)
                {
                    await context.CallActivityAsync(
                        nameof(JobStatusUpdaterFunction),
                        new JobStatusUpdaterRequest
                        {
                            SyncJob = request.SyncJob,
                            CurrentPart = request.CurrentPart,
                            TotalParts = request.TotalParts,
                            BeforeSyncUserCount = groupMemberCount
                        });
                }

                response = await ProcessCachedAndDeltaUsers(
                    context,
                    new ProcessCachedAndDeltaUsersRequest
                    {
                        SyncJob = request.SyncJob,
                        TotalParts = request.TotalParts,
                        CacheFilePath = cacheFilePath,
                        SourceGroupId = request.SourceGroup.ObjectId,
                        TargetGroupId = request.GroupId,
                        CountOfUsersFromAADGroup = groupMemberCount,
                        CurrentPart = request.CurrentPart,
                        DeltaUrl = deltaLink,
                        Exclusionary = request.Exclusionary,
                        IdentityTypes = request.IdentityTypes,
                        WriteStaging = oneTypeSelection,
                        ReadKind = GroupMembershipReadKind.DeltaLink,
                        FileTimestamp = clock.Next(context.CurrentUtcDateTime)
                    });
            }
            finally
            {
                // The processing activity no longer deletes its inputs, so a retried or duplicate execution
                // reads the same saved delta. Delete them once the read has finished, on every outcome.
                await TryDeleteSavedDeltaInputsAsync(context, request, logger);
            }

            if (!context.IsReplaying)
            {
                TrackCachedUsersEvent(
                    request.SyncJob.RunId.GetValueOrDefault(),
                    response.CacheCount,
                    request.SourceGroup.ObjectId);
            }

            if (!response.CacheMatchesGroupCount)
            {
                if (oneTypeSelection)
                {
                    await CleanupBeforeReplacementReadAsync(context, request, logger);
                }

                logger.CacheMismatchRunningDelta(
                    request.SourceGroup.ObjectId,
                    groupMemberCount,
                    response.CacheCount);

                var completedRead = await RunInitialDeltaReadWithKindAsync(
                    context,
                    clock,
                    request,
                    logger,
                    trackAgentMembersRead: false,
                    writeCompletion: false);
                await ClearCacheFunction(context, cachePrefix, request);
                await ClearCacheFunction(context, deltaFilePath, request);
                await TrackAgentMembersReadEventIfNeededAsync(
                    context,
                    request,
                    ReadPathDelta);
                GroupReadLogWriter.ReadCompleted(
                    logger,
                    completedRead.Result.MemberCount,
                    request.SourceGroup.ObjectId,
                    request.GroupId,
                    completedRead.ReadKind,
                    request.IdentityTypes);
                return completedRead.Result;
            }

            if (oneTypeSelection)
            {
                if (string.IsNullOrWhiteSpace(response.StagingPath))
                {
                    throw new InvalidOperationException(
                        "A cache-matched selected delta-link read did not return its staging path.");
                }

                return await CompleteSelectedIncrementalReadAsync(
                    context,
                    clock,
                    request,
                    logger,
                    response.MembershipFilePath,
                    response.StagingPath,
                    GroupMembershipReadKind.DeltaLink);
            }

            await TrackAgentMembersReadEventIfNeededAsync(
                context,
                request,
                ReadPathDelta);
            GroupReadLogWriter.ReadCompleted(
                logger,
                response.CacheCount,
                request.SourceGroup.ObjectId,
                request.GroupId,
                GroupMembershipReadKind.DeltaLink,
                request.IdentityTypes);
            return new GroupMembershipFileResult
            {
                FilePath = response.MembershipFilePath,
                MemberCount = response.CacheCount
            };
        }

        private async Task<GroupMembershipFileResult> CompleteSelectedIncrementalReadAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger,
            string partFilePath,
            string stagingPath,
            GroupMembershipReadKind readKind,
            bool writeCompletion = true)
        {
            var completedRead = await CompleteSelectedIncrementalReadWithKindAsync(
                context,
                clock,
                request,
                logger,
                partFilePath,
                stagingPath,
                readKind,
                writeCompletion);
            return completedRead.Result;
        }

        private async Task<CompletedReadResult> CompleteSelectedIncrementalReadWithKindAsync(
            TaskOrchestrationContext context,
            ReadAttemptClock clock,
            GroupMembershipRequest request,
            ILogger logger,
            string partFilePath,
            string stagingPath,
            GroupMembershipReadKind readKind,
            bool writeCompletion = true)
        {
            var selectionRequest = new GroupPartSelectionRequest
            {
                SyncJob = request.SyncJob,
                CurrentPart = request.CurrentPart,
                TotalParts = request.TotalParts,
                SourceGroupId = request.SourceGroup.ObjectId,
                TargetGroupId = request.GroupId,
                IdentityTypes = request.IdentityTypes,
                ReadKind = readKind,
                PartFilePath = partFilePath,
                StagingPath = stagingPath
            };

            // Prepare, classify (inline up to 100 segments, then child orchestrations of up to 100), finalize. Any
            // Failed outcome, 401/403 refusal, or TaskFailedException from these steps falls back to the full read. A
            // lookup that still fails after the shared retry policies' attempts fails the part instead (no fallback,
            // nothing published, job TransientError). Cleanup runs in the caller's finally and before the replacement read.
            string fallbackReason = null;
            string retriesExhausted = null;
            GroupPartSelectionResult finalized = null;
            try
            {
                var prepared = await context.CallActivityAsync<GroupPartPrepareResult>(
                    nameof(GroupPartSelectionPrepareFunction),
                    selectionRequest);
                if (prepared?.Outcome?.Status != SelectionOutcomeStatus.Completed)
                {
                    fallbackReason = SegmentClassificationLoop.IdentificationFailed;
                }
                else
                {
                    var unknownSegments = prepared.UnknownSegments ?? new List<int>();
                    var batchSize = GroupPartSegmentBatchOrchestratorFunction.MaximumSegmentsPerOrchestration;
                    var batch = await SegmentClassificationLoop.ClassifyAsync(
                        context,
                        selectionRequest,
                        unknownSegments.Take(batchSize).ToList());
                    fallbackReason = batch.FallbackReason;
                    retriesExhausted = batch.RetriesExhausted;
                    for (var offset = batchSize; fallbackReason == null && retriesExhausted == null && offset < unknownSegments.Count; offset += batchSize)
                    {
                        var child = await context.CallSubOrchestratorAsync<GroupPartSegmentBatchResult>(
                            nameof(GroupPartSegmentBatchOrchestratorFunction),
                            new GroupPartSegmentBatchRequest
                            {
                                Request = selectionRequest,
                                SegmentIndices = unknownSegments.Skip(offset).Take(batchSize).ToList()
                            });
                        fallbackReason = child?.FallbackReason ?? (child == null ? SegmentClassificationLoop.IdentificationFailed : null);
                        retriesExhausted = child?.RetriesExhausted;
                    }

                    if (fallbackReason == null && retriesExhausted == null)
                    {
                        finalized = await context.CallActivityAsync<GroupPartSelectionResult>(
                            nameof(GroupPartSelectionFinalizeFunction),
                            selectionRequest);
                        if (finalized?.Outcome?.Status != SelectionOutcomeStatus.Completed ||
                            string.IsNullOrWhiteSpace(finalized.FilePath))
                        {
                            fallbackReason = SegmentClassificationLoop.IdentificationFailed;
                        }
                    }
                }
            }
            catch (TaskFailedException exception) when (!OrchestratorFunction.IsRetryExhaustion(exception))
            {
                // A selection step that exhausted its retries or ran past the function timeout is not caught here: it
                // propagates (no fallback), and the job is set to TransientError. Other failures fall back.
                GroupReadLogWriter.SelectionStepFailed(
                    logger,
                    exception.TaskName,
                    request.SourceGroup.ObjectId,
                    exception.FailureDetails?.ErrorMessage ?? exception.Message);
                fallbackReason = SegmentClassificationLoop.IdentificationFailed;
            }

            if (retriesExhausted != null)
            {
                // Retry exhaustion is not a fallback case: the part fails, publishes nothing, and the job reaches TransientError.
                // The caller's finally still cleans up this part's run-scoped blobs.
                throw new GraphRetriesExhaustedException(
                    $"The member-type lookup for group {request.SourceGroup.ObjectId} still failed after the configured " +
                    $"retries ({retriesExhausted}). No membership is published for this part.");
            }

            if (fallbackReason != null)
            {
                GroupReadLogWriter.Fallback(
                    logger,
                    request.SourceGroup.ObjectId,
                    fallbackReason,
                    readKind,
                    request.IdentityTypes);
                // The abandoned attempt's part file (if finalize committed it) and its unfiltered page uploads must
                // not survive into the replacement read.
                await CleanupBeforeReplacementReadAsync(context, request, logger, partFilePath);
                var fullReadResult = await RunFullReadAsync(
                    context,
                    clock,
                    request,
                    logger,
                    writeCompletion);
                return new CompletedReadResult(
                    fullReadResult,
                    GroupMembershipReadKind.Full);
            }

            try
            {
                await context.CallActivityAsync<GroupPartRecordTypesResult>(
                    nameof(GroupPartRecordTypesFunction),
                    selectionRequest);
            }
            catch (Exception exception)
            {
                // Best-effort: a lost record only costs later lookups.
                GroupReadLogWriter.SelectionStepFailed(
                    logger,
                    nameof(GroupPartRecordTypesFunction),
                    request.SourceGroup.ObjectId,
                    exception.Message);
            }

            if (writeCompletion)
            {
                GroupReadLogWriter.ReadCompleted(
                    logger,
                    finalized.MemberCount,
                    request.SourceGroup.ObjectId,
                    request.GroupId,
                    readKind,
                    request.IdentityTypes);
            }

            return new CompletedReadResult(
                new GroupMembershipFileResult
                {
                    FilePath = finalized.FilePath,
                    MemberCount = finalized.MemberCount
                },
                readKind);
        }

        private sealed record CompletedReadResult(
            GroupMembershipFileResult Result,
            GroupMembershipReadKind ReadKind);

        /// <summary>
        /// Deletes this run's saved-delta add and remove inputs in one activity call. Failures are logged and
        /// swallowed so they never replace the outcome (or the exception) of the saved-delta read.
        /// </summary>
        private static async Task TryDeleteSavedDeltaInputsAsync(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            ILogger logger)
        {
            var runId = request.SyncJob.RunId.GetValueOrDefault();
            try
            {
                await context.CallActivityAsync(
                    nameof(DeleteBlobFunction),
                    new DeleteBlobRequest
                    {
                        GroupId = request.GroupId,
                        SyncJob = request.SyncJob,
                        TotalParts = request.TotalParts,
                        CurrentPart = request.CurrentPart,
                        Prefixes = new List<string>
                        {
                            ProcessCachedAndDeltaUsersFunction.BuildDeltaAddsPrefix(
                                request.GroupId,
                                runId,
                                request.CurrentPart),
                            ProcessCachedAndDeltaUsersFunction.BuildDeltaRemovesPrefix(
                                request.GroupId,
                                runId,
                                request.CurrentPart)
                        }
                    });
            }
            catch (Exception exception)
            {
                logger.SavedDeltaInputCleanupFailed(exception, request.SourceGroup.ObjectId);
            }
        }

        /// <summary>
        /// The final cleanup of a one-type incremental read (not followed by a replacement read): best-effort, its
        /// result is not used and a failure is logged and never fails the part.
        /// </summary>
        private async Task TryCleanupAsync(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            ILogger logger)
        {
            try
            {
                await context.CallActivityAsync(
                    nameof(GroupPartCleanupFunction),
                    CleanupRequest(request, abandonedPartFilePath: null, beforeReplacementRead: false));
            }
            catch (Exception exception)
            {
                GroupReadLogWriter.CleanupFailed(
                    logger,
                    request.SourceGroup.ObjectId,
                    exception);
            }
        }

        /// <summary>
        /// The cleanup before every replacement read of a one-type part (failure model I1). The replacement read merges
        /// the same run and part prefix as the abandoned read, so it may start only when cleanup returns
        /// <see cref="GroupPartCleanupStatus.Completed"/>. An incomplete cleanup or a failed cleanup activity fails the
        /// part through the failed-read path, publishing nothing: publishing nothing is safe, publishing the abandoned
        /// read's unfiltered (wrong-type) members is not.
        /// </summary>
        private static async Task CleanupBeforeReplacementReadAsync(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            ILogger logger,
            string abandonedPartFilePath = null)
        {
            string reason;
            try
            {
                var result = await context.CallActivityAsync<GroupPartCleanupResult>(
                    nameof(GroupPartCleanupFunction),
                    CleanupRequest(request, abandonedPartFilePath, beforeReplacementRead: true));
                if (result?.Status == GroupPartCleanupStatus.Completed)
                {
                    return;
                }

                reason = result == null
                    ? "no cleanup result"
                    : string.IsNullOrWhiteSpace(result.Reason) ? "incomplete" : result.Reason;
            }
            catch (TaskFailedException exception)
            {
                reason = "cleanup activity failed: " + (exception.FailureDetails?.ErrorMessage ?? exception.Message);
            }

            GroupReadLogWriter.ReplacementReadCleanupIncomplete(
                logger,
                request.SourceGroup.ObjectId,
                reason);
            throw new GroupPartCleanupIncompleteException(request.SourceGroup.ObjectId, reason);
        }

        private static GroupPartCleanupRequest CleanupRequest(
            GroupMembershipRequest request,
            string abandonedPartFilePath,
            bool beforeReplacementRead) =>
            new GroupPartCleanupRequest
            {
                SyncJob = request.SyncJob,
                CurrentPart = request.CurrentPart,
                TotalParts = request.TotalParts,
                TargetGroupId = request.GroupId,
                AbandonedPartFilePath = abandonedPartFilePath,
                BeforeReplacementRead = beforeReplacementRead
            };

        private void TrackNestedGroupCount(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            int transitiveGroupCount)
        {
            if (context.IsReplaying || request.SourceGroup.ObjectId == request.GroupId)
            {
                return;
            }

            var nestedGroupEvent = new Dictionary<string, string>
            {
                { "SourceGroupObjectId", request.SourceGroup.ObjectId.ToString() },
                { "Destination", $"[{{\"type\":\"{request.SyncJob.MembershipType}\",\"value\":{{\"objectId\":\"{request.GroupId}\"}}}}]" },
                { "DestinationGroupObjectId", request.GroupId.ToString() },
                { "NestedGroupCount", transitiveGroupCount.ToString() }
            };
            _telemetryClient.TrackEvent("NestedGroupCount", nestedGroupEvent);
        }

        private async Task<SubOrchestratorResponse> HandleNestedDestinationGroupAsync(
            TaskOrchestrationContext context,
            GroupMembershipRequest request)
        {
            var nestedGroups = await context.CallActivityAsync<List<AzureADGroup>>(
                nameof(LogNestedGroupsFunction),
                new LogNestedGroupsRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    GroupId = request.GroupId
                });
            var destinationName = await context.CallActivityAsync<string>(
                nameof(DestinationNameReaderFunction),
                new DestinationNameReaderRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts
                });
            var nestedGroupsInfo = string.Join(
                "\n",
                nestedGroups.Select(group => $"- {group.Name} ({group.ObjectId})"));
            var additionalContentParams = new[]
            {
                request.GroupId.ToString(),
                destinationName.ToString(),
                nestedGroups.Count.ToString(),
                nestedGroupsInfo,
                DisabledNotificationType.StatusDescriptions[
                    NotificationMessageType.NestedGroupsFoundNotification],
                context.CurrentUtcDateTime.ToString("o", CultureInfo.InvariantCulture)
            };

            await context.CallActivityAsync(
                nameof(EmailSenderFunction),
                new EmailSenderRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    NotificationType =
                        NotificationMessageType.NestedGroupsFoundNotification,
                    AdditionalContentParams = additionalContentParams
                });
            await context.CallActivityAsync(
                nameof(JobStatusUpdaterFunction),
                new JobStatusUpdaterRequest
                {
                    Status = SyncStatus.NestedGroupsFound,
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts
                });

            return new SubOrchestratorResponse
            {
                Status = SyncStatus.NestedGroupsFound
            };
        }

        private async Task TrackAgentMembersReadEventIfNeededAsync(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            string readPath)
        {
            if (GroupIdentitySelection.IsOneType(request.IdentityTypes))
            {
                return;
            }

            var agentCount = await context.CallActivityAsync<int?>(
                nameof(GetAgentUserCountFunction),
                new GetAgentUserCountRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    GroupId = request.SourceGroup.ObjectId
                });

            if (context.IsReplaying)
            {
                return;
            }

            if (!agentCount.HasValue)
            {
                // FR-020 / I15: a count that could not be read is never emitted as a measurement (no fabricated
                // zero); the dashboard keeps the last good value.
                var logger = context.CreateReplaySafeLogger("GroupMembershipObtainer.SubOrchestratorFunction");
                logger.LogWarning(
                    "The agent count for group {GroupId} could not be read; no AgentMembersRead event is emitted for read path {ReadPath}.",
                    request.SourceGroup.ObjectId,
                    readPath);
                return;
            }

            var agentMembersReadEvent = new Dictionary<string, string>
            {
                { "RunId", request.SyncJob.RunId.GetValueOrDefault().ToString() },
                { "GroupObjectId", request.SourceGroup.ObjectId.ToString() },
                { "TargetGroupObjectId", request.GroupId.ToString() },
                { "GroupRole", request.SourceGroup.ObjectId == request.GroupId ? "Destination" : "Source" },
                { "ReadPath", readPath },
                { "AgentCount", agentCount.Value.ToString() }
            };
            _telemetryClient.TrackEvent("AgentMembersRead", agentMembersReadEvent);
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

        /// <summary>
        /// Downloads one blob through the durable file-downloader activity.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule the activity.</param>
        /// <param name="filePath">The blob path or prefix to read; it must identify the requested cache artifact.</param>
        /// <param name="request">The non-null group-part request that supplies sync-job and part metadata.</param>
        /// <param name="checkFileAge">
        /// <see langword="true"/> to let the activity reject an expired cache artifact; otherwise
        /// <see langword="false"/>.
        /// </param>
        /// <returns>The downloaded blob content, or the activity's empty result when no usable blob exists.</returns>
        public async Task<string> GetFileDownloaderFunction(
            TaskOrchestrationContext context,
            string filePath,
            GroupMembershipRequest request,
            bool checkFileAge) =>
            await context.CallActivityAsync<string>(
                nameof(FileDownloaderFunction),
                new FileDownloaderRequest
                {
                    FilePath = filePath,
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    CheckFileAge = checkFileAge
                });

        /// <summary>
        /// Deletes cache blobs matching a path or prefix through the durable file-deleter activity.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule the activity.</param>
        /// <param name="filePath">The cache path or prefix to delete.</param>
        /// <param name="request">The non-null group-part request that supplies sync-job and part metadata.</param>
        /// <returns>A task that completes after the delete activity finishes.</returns>
        public async Task ClearCacheFunction(
            TaskOrchestrationContext context,
            string filePath,
            GroupMembershipRequest request) =>
            await context.CallActivityAsync(
                nameof(FileDeleterFunction),
                new FileDeleterRequest
                {
                    FilePath = filePath,
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts
                });

        /// <summary>
        /// Reads Graph's transitive user-family count for the source group.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule the activity.</param>
        /// <param name="request">The non-null request containing the source group and sync metadata.</param>
        /// <returns>The count returned by the user-count activity.</returns>
        public async Task<int> GetUsersCountFunction(
            TaskOrchestrationContext context,
            GroupMembershipRequest request) =>
            await context.CallActivityAsync<int>(
                nameof(GetUserCountFunction),
                new GetUserCountRequest
                {
                    SyncJob = request.SyncJob,
                    CurrentPart = request.CurrentPart,
                    TotalParts = request.TotalParts,
                    GroupId = request.SourceGroup.ObjectId
                });

        /// <summary>
        /// Applies saved-delta additions and removals to an unfiltered source-group cache.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule the activity.</param>
        /// <param name="request">
        /// The cache path, delta path, selection metadata, and sync-job context. A missing cache path returns a
        /// response whose cache-match flag is false.
        /// </param>
        /// <returns>
        /// The cache-match result, updated count, membership file path, and staging path when staging was
        /// requested.
        /// </returns>
        public async Task<ProcessCachedAndDeltaUsersResponse> ProcessCachedAndDeltaUsers(
            TaskOrchestrationContext context,
            ProcessCachedAndDeltaUsersRequest request) =>
            await context.CallActivityAsync<ProcessCachedAndDeltaUsersResponse>(
                nameof(ProcessCachedAndDeltaUsersFunction),
                request);

        /// <summary>
        /// Reads every transitive-members page for the source group and writes each page to the part's upload area.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule page-reader activities.</param>
        /// <param name="request">
        /// The source group, destination group, explicit identity selection, and sync metadata. Omitted
        /// <c>IdentityTypes</c> reads people and agent users.
        /// </param>
        /// <param name="logger">The replay-safe logger used for page-progress messages.</param>
        /// <returns>A task that completes after the final Graph page is written.</returns>
        public async Task GetTransitiveMembers(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            ILogger logger)
        {
            logger.LogInformation(
                "Getting results from 1st page using transitive members query for group {GroupId}",
                request.SourceGroup.ObjectId);
            var firstPageRequest = new MembersReaderRequest
            {
                SyncJob = request.SyncJob,
                TotalParts = request.TotalParts,
                GroupId = request.SourceGroup.ObjectId,
                TargetGroupId = request.GroupId,
                CurrentPart = request.CurrentPart,
                IdentityTypes = request.IdentityTypes,
                ReadKind = GroupMembershipReadKind.Full,
                IsDestinationPart = request.IsDestinationPart
            };
            // FR-020: a page that still fails after the shared retry policies' attempts fails its activity, so the
            // TaskFailedException fails the part before any merge and the job reaches TransientError.
            var nextPageUrl = await context.CallActivityAsync<string>(
                nameof(MembersReaderFunction),
                firstPageRequest);
            while (!string.IsNullOrEmpty(nextPageUrl))
            {
                logger.LogInformation(
                    "Getting results from next page using transitive members query for group {GroupId}",
                    request.SourceGroup.ObjectId);
                var nextPageRequest = new SubsequentMembersReaderRequest
                {
                    SyncJob = request.SyncJob,
                    TotalParts = request.TotalParts,
                    NextPageUrl = nextPageUrl,
                    GroupId = request.SourceGroup.ObjectId,
                    TargetGroupId = request.GroupId,
                    CurrentPart = request.CurrentPart,
                    IdentityTypes = request.IdentityTypes,
                    ReadKind = GroupMembershipReadKind.Full,
                    IsDestinationPart = request.IsDestinationPart
                };
                nextPageUrl = await context.CallActivityAsync<string>(
                    nameof(SubsequentMembersReaderFunction),
                    nextPageRequest);
            }
        }

        /// <summary>
        /// Combines the page blobs for one part, writes its membership artifact, and persists incremental state.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule the activities.</param>
        /// <param name="request">The non-null source-group request and sync metadata.</param>
        /// <param name="deltaLink">
        /// The new Graph delta link to persist. Pass <see langword="null"/> or an empty string for a full read.
        /// </param>
        /// <param name="writeStaging">
        /// <see langword="true"/> to cache the unfiltered staging artifact for later one-type selection;
        /// otherwise <see langword="false"/>.
        /// </param>
        /// <param name="readKind">The full, initial-delta, or saved-delta-link path producing the artifact.</param>
        /// <returns>The membership artifact path and number of members written by the merge activity.</returns>
        public async Task<GroupMembershipFileResult> ProcessGroupMembershipChangesAsync(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            string deltaLink,
            bool writeStaging,
            GroupMembershipReadKind readKind,
            ReadAttemptClock readAttemptClock = null)
        {
            // One timestamp per read attempt names its part file, cache and delta link.
            var fileTimestamp = (readAttemptClock ?? new ReadAttemptClock()).Next(context.CurrentUtcDateTime);
            var membershipFileResult =
                await context.CallActivityAsync<GroupMembershipFileResult>(
                    nameof(TransitiveAndDeltaUsersSenderFunction),
                    new TransitiveAndDeltaUsersSenderRequest
                    {
                        SyncJob = request.SyncJob,
                        ObjectId = request.SourceGroup.ObjectId,
                        GroupId = request.GroupId,
                        TotalParts = request.TotalParts,
                        CurrentPart = request.CurrentPart,
                        Exclusionary = request.Exclusionary,
                        IdentityTypes = request.IdentityTypes,
                        WriteStaging = writeStaging,
                        ReadKind = readKind,
                        FileTimestamp = fileTimestamp
                    });
            await context.CallActivityAsync(
                nameof(DeleteBlobFunction),
                new DeleteBlobRequest
                {
                    GroupId = request.GroupId,
                    SyncJob = request.SyncJob,
                    TotalParts = request.TotalParts,
                    CurrentPart = request.CurrentPart
                });

            if (!string.IsNullOrEmpty(deltaLink))
            {
                var cacheSourceResult = membershipFileResult;
                if (writeStaging)
                {
                    cacheSourceResult = new GroupMembershipFileResult
                    {
                        FilePath = GroupPartStoragePaths.BuildStagingPath(
                            request.GroupId,
                            request.SyncJob.RunId.GetValueOrDefault(),
                            request.CurrentPart),
                        MemberCount = membershipFileResult.MemberCount
                    };
                }

                await context.CallActivityAsync(
                    nameof(CacheUploaderFunction),
                    new CacheUploaderRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = request.CurrentPart,
                        TotalParts = request.TotalParts,
                        ObjectId = request.SourceGroup.ObjectId,
                        MembershipFileResult = cacheSourceResult,
                        FileTimestamp = fileTimestamp
                    });
                await context.CallActivityAsync(
                    nameof(DeltaLinkUploaderFunction),
                    new DeltaLinkUploaderRequest
                    {
                        SyncJob = request.SyncJob,
                        CurrentPart = request.CurrentPart,
                        TotalParts = request.TotalParts,
                        ObjectId = request.SourceGroup.ObjectId,
                        DeltaLink = deltaLink,
                        FileTimestamp = fileTimestamp
                    });
            }

            return membershipFileResult;
        }

        /// <summary>
        /// Reads every page of a new group-delta sequence and writes its user-family additions.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule page-reader activities.</param>
        /// <param name="request">The non-null source-group request and sync metadata.</param>
        /// <param name="logger">The replay-safe logger used for page-progress messages.</param>
        /// <returns>The final delta link, or <see langword="null"/> when Graph did not return one.</returns>
        public async Task<string> GetInitialDeltaUsers(
            TaskOrchestrationContext context,
            GroupMembershipRequest request,
            ILogger logger)
        {
            var response = await context.CallActivityAsync<DeltaUrls>(
                nameof(DeltaUserReaderFunction),
                new DeltaUserReaderRequest
                {
                    SyncJob = request.SyncJob,
                    TotalParts = request.TotalParts,
                    ObjectId = request.SourceGroup.ObjectId,
                    TargetGroupId = request.GroupId,
                    CurrentPart = request.CurrentPart,
                    PageCount = DeltaQueryPageCount,
                    IdentityTypes = request.IdentityTypes,
                    ReadKind = GroupMembershipReadKind.DeltaInitial,
                    IsDestinationPart = request.IsDestinationPart
                });
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                logger.LogInformation(
                    "Getting results from next page using delta query for group {GroupId}",
                    request.SourceGroup.ObjectId);
                response = await context.CallActivityAsync<DeltaUrls>(
                    nameof(SubsequentDeltaUserReaderFunction),
                    new SubsequentDeltaUserReaderRequest
                    {
                        SyncJob = request.SyncJob,
                        TotalParts = request.TotalParts,
                        NextPageUrl = response.NextPageUrl,
                        ObjectId = request.SourceGroup.ObjectId,
                        TargetGroupId = request.GroupId,
                        CurrentPart = request.CurrentPart,
                        PageCount = DeltaQueryPageCount,
                        IdentityTypes = request.IdentityTypes,
                        ReadKind = GroupMembershipReadKind.DeltaInitial,
                        IsDestinationPart = request.IsDestinationPart
                    });
            }

            return response.DeltaUrl;
        }

        /// <summary>
        /// Reads every page that follows an existing group delta link and writes additions and removals.
        /// </summary>
        /// <param name="context">The non-null orchestration context used to schedule page-reader activities.</param>
        /// <param name="fileContent">The nonempty saved Graph delta-link URL.</param>
        /// <param name="request">The non-null source-group request and sync metadata.</param>
        /// <param name="logger">The replay-safe logger used for page-progress messages.</param>
        /// <returns>The replacement delta link, or <see langword="null"/> when Graph did not return one.</returns>
        public async Task<string> GetInitialDeltaLinkUsers(
            TaskOrchestrationContext context,
            string fileContent,
            GroupMembershipRequest request,
            ILogger logger)
        {
            var response = await context.CallActivityAsync<DeltaUrls>(
                nameof(DeltaLinkUserReaderFunction),
                new DeltaLinkUserReaderRequest
                {
                    SyncJob = request.SyncJob,
                    TotalParts = request.TotalParts,
                    GroupId = request.SourceGroup.ObjectId,
                    TargetGroupId = request.GroupId,
                    CurrentPart = request.CurrentPart,
                    DeltaLink = fileContent,
                    NumberOfPages = DeltaLinkQueryPageCount,
                    IdentityTypes = request.IdentityTypes,
                    ReadKind = GroupMembershipReadKind.DeltaLink
                });
            while (!string.IsNullOrEmpty(response.NextPageUrl))
            {
                logger.LogInformation(
                    "Getting results from next page using delta link members query for group {GroupId}",
                    request.SourceGroup.ObjectId);
                response = await context.CallActivityAsync<DeltaUrls>(
                    nameof(SubsequentDeltaLinkUserReaderFunction),
                    new SubsequentDeltaLinkUserReaderRequest
                    {
                        SyncJob = request.SyncJob,
                        TotalParts = request.TotalParts,
                        NextPageUrl = response.NextPageUrl,
                        GroupId = request.SourceGroup.ObjectId,
                        TargetGroupId = request.GroupId,
                        CurrentPart = request.CurrentPart,
                        PageCount = DeltaLinkQueryPageCount,
                        IdentityTypes = request.IdentityTypes,
                        ReadKind = GroupMembershipReadKind.DeltaLink
                    });
            }

            return response.DeltaUrl;
        }
    }
}
