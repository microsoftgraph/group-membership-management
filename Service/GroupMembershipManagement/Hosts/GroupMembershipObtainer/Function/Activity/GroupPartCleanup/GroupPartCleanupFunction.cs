// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Removes the run-scoped blobs of one one-type incremental read of a part: the staging blob by exact name and the
    /// whole selection folder by its <c>/</c>-terminated prefix, so no other part's blobs match. Deletion is
    /// listing-based (no expected-404 requests) and idempotent. Host cancellation propagates.
    /// <para>
    /// Before a replacement read (<see cref="GroupPartCleanupRequest.BeforeReplacementRead"/>) it is not best-effort:
    /// it first deletes the abandoned read's merge-prefix page uploads, then lists the merge prefix again and requires
    /// it to be empty, then deletes the abandoned part file (exact name) and the staging and selection blobs. A failed
    /// delete or a non-empty merge prefix returns <see cref="GroupPartCleanupStatus.Incomplete"/>, never
    /// <see cref="GroupPartCleanupStatus.Completed"/>: the replacement read's merge uses the same run and part prefix,
    /// so it would merge any surviving page. The sub-orchestrator then fails the part without starting that read.
    /// </para>
    /// <para>
    /// Otherwise (the final cleanup) it stays best-effort: a failure reaches the orchestrator, which logs it and never
    /// fails the part. The blobs left behind are run-scoped and removed by the 30-day lifecycle rule.
    /// </para>
    /// </summary>
    public sealed class GroupPartCleanupFunction
    {
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILogger<GroupPartCleanupFunction> _logger;

        public GroupPartCleanupFunction(
            IBlobStorageRepository blobStorageRepository,
            ILogger<GroupPartCleanupFunction> logger)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(GroupPartCleanupFunction))]
        public async Task<GroupPartCleanupResult> RunAsync(
            [ActivityTrigger] GroupPartCleanupRequest request,
            CancellationToken hostCancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.SyncJob);

            using (_logger.BeginSyncJobScope(
                request.SyncJob,
                new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                var runId = request.SyncJob.RunId.GetValueOrDefault();
                var exactPaths = new List<string>
                {
                    GroupPartStoragePaths.BuildStagingPath(request.TargetGroupId, runId, request.CurrentPart)
                };
                var prefixes = new List<string>
                {
                    GroupPartStoragePaths.BuildSelectionFolder(request.TargetGroupId, runId, request.CurrentPart)
                };

                if (!string.IsNullOrWhiteSpace(request.AbandonedPartFilePath))
                {
                    exactPaths.Add(request.AbandonedPartFilePath.TrimStart('/'));
                }

                if (request.BeforeReplacementRead)
                {
                    return await CleanupBeforeReplacementReadAsync(request, runId, exactPaths, prefixes, hostCancellationToken);
                }

                await _blobStorageRepository.DeleteListedBlobsAsync(exactPaths, prefixes, hostCancellationToken);
                return GroupPartCleanupResult.Completed();
            }
        }

        private async Task<GroupPartCleanupResult> CleanupBeforeReplacementReadAsync(
            GroupPartCleanupRequest request,
            Guid runId,
            IReadOnlyCollection<string> exactPaths,
            IReadOnlyCollection<string> prefixes,
            CancellationToken cancellationToken)
        {
            var mergePrefix = new[] { GroupPartStoragePaths.BuildMergePrefix(request.TargetGroupId, runId, request.CurrentPart) };
            string reason;
            try
            {
                // 1. The abandoned read's page uploads first: they are what the replacement read would merge.
                await _blobStorageRepository.DeleteListedBlobsAsync(Array.Empty<string>(), mergePrefix, cancellationToken);

                // 2. List the merge prefix again; it must be empty. A blob listed now was uploaded after the first
                //    listing (for example by a straggling duplicate page reader), so the prefix is not clean.
                var remaining = await _blobStorageRepository.DeleteListedBlobsAsync(Array.Empty<string>(), mergePrefix, cancellationToken);
                if (remaining != 0)
                {
                    reason = $"merge prefix not empty after delete ({remaining} upload(s) listed)";
                    LogFailure(request, reason);
                    return GroupPartCleanupResult.Incomplete(reason);
                }

                // 3. The abandoned part file, the staging blob and the selection folder.
                await _blobStorageRepository.DeleteListedBlobsAsync(exactPaths, prefixes, cancellationToken);
                return GroupPartCleanupResult.Completed();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                reason = "delete failed: " + exception.Message;
            }

            LogFailure(request, reason);
            return GroupPartCleanupResult.Incomplete(reason);
        }

        private void LogFailure(GroupPartCleanupRequest request, string reason)
        {
            var targetGroupId = request.TargetGroupId;
            SelectionLog.TryLog(() => _logger.GroupPartCleanupFailed(targetGroupId, reason));
        }
    }
}
