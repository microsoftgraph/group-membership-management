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
    /// Best-effort merge of this read's identified types into <c>memberTypes/{source}.jsonl</c>: the record read with
    /// its ETag, plus the receipts' identified types, limited to this read's staging identifiers. A known type never
    /// changes (a disagreement drops the identifier). Written with <c>If-Match</c> (or <c>If-None-Match: *</c>), retried
    /// with jitter up to 5 attempts, also when older than 7 days. <c>notIdentified</c> is never written. Its failure is
    /// logged and never fails the part. Host cancellation propagates.
    /// </summary>
    public sealed class GroupPartRecordTypesFunction
    {
        public const int MaximumAttempts = 5;
        public static readonly TimeSpan RefreshAge = TimeSpan.FromDays(7);

        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILogger<GroupPartRecordTypesFunction> _logger;
        private readonly TimeProvider _timeProvider;

        public GroupPartRecordTypesFunction(
            IBlobStorageRepository blobStorageRepository,
            ILogger<GroupPartRecordTypesFunction> logger,
            TimeProvider timeProvider = null)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>The jittered delay before conditional attempt <c>attempt</c> (2..5).</summary>
        internal Func<int, TimeSpan> RetryDelay { get; set; } = attempt =>
            TimeSpan.FromMilliseconds(Random.Shared.Next(50, 250) * attempt);

        [Function(nameof(GroupPartRecordTypesFunction))]
        public async Task<GroupPartRecordTypesResult> RunAsync(
            [ActivityTrigger] GroupPartSelectionRequest request,
            CancellationToken hostCancellationToken)
        {
            GroupPartSelectionValidation.ValidateOneTypeRequest(request);
            using (_logger.BeginSyncJobScope(
                request.SyncJob,
                new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                try
                {
                    return await MergeAsync(request, hostCancellationToken);
                }
                catch (Exception exception) when (!hostCancellationToken.IsCancellationRequested)
                {
                    SelectionLog.TryLog(() => _logger.RecordedTypesWriteFailed(request.SourceGroupId, exception.Message));
                    return new GroupPartRecordTypesResult { Failed = true };
                }
            }
        }

        private async Task<GroupPartRecordTypesResult> MergeAsync(GroupPartSelectionRequest request, CancellationToken cancellationToken)
        {
            var runId = request.SyncJob.RunId.GetValueOrDefault();
            var manifestContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                _blobStorageRepository,
                GroupPartStoragePaths.BuildManifestPath(request.TargetGroupId, runId, request.CurrentPart), cancellationToken);
            if (manifestContent == null)
            {
                return new GroupPartRecordTypesResult();
            }

            var manifest = SelectionManifest.Parse(manifestContent);

            // This read's staging set (from the snapshot) with its identified types: recorded at prepare time, or by a
            // receipt. Receipts' notIdentified answers stay unknown.
            var snapshot = new CompactTypeMap.Builder(Math.Max(manifest.StagingCount, 1));
            var unknownSegments = new HashSet<int>(manifest.UnknownSegments);
            for (var segment = 0; segment < manifest.SegmentCount; segment++)
            {
                var segmentContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                    _blobStorageRepository,
                    GroupPartStoragePaths.BuildSegmentPath(request.TargetGroupId, runId, request.CurrentPart, manifest.ExecutionId, segment), cancellationToken);
                if (segmentContent == null)
                {
                    return new GroupPartRecordTypesResult();
                }

                var entries = SelectionSnapshotCodec.ParseSegment(segmentContent);
                if (unknownSegments.Contains(segment))
                {
                    var receiptContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                        _blobStorageRepository,
                        GroupPartStoragePaths.BuildReceiptPath(request.TargetGroupId, runId, request.CurrentPart, manifest.ExecutionId, segment), cancellationToken);
                    var answers = new Dictionary<Guid, byte>();
                    foreach (var (id, type) in SelectionSnapshotCodec.ParseReceipt(receiptContent))
                    {
                        answers[id] = type;
                    }

                    for (var i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].Type == MemberTypeCode.Unknown && answers.TryGetValue(entries[i].Id, out var answer))
                        {
                            entries[i] = (entries[i].Id, answer);
                        }
                    }
                }

                foreach (var (id, type) in entries)
                {
                    snapshot.Add(id, type);
                }
            }

            var current = snapshot.Build(dropConflicts: false);
            var recordPath = GroupPartStoragePaths.BuildRecordedTypesPath(request.SourceGroupId);
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    await Task.Delay(RetryDelay(attempt), cancellationToken);
                }

                CompactTypeMap recorded;
                string eTag;
                DateTimeOffset? lastModified;
                await using (var record = await _blobStorageRepository.OpenReadWithETagAsync(recordPath, cancellationToken))
                {
                    recorded = record == null
                        ? CompactTypeMap.Empty
                        : await SelectionSnapshotCodec.ReadRecordedTypesAsync(record.Content, cancellationToken);
                    eTag = record?.ETag;
                    lastModified = record?.LastModified;
                }

                var merged = Merge(recorded, current, out var changed);
                var stale = lastModified.HasValue && _timeProvider.GetUtcNow() - lastModified.Value > RefreshAge;
                if (!changed && !stale)
                {
                    return new GroupPartRecordTypesResult { Attempts = attempt - 1 };
                }

                if (merged.Count == 0 && eTag == null)
                {
                    return new GroupPartRecordTypesResult { Attempts = attempt - 1 };
                }

                var written = await _blobStorageRepository.UploadFileConditionalStreamAsync(
                    recordPath,
                    (stream, token) => SelectionSnapshotCodec.WriteRecordedTypesAsync(stream, merged, token),
                    eTag,
                    cancellationToken);
                if (written)
                {
                    return new GroupPartRecordTypesResult { Written = true, Attempts = attempt };
                }
            }

            SelectionLog.TryLog(() => _logger.RecordedTypesMergeExhausted(request.SourceGroupId, MaximumAttempts));
            return new GroupPartRecordTypesResult { Exhausted = true, Attempts = MaximumAttempts };
        }

        /// <summary>
        /// Keeps, for every identifier of this read, the recorded type or else the identified one; drops it when the
        /// two disagree. Identifiers outside this read are pruned.
        /// </summary>
        private static CompactTypeMap Merge(CompactTypeMap recorded, CompactTypeMap current, out bool changed)
        {
            var builder = new CompactTypeMap.Builder(Math.Max(current.Count, 1));
            for (var i = 0; i < current.Count; i++)
            {
                var id = current.Ids[i];
                var identified = current.Types[i];
                var known = recorded.TryGet(id, out var recordedType);
                var identifiedKnown = identified == MemberTypeCode.User || identified == MemberTypeCode.AgentUser;
                if (known && identifiedKnown && recordedType != identified)
                {
                    continue;
                }

                if (known)
                {
                    builder.Add(id, recordedType);
                }
                else if (identifiedKnown)
                {
                    builder.Add(id, identified);
                }
            }

            var merged = builder.Build(dropConflicts: true);
            changed = merged.Count != recorded.Count;
            for (var i = 0; !changed && i < merged.Count; i++)
            {
                changed = merged.Ids[i] != recorded.Ids[i] || merged.Types[i] != recorded.Types[i];
            }

            return merged;
        }
    }
}
