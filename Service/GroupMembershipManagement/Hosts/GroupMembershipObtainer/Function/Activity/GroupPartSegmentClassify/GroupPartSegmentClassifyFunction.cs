// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Classifies the <c>unknown</c> identifiers of one segment of the winning snapshot with one
    /// <c>GetDirectoryObjectTypesAsync</c> call and writes the segment's write-once receipt.
    /// </summary>
    public sealed class GroupPartSegmentClassifyFunction
    {
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILogger<GroupPartSegmentClassifyFunction> _logger;

        public GroupPartSegmentClassifyFunction(
            IBlobStorageRepository blobStorageRepository,
            IGraphGroupRepository graphGroupRepository,
            ILogger<GroupPartSegmentClassifyFunction> logger)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Returns <c>Completed</c> with the receipt's summary (new or reused), <c>RetriesExhausted</c> when the lookup
        /// still fails with 429, 500/502/503/504, a transport failure, or an HttpClient timeout after the retries,
        /// <c>LookupUnavailable</c> on 401/403, or <c>Failed</c>. Only <c>Completed</c> writes a receipt. Host
        /// cancellation propagates.
        /// </summary>
        [Function(nameof(GroupPartSegmentClassifyFunction))]
        public async Task<GroupPartSegmentResult> RunAsync(
            [ActivityTrigger] GroupPartSelectionRequest request,
            CancellationToken hostCancellationToken)
        {
            GroupPartSelectionValidation.ValidateOneTypeRequest(request);
            if (!request.SegmentIndex.HasValue || request.SegmentIndex.Value < 0)
            {
                throw new ArgumentException("A segment index is required.", nameof(request));
            }

            using (_logger.BeginSyncJobScope(
                request.SyncJob,
                new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                var result = await ClassifyAsync(request, request.SegmentIndex.Value, hostCancellationToken);
                if (result.Outcome.Status != SelectionOutcomeStatus.Completed)
                {
                    SelectionLog.TryLog(() => _logger.SelectionStepOutcome(
                        nameof(GroupPartSegmentClassifyFunction),
                        request.SourceGroupId,
                        result.Outcome.ToString()));
                }

                return result;
            }
        }

        private async Task<GroupPartSegmentResult> ClassifyAsync(GroupPartSelectionRequest request, int segmentIndex, CancellationToken cancellationToken)
        {
            var runId = request.SyncJob.RunId.GetValueOrDefault();
            var manifestContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                _blobStorageRepository,
                GroupPartStoragePaths.BuildManifestPath(request.TargetGroupId, runId, request.CurrentPart), cancellationToken);
            if (manifestContent == null)
            {
                return Result(segmentIndex, SelectionOutcome.Failed("manifest missing"));
            }

            var manifest = SelectionManifest.Parse(manifestContent);
            if (segmentIndex >= manifest.SegmentCount)
            {
                return Result(segmentIndex, SelectionOutcome.Failed("segment out of range"));
            }

            var receiptPath = GroupPartStoragePaths.BuildReceiptPath(request.TargetGroupId, runId, request.CurrentPart, manifest.ExecutionId, segmentIndex);
            var existingReceipt = await SelectionSnapshotCodec.ReadTextOrNullAsync(_blobStorageRepository, receiptPath, cancellationToken);
            if (existingReceipt != null)
            {
                return Summary(segmentIndex, SelectionSnapshotCodec.ParseReceipt(existingReceipt));
            }

            var segmentContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                _blobStorageRepository,
                GroupPartStoragePaths.BuildSegmentPath(request.TargetGroupId, runId, request.CurrentPart, manifest.ExecutionId, segmentIndex), cancellationToken)
                ?? throw new InvalidDataException($"Selection segment {segmentIndex} is missing.");
            var unknownIds = SelectionSnapshotCodec.ParseSegment(segmentContent)
                .Where(entry => entry.Type == MemberTypeCode.Unknown)
                .Select(entry => entry.Id)
                .ToArray();

            var stopwatch = Stopwatch.StartNew();
            IReadOnlyDictionary<Guid, string> returned;
            try
            {
                returned = unknownIds.Length == 0
                    ? new Dictionary<Guid, string>()
                    : await _graphGroupRepository.GetDirectoryObjectTypesAsync(unknownIds, runId, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException) && SelectionSnapshotCodec.RefusedStatus(exception) is int refusedStatus)
            {
                // 401/403: this run falls back to the selected full read; the next run probes the lookup again.
                SelectionLog.TryLog(() => _logger.MemberTypeLookupRefused(request.SourceGroupId, refusedStatus));
                return Result(segmentIndex, SelectionOutcome.LookupUnavailable(refusedStatus));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && SelectionSnapshotCodec.IsRetriesExhausted(exception))
            {
                SelectionLog.TryLog(() => _logger.MemberTypeLookupFailed(
                    request.SourceGroupId,
                    unknownIds.Length,
                    exception.GetType().Name,
                    exception.Message,
                    SelectionSnapshotCodec.StatusText(exception)));
                return Result(segmentIndex, SelectionOutcome.RetriesExhausted(
                    $"lookup still failed after retries ({SelectionSnapshotCodec.StatusText(exception)})"));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException) || !cancellationToken.IsCancellationRequested)
            {
                SelectionLog.TryLog(() => _logger.MemberTypeLookupFailed(
                    request.SourceGroupId,
                    unknownIds.Length,
                    exception.GetType().Name,
                    exception.Message,
                    SelectionSnapshotCodec.StatusText(exception)));
                return Result(segmentIndex, SelectionOutcome.Failed("lookup failed"));
            }

            stopwatch.Stop();

            var answers = new List<(Guid Id, byte Type)>(unknownIds.Length);
            var unknownSet = new HashSet<Guid>(unknownIds);
            if (returned.Keys.Any(id => !unknownSet.Contains(id)))
            {
                return Result(segmentIndex, SelectionOutcome.Failed("lookup returned an unrequested identifier"));
            }

            foreach (var id in unknownIds)
            {
                if (!returned.TryGetValue(id, out var graphType))
                {
                    answers.Add((id, MemberTypeCode.NotIdentified));
                }
                else if (MemberTypeCode.TryFromGraphType(graphType, out var code))
                {
                    answers.Add((id, code));
                }
                else
                {
                    return Result(segmentIndex, SelectionOutcome.Failed("unsupported member type"));
                }
            }

            if (unknownIds.Length >= 2 && returned.Count == 0)
            {
                // Owner D3 exception: a batch of 2 or more that returns none of them is a lookup failure.
                return Result(segmentIndex, SelectionOutcome.Failed("lookup returned none"));
            }

            var written = await _blobStorageRepository.UploadFileWriteOnceAsync(
                receiptPath,
                SelectionSnapshotCodec.FormatReceipt(answers),
                metadata: null,
                cancellationToken);
            if (written.AlreadyExisted)
            {
                var winner = await SelectionSnapshotCodec.ReadTextOrNullAsync(_blobStorageRepository, receiptPath, cancellationToken)
                    ?? throw new InvalidDataException("The receipt was rejected as existing but could not be read.");
                return Summary(segmentIndex, SelectionSnapshotCodec.ParseReceipt(winner));
            }

            var summary = Summary(segmentIndex, answers);
            var readKind = (request.ReadKind ?? GroupMembershipReadKind.DeltaInitial).ToString();
            SelectionLog.TryLog(() => _logger.SegmentClassified(
                segmentIndex,
                manifest.SegmentCount,
                request.SourceGroupId,
                summary.LookupCount,
                summary.IdentifiedCount,
                summary.NotIdentifiedCount,
                stopwatch.ElapsedMilliseconds,
                readKind,
                GroupIdentitySelection.Describe(request.IdentityTypes)));
            return summary;
        }

        private static GroupPartSegmentResult Summary(int segmentIndex, IReadOnlyCollection<(Guid Id, byte Type)> answers) => new()
        {
            Outcome = SelectionOutcome.Completed(),
            SegmentIndex = segmentIndex,
            LookupCount = answers.Count,
            IdentifiedCount = answers.Count(answer => answer.Type != MemberTypeCode.NotIdentified),
            NotIdentifiedCount = answers.Count(answer => answer.Type == MemberTypeCode.NotIdentified)
        };

        private static GroupPartSegmentResult Result(int segmentIndex, SelectionOutcome outcome) => new()
        {
            Outcome = outcome,
            SegmentIndex = segmentIndex
        };
    }
}
