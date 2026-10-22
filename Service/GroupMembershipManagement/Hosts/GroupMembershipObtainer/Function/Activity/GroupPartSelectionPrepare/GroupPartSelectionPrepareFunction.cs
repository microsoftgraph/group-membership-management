// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Writes the run's per-execution snapshot of every staging member with its recorded type or <c>unknown</c>, in
    /// segments of up to 1,000, and selects one execution's snapshot through a write-once manifest.
    /// </summary>
    public sealed class GroupPartSelectionPrepareFunction
    {
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILogger<GroupPartSelectionPrepareFunction> _logger;

        public GroupPartSelectionPrepareFunction(
            IBlobStorageRepository blobStorageRepository,
            ILogger<GroupPartSelectionPrepareFunction> logger)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Returns the winning manifest: the existing one, or the one this execution created. A missing or unreadable
        /// staging blob throws (never an empty set). Host cancellation propagates.
        /// </summary>
        [Function(nameof(GroupPartSelectionPrepareFunction))]
        public async Task<GroupPartPrepareResult> RunAsync(
            [ActivityTrigger] GroupPartSelectionRequest request,
            CancellationToken hostCancellationToken)
        {
            GroupPartSelectionValidation.ValidateOneTypeRequest(request);
            using (_logger.BeginSyncJobScope(
                request.SyncJob,
                new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                return await PrepareAsync(request, hostCancellationToken);
            }
        }

        private async Task<GroupPartPrepareResult> PrepareAsync(GroupPartSelectionRequest request, CancellationToken cancellationToken)
        {
            var runId = request.SyncJob.RunId.GetValueOrDefault();
            var manifestPath = GroupPartStoragePaths.BuildManifestPath(request.TargetGroupId, runId, request.CurrentPart);
            var existing = await SelectionSnapshotCodec.ReadTextOrNullAsync(_blobStorageRepository, manifestPath, cancellationToken);
            if (existing != null)
            {
                return SelectionManifest.Parse(existing).ToResult();
            }

            // 1. Staging identifiers into a compact sorted array.
            var stagingBuilder = new CompactTypeMap.Builder(64 * 1024);
            await foreach (var member in _blobStorageRepository.StreamMembershipAsync(request.StagingPath, null, cancellationToken))
            {
                if (member != null)
                {
                    stagingBuilder.Add(member.ObjectId, MemberTypeCode.Unknown);
                }
            }

            var staging = stagingBuilder.Build(dropConflicts: false);

            // 2. Recorded types, best-effort, into a compact sorted array.
            var recorded = await ReadRecordedTypesBestEffortAsync(request, cancellationToken);

            // 3. Label every staging member and write the segments under a new execution identifier.
            for (var i = 0; i < staging.Count; i++)
            {
                if (recorded.TryGet(staging.Ids[i], out var type))
                {
                    staging.Types[i] = type;
                }
            }

            recorded = null;
            var executionId = Guid.NewGuid();
            var segmentCount = (staging.Count + SelectionSnapshotCodec.SegmentSize - 1) / SelectionSnapshotCodec.SegmentSize;
            var unknownSegments = new List<int>();
            for (var segment = 0; segment < segmentCount; segment++)
            {
                var start = segment * SelectionSnapshotCodec.SegmentSize;
                var count = Math.Min(SelectionSnapshotCodec.SegmentSize, staging.Count - start);
                if (Array.IndexOf(staging.Types, MemberTypeCode.Unknown, start, count) >= 0)
                {
                    unknownSegments.Add(segment);
                }

                await _blobStorageRepository.UploadFileAsync(
                    GroupPartStoragePaths.BuildSegmentPath(request.TargetGroupId, request.SyncJob.RunId.GetValueOrDefault(), request.CurrentPart, executionId, segment),
                    SelectionSnapshotCodec.FormatSegment(staging.Ids, staging.Types, start, count),
                    metadata: null,
                    cancellationToken);
            }

            // 4. Create the manifest; a losing duplicate returns the winner's.
            var manifest = new SelectionManifest
            {
                ExecutionId = executionId,
                StagingCount = staging.Count,
                SegmentCount = segmentCount,
                UnknownSegments = unknownSegments
            };
            var created = await _blobStorageRepository.UploadFileIfAbsentAsync(manifestPath, manifest.Serialize(), cancellationToken);
            if (created)
            {
                return manifest.ToResult();
            }

            var winner = await SelectionSnapshotCodec.ReadTextOrNullAsync(_blobStorageRepository, manifestPath, cancellationToken)
                ?? throw new InvalidDataException("The selection manifest was rejected as existing but could not be read.");
            return SelectionManifest.Parse(winner).ToResult();
        }

        private async Task<CompactTypeMap> ReadRecordedTypesBestEffortAsync(GroupPartSelectionRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await using var record = await _blobStorageRepository.OpenReadWithETagAsync(
                    GroupPartStoragePaths.BuildRecordedTypesPath(request.SourceGroupId),
                    cancellationToken);
                return record == null
                    ? CompactTypeMap.Empty
                    : await SelectionSnapshotCodec.ReadRecordedTypesAsync(record.Content, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                SelectionLog.TryLog(() => _logger.RecordedTypesReadFailed(request.SourceGroupId, exception.Message));
                return CompactTypeMap.Empty;
            }
        }
    }

    internal static class GroupPartSelectionValidation
    {
        public static void ValidateOneTypeRequest(GroupPartSelectionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.SyncJob);
            if (!GroupIdentitySelection.IsOneType(request.IdentityTypes))
            {
                throw new ArgumentException("The selection pipeline runs only for a users-only or agents-only part.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.StagingPath))
            {
                throw new ArgumentException("A staging path is required.", nameof(request));
            }
        }
    }
}
