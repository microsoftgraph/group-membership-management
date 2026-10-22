// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Classifies every staging record only from the winning snapshot and its receipts (never the shared record) and
    /// publishes the selected part write-once. A committed (or already committed) part is <c>Completed</c>. Host
    /// cancellation propagates.
    /// </summary>
    public sealed class GroupPartSelectionFinalizeFunction
    {
        private const int NotIdentifiedBatchSize = 100;

        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly ILogger<GroupPartSelectionFinalizeFunction> _logger;

        public GroupPartSelectionFinalizeFunction(
            IBlobStorageRepository blobStorageRepository,
            ILogger<GroupPartSelectionFinalizeFunction> logger)
        {
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(GroupPartSelectionFinalizeFunction))]
        public async Task<GroupPartSelectionResult> RunAsync(
            [ActivityTrigger] GroupPartSelectionRequest request,
            CancellationToken hostCancellationToken)
        {
            GroupPartSelectionValidation.ValidateOneTypeRequest(request);
            if (string.IsNullOrWhiteSpace(request.PartFilePath))
            {
                throw new ArgumentException("A part-file path is required.", nameof(request));
            }

            using (_logger.BeginSyncJobScope(
                request.SyncJob,
                new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                return await FinalizeAsync(request, hostCancellationToken);
            }
        }

        private async Task<GroupPartSelectionResult> FinalizeAsync(
            GroupPartSelectionRequest request,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var runId = request.SyncJob.RunId.GetValueOrDefault();
            var selection = GroupIdentitySelection.Resolve(request.IdentityTypes);
            var selectsUsers = selection.Includes(IdentityType.Users);
            var readKind = request.ReadKind ?? GroupMembershipReadKind.DeltaInitial;

            // 1. The winning snapshot and its receipts, as one compact classification array.
            var manifestContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                _blobStorageRepository,
                GroupPartStoragePaths.BuildManifestPath(request.TargetGroupId, runId, request.CurrentPart), cancellationToken);
            if (manifestContent == null)
            {
                return Failed(request, "manifest missing");
            }

            var manifest = SelectionManifest.Parse(manifestContent);
            var ids = new Guid[manifest.StagingCount];
            var types = new byte[manifest.StagingCount];
            var unknownSegments = new HashSet<int>(manifest.UnknownSegments);
            var filled = 0;
            var lookupCount = 0;
            for (var segment = 0; segment < manifest.SegmentCount; segment++)
            {
                var segmentContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                    _blobStorageRepository,
                    GroupPartStoragePaths.BuildSegmentPath(request.TargetGroupId, runId, request.CurrentPart, manifest.ExecutionId, segment), cancellationToken);
                if (segmentContent == null)
                {
                    return Failed(request, "unclassified member");
                }

                var entries = SelectionSnapshotCodec.ParseSegment(segmentContent);
                if (filled + entries.Count > ids.Length)
                {
                    return Failed(request, "unclassified member");
                }

                var segmentStart = filled;
                foreach (var (id, type) in entries)
                {
                    ids[filled] = id;
                    types[filled] = type;
                    filled++;
                }

                if (unknownSegments.Contains(segment))
                {
                    var receiptContent = await SelectionSnapshotCodec.ReadTextOrNullAsync(
                        _blobStorageRepository,
                        GroupPartStoragePaths.BuildReceiptPath(request.TargetGroupId, runId, request.CurrentPart, manifest.ExecutionId, segment), cancellationToken);
                    if (receiptContent != null)
                    {
                        foreach (var (id, type) in SelectionSnapshotCodec.ParseReceipt(receiptContent))
                        {
                            lookupCount++;
                            var index = Array.BinarySearch(ids, segmentStart, filled - segmentStart, id);
                            if (index >= 0 && types[index] == MemberTypeCode.Unknown)
                            {
                                types[index] = type;
                            }
                        }
                    }
                }
            }

            if (filled != ids.Length)
            {
                return Failed(request, "unclassified member");
            }

            var snapshot = new CompactTypeMap(ids, types, filled);

            // 2. Stream staging; classify each record from the array only.
            var counts = new SelectionCounts();
            GroupMembership envelope = null;
            var counted = new BitArrayIndex(snapshot.Count);
            await foreach (var member in _blobStorageRepository.StreamMembershipAsync(request.StagingPath, details => envelope = details, cancellationToken))
            {
                if (member == null)
                {
                    continue;
                }

                var index = snapshot.IndexOf(member.ObjectId);
                var type = index >= 0 ? snapshot.Types[index] : MemberTypeCode.Unknown;
                if (type == MemberTypeCode.Unknown)
                {
                    return Failed(request, "unclassified member");
                }

                // Counted once per unique identifier, as the part holds it: a duplicate staging record is written
                // once (initial read) or not at all beyond the snapshot's single entry (saved delta).
                if (counted.TrySet(index))
                {
                    counts.Add(type, selectsUsers);
                }
            }

            if (envelope == null)
            {
                throw new InvalidDataException($"The staging blob '{request.StagingPath}' did not contain membership details.");
            }

            // 3. Cancellation before the single final commit.
            cancellationToken.ThrowIfCancellationRequested();
            WriteOnceResult written;
            if (readKind == GroupMembershipReadKind.DeltaLink)
            {
                // Sized from the snapshot it is filled from (unique identifiers), never from the staging record count:
                // a duplicate staging record must not leave a trailing Guid.Empty.
                var selectedCount = 0;
                for (var i = 0; i < snapshot.Count; i++)
                {
                    if (IsSelected(snapshot.Types[i], selectsUsers))
                    {
                        selectedCount++;
                    }
                }

                var selectedIds = new Guid[selectedCount];
                var next = 0;
                for (var i = 0; i < snapshot.Count; i++)
                {
                    if (IsSelected(snapshot.Types[i], selectsUsers))
                    {
                        selectedIds[next++] = snapshot.Ids[i];
                    }
                }

                written = await _blobStorageRepository.UploadGroupMembershipFromGuidsWriteOnceAsync(
                    request.PartFilePath,
                    selectedIds,
                    envelope.Destination,
                    envelope.RunId,
                    envelope.SyncJobId,
                    envelope.Exclusionary,
                    envelope.MembershipObtainerDryRunEnabled,
                    envelope.Query,
                    request.SourceGroupId,
                    cancellationToken);
            }
            else
            {
                written = await _blobStorageRepository.UploadGroupMembershipStreamWriteOnceAsync(
                    request.PartFilePath,
                    envelope,
                    SelectedRecordsAsync(request.StagingPath, snapshot, selectsUsers, cancellationToken),
                    cancellationToken.ThrowIfCancellationRequested,
                    cancellationToken,
                    cancellationToken);
            }

            // 4. Committed (or already committed by an earlier execution): the outcome is Completed.
            stopwatch.Stop();
            LogNotIdentified(request, snapshot, readKind);
            var selectionDescription = GroupIdentitySelection.Describe(request.IdentityTypes);
            // Counts describe the part actually written: the written member count (or, when an earlier execution
            // committed the part, the existing part's count) and the unique identifiers classified above.
            var memberCount = written.Count;
            SelectionLog.TryLog(() => _logger.SelectionCompleted(
                memberCount,
                request.SourceGroupId,
                counts.ContributedUsers,
                counts.ContributedAgents,
                counts.ExcludedUsers,
                counts.ExcludedAgents,
                counts.NotIdentified,
                lookupCount,
                manifest.SegmentCount,
                stopwatch.ElapsedMilliseconds,
                readKind.ToString(),
                selectionDescription));

            return new GroupPartSelectionResult
            {
                Outcome = SelectionOutcome.Completed(),
                FilePath = request.PartFilePath,
                MemberCount = memberCount,
                UserCount = counts.ContributedUsers,
                AgentCount = counts.ContributedAgents,
                NotIdentifiedCount = counts.NotIdentified,
                LookupCount = lookupCount
            };
        }

        private async IAsyncEnumerable<AzureADUser> SelectedRecordsAsync(
            string stagingPath,
            CompactTypeMap snapshot,
            bool selectsUsers,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // The part keeps today's canonical order (ascending object identifier); the snapshot is in that order, so
            // records are emitted in staging order only when staging is canonical. Staging written by the merge is.
            var seen = new BitArrayIndex(snapshot.Count);
            await foreach (var member in _blobStorageRepository.StreamMembershipAsync(stagingPath, null, cancellationToken))
            {
                if (member == null)
                {
                    continue;
                }

                var index = snapshot.IndexOf(member.ObjectId);
                if (index < 0 || !seen.TrySet(index))
                {
                    continue;
                }

                if (IsSelected(snapshot.Types[index], selectsUsers))
                {
                    yield return member;
                }
            }
        }

        private void LogNotIdentified(GroupPartSelectionRequest request, CompactTypeMap snapshot, GroupMembershipReadKind readKind)
        {
            var batch = new List<Guid>(NotIdentifiedBatchSize);
            var total = 0;
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (snapshot.Types[i] == MemberTypeCode.NotIdentified)
                {
                    total++;
                }
            }

            if (total == 0)
            {
                return;
            }

            var batchCount = (total + NotIdentifiedBatchSize - 1) / NotIdentifiedBatchSize;
            var batchIndex = 0;
            var selectionDescription = GroupIdentitySelection.Describe(request.IdentityTypes);
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (snapshot.Types[i] != MemberTypeCode.NotIdentified)
                {
                    continue;
                }

                batch.Add(snapshot.Ids[i]);
                if (batch.Count == NotIdentifiedBatchSize)
                {
                    Flush();
                }
            }

            Flush();

            void Flush()
            {
                if (batch.Count == 0)
                {
                    return;
                }

                batchIndex++;
                var memberIds = string.Join(",", batch);
                var index = batchIndex;
                SelectionLog.TryLog(() => _logger.MembersNotIdentified(
                    request.SourceGroupId,
                    index,
                    batchCount,
                    memberIds,
                    readKind.ToString(),
                    selectionDescription));
                batch.Clear();
            }
        }

        private static bool IsSelected(byte type, bool selectsUsers) =>
            selectsUsers ? type == MemberTypeCode.User : type == MemberTypeCode.AgentUser;

        private GroupPartSelectionResult Failed(GroupPartSelectionRequest request, string reason)
        {
            SelectionLog.TryLog(() => _logger.SelectionStepOutcome(nameof(GroupPartSelectionFinalizeFunction), request.SourceGroupId, "Failed: " + reason));
            return new GroupPartSelectionResult { Outcome = SelectionOutcome.Failed(reason) };
        }

        private sealed class SelectionCounts
        {
            public int Selected;
            public int ContributedUsers;
            public int ContributedAgents;
            public int ExcludedUsers;
            public int ExcludedAgents;
            public int NotIdentified;

            public void Add(byte type, bool selectsUsers)
            {
                switch (type)
                {
                    case MemberTypeCode.User when selectsUsers:
                        ContributedUsers++;
                        Selected++;
                        break;
                    case MemberTypeCode.User:
                        ExcludedUsers++;
                        break;
                    case MemberTypeCode.AgentUser when !selectsUsers:
                        ContributedAgents++;
                        Selected++;
                        break;
                    case MemberTypeCode.AgentUser:
                        ExcludedAgents++;
                        break;
                    default:
                        NotIdentified++;
                        break;
                }
            }
        }

        /// <summary>One bit per snapshot entry, so a duplicate staging record is emitted once.</summary>
        private sealed class BitArrayIndex
        {
            private readonly ulong[] _bits;

            public BitArrayIndex(int count)
            {
                _bits = new ulong[(count + 63) / 64];
            }

            public bool TrySet(int index)
            {
                var mask = 1UL << (index & 63);
                ref var word = ref _bits[index >> 6];
                if ((word & mask) != 0)
                {
                    return false;
                }

                word |= mask;
                return true;
            }
        }
    }
}
