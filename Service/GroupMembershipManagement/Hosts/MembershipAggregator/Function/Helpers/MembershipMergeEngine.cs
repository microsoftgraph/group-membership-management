// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Services.Entities;
using Microsoft.Extensions.Options;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator.Helpers
{
    /// <summary>
    /// Merges canonically ordered membership streams in bounded passes and publishes staged snapshots or retry-safe delta artifacts.
    /// </summary>
    internal sealed class MembershipMergeEngine
    {
        private readonly IBlobStorageRepository _repository;
        private readonly MembershipMergeOptions _options;

        public MembershipMergeEngine(
            IBlobStorageRepository repository,
            IOptions<MembershipMergeOptions> options)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

            if (!_options.IsValid)
            {
                throw new OptionsValidationException(
                    nameof(MembershipMergeOptions),
                    typeof(MembershipMergeOptions),
                    new[]
                    {
                        "MaxStreamsPerPass must be at least 4 and ReadAheadSize must be at least 1."
                    });
            }
        }

        /// <summary>
        /// Produces addition and removal blobs, then claims the deterministic manifest that makes one attempt authoritative.
        /// </summary>
        public async Task<MembershipMergeResult> ExecuteAsync(
            MembershipMergeRequest request,
            MembershipMergeDiagnostics diagnostics = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            diagnostics ??= new MembershipMergeDiagnostics();

            var intermediatePaths = new List<string>();
            var additionsPath = $"{request.AttemptPrefix}-additions.json";
            var removalsPath = $"{request.AttemptPrefix}-removals.json";
            var manifestPath = $"{request.OutputPrefix}/{request.IdempotencyKey}-manifest.json";
            var attemptPaths = new[] { additionsPath, removalsPath };
            var manifestPublicationStarted = false;

            var committedManifest = await TryReadCommittedManifestAsync(
                manifestPath,
                cancellationToken);
            if (committedManifest != null)
            {
                return ToResult(manifestPath, committedManifest);
            }

            try
            {
                var inputs = await ReduceInputsAsync(
                    request.Inputs,
                    diagnostics,
                    (batchInputs, summary, pass, batch, nextInputs) =>
                        WriteIntermediateOutputsAsync(
                            request,
                            batchInputs,
                            summary,
                            diagnostics,
                            pass,
                            batch,
                            nextInputs,
                            intermediatePaths,
                            cancellationToken),
                    cancellationToken);

                diagnostics.PassCount++;
                using var additionsSummary = new MembershipMergeSummary();
                await _repository.WriteMembershipAsync(
                    additionsPath,
                    request.FirstSourceEnvelope,
                    StreamDeltaMembersAsync(
                        inputs,
                        MembershipAction.Add,
                        additionsSummary,
                        diagnostics,
                        cancellationToken),
                    cancellationToken: cancellationToken);

                if (additionsSummary.RemoveCount == 0)
                {
                    // The additions pass already counted removals, so an empty result needs no second input read.
                    await _repository.WriteMembershipAsync(
                        removalsPath,
                        request.FirstSourceEnvelope,
                        EmptyMembersAsync(cancellationToken),
                        cancellationToken: cancellationToken);
                }
                else
                {
                    using var removalsSummary = new MembershipMergeSummary();
                    await _repository.WriteMembershipAsync(
                        removalsPath,
                        request.FirstSourceEnvelope,
                        StreamDeltaMembersAsync(
                            inputs,
                            MembershipAction.Remove,
                            removalsSummary,
                            diagnostics,
                            cancellationToken),
                        cancellationToken: cancellationToken);
                    EnsureMatchingSummaries(additionsSummary, removalsSummary, "delta outputs");
                }

                await DeletePathsAsync(intermediatePaths);
                intermediatePaths.Clear();

                var status = additionsSummary.AddCount == 0 && additionsSummary.RemoveCount == 0
                    ? MembershipDeltaStatus.NoChanges
                    : MembershipDeltaStatus.Ok;
                var manifest = new MembershipMergeManifest(
                    additionsPath,
                    removalsPath,
                    additionsSummary.SourceMemberCount,
                    additionsSummary.DestinationCount,
                    additionsSummary.AddCount,
                    additionsSummary.RemoveCount,
                    status);

                var serializedManifest = JsonSerializer.Serialize(manifest);
                cancellationToken.ThrowIfCancellationRequested();
                manifestPublicationStarted = true;
                var committed = await _repository.UploadFileIfAbsentAsync(
                    manifestPath,
                    serializedManifest,
                    cancellationToken);

                if (!committed)
                {
                    var existingManifest = await ReadCommittedManifestAsync(
                        manifestPath,
                        cancellationToken);
                    var losingPaths = attemptPaths
                        .Except(
                            new[] { existingManifest.AdditionsPath, existingManifest.RemovalsPath },
                            StringComparer.Ordinal)
                        .ToArray();
                    await DeletePathsAsync(losingPaths);
                    return ToResult(manifestPath, existingManifest);
                }

                return ToResult(manifestPath, manifest);
            }
            catch (Exception primaryFailure)
            {
                var cleanupPaths = new List<string>(intermediatePaths);
                if (!manifestPublicationStarted)
                {
                    cleanupPaths.AddRange(attemptPaths);
                }

                var cleanupFailure = await TryDeletePathsAsync(cleanupPaths);
                if (cleanupFailure != null)
                {
                    primaryFailure.Data["MembershipMergeCleanupFailure"] = cleanupFailure;
                }

                throw;
            }
        }

        /// <summary>
        /// Combines source parts into one post-exclusion source snapshot and preserves the destination as a separate snapshot.
        /// </summary>
        internal async Task<MembershipSnapshotResult> StageSnapshotAsync(
            IReadOnlyList<MembershipMergeInput> inputs,
            string sourcePath,
            GroupMembership sourceEnvelope,
            string destinationPath,
            GroupMembership destinationEnvelope,
            MembershipMergeDiagnostics diagnostics = null,
            CancellationToken cancellationToken = default)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("A source output path is required.", nameof(sourcePath));
            if (sourceEnvelope == null)
                throw new ArgumentNullException(nameof(sourceEnvelope));
            if ((destinationPath == null) != (destinationEnvelope == null))
            {
                throw new ArgumentException(
                    "The destination output path and envelope must either both be supplied or both be omitted.");
            }

            diagnostics ??= new MembershipMergeDiagnostics();
            var intermediatePaths = new List<string>();
            var outputPaths = destinationPath == null
                ? new[] { sourcePath }
                : new[] { sourcePath, destinationPath };

            try
            {
                var currentInputs = await ReduceInputsAsync(
                    inputs,
                    diagnostics,
                    (batchInputs, summary, pass, batch, nextInputs) =>
                        WriteSnapshotIntermediateOutputsAsync(
                            batchInputs,
                            summary,
                            diagnostics,
                            sourceEnvelope,
                            sourcePath,
                            pass,
                            batch,
                            nextInputs,
                            intermediatePaths,
                            cancellationToken),
                    cancellationToken);

                diagnostics.PassCount++;
                using var sourceSummary = new MembershipMergeSummary();
                await _repository.WriteMembershipAsync(
                    sourcePath,
                    sourceEnvelope,
                    StreamMembershipKindAsync(
                        currentInputs,
                        MembershipMergeInputKind.Included,
                        applyExclusions: true,
                        summary: sourceSummary,
                        diagnostics: diagnostics,
                        cancellationToken: cancellationToken),
                    cancellationToken: cancellationToken);

                if (destinationPath != null)
                {
                    using var destinationSummary = new MembershipMergeSummary();
                    await _repository.WriteMembershipAsync(
                        destinationPath,
                        destinationEnvelope,
                        StreamMembershipKindAsync(
                            currentInputs,
                            MembershipMergeInputKind.Destination,
                            applyExclusions: true,
                            summary: destinationSummary,
                            diagnostics: diagnostics,
                            cancellationToken: cancellationToken),
                        cancellationToken: cancellationToken);
                    EnsureMatchingSummaries(
                        sourceSummary,
                        destinationSummary,
                        "source and destination snapshots");
                }

                await DeletePathsAsync(intermediatePaths);
                intermediatePaths.Clear();

                return new MembershipSnapshotResult(
                    sourceSummary.SourceMemberCount,
                    sourceSummary.DestinationCount);
            }
            catch (Exception primaryFailure)
            {
                var cleanupFailure = await TryDeletePathsAsync(
                    intermediatePaths.Concat(outputPaths));

                if (cleanupFailure != null)
                {
                    primaryFailure.Data["MembershipSnapshotCleanupFailure"] = cleanupFailure;
                }

                throw;
            }
        }

        /// <summary>
        /// Reduces large input sets through intermediate merge passes until one bounded merge can consume them.
        /// </summary>
        private async Task<IReadOnlyList<MembershipMergeInput>> ReduceInputsAsync(
            IReadOnlyList<MembershipMergeInput> inputs,
            MembershipMergeDiagnostics diagnostics,
            Func<
                IReadOnlyList<MembershipMergeInput>,
                MembershipMergeSummary,
                int,
                int,
                ICollection<MembershipMergeInput>,
                Task> writeIntermediateOutputs,
            CancellationToken cancellationToken)
        {
            var currentInputs = inputs.ToList();
            var pass = 0;

            while (currentInputs.Count > _options.MaxStreamsPerPass)
            {
                diagnostics.UsedHierarchicalMerge = true;
                diagnostics.PassCount++;
                var nextInputs = new List<MembershipMergeInput>();
                var stagedBatches =
                    new List<(
                        IReadOnlyList<MembershipMergeInput> Inputs,
                        MembershipMergeSummary Summary,
                        int Batch)>();
                var batch = 0;

                try
                {
                    for (var index = 0; index < currentInputs.Count; index += _options.MaxStreamsPerPass)
                    {
                        var batchInputs = currentInputs
                            .Skip(index)
                            .Take(_options.MaxStreamsPerPass)
                            .ToArray();
                        var summary = await SummarizeMergeAsync(batchInputs, diagnostics, cancellationToken);
                        stagedBatches.Add((batchInputs, summary, batch));
                        batch++;
                    }

                    foreach (var stagedBatch in stagedBatches)
                    {
                        await writeIntermediateOutputs(stagedBatch.Inputs, stagedBatch.Summary, pass, stagedBatch.Batch, nextInputs);
                    }
                }
                finally
                {
                    // Each summary must remain alive until its batch output has been validated.
                    foreach (var stagedBatch in stagedBatches)
                    {
                        stagedBatch.Summary.Dispose();
                    }
                }

                if (nextInputs.Count >= currentInputs.Count)
                {
                    throw new InvalidOperationException(
                        $"Membership merge pass {pass} did not reduce its {currentInputs.Count} inputs.");
                }

                currentInputs = nextInputs;
                pass++;
            }

            return currentInputs;
        }

        /// <summary>
        /// Coalesces equal ObjectIds across sorted inputs while retaining inclusion, exclusion, destination, and provenance data.
        /// </summary>
        internal async IAsyncEnumerable<MembershipMergeRecord> MergeRecordsAsync(
            IReadOnlyList<MembershipMergeInput> inputs,
            MembershipMergeDiagnostics diagnostics,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            if (inputs.Count > _options.MaxStreamsPerPass)
            {
                throw new ArgumentException(
                    $"A merge pass cannot open more than {_options.MaxStreamsPerPass} streams.",
                    nameof(inputs));
            }

            diagnostics.OpenStreams(inputs.Count);
            var cursors = new List<MembershipInputCursor>(inputs.Count);

            try
            {
                foreach (var input in inputs)
                {
                    var cursor = new MembershipInputCursor(
                        input,
                        _repository,
                        _options.ReadAheadSize,
                        diagnostics,
                        cancellationToken);
                    cursors.Add(cursor);
                    await cursor.FillAsync();
                }

                while (TryFindNextObjectId(cursors, out var objectId))
                {
                    var included = false;
                    var excluded = false;
                    var inDestination = false;
                    var sourceGroups = new HashSet<Guid>();
                    JsonElement? destinationProperties = null;

                    foreach (var cursor in cursors)
                    {
                        if (!cursor.TryTake(objectId, out var record))
                        {
                            continue;
                        }

                        included |= record.Included;
                        excluded |= record.Excluded;
                        inDestination |= record.InDestination;
                        if (!destinationProperties.HasValue
                            && record.InDestination
                            && record.DestinationProperties.HasValue)
                        {
                            destinationProperties = record.DestinationProperties.Value.Clone();
                        }

                        foreach (var sourceGroup in record.SourceGroups)
                        {
                            sourceGroups.Add(sourceGroup);
                        }

                        await cursor.FillAsync();
                    }

                    var orderedSourceGroups = new Guid[sourceGroups.Count];
                    sourceGroups.CopyTo(orderedSourceGroups);
                    Array.Sort(orderedSourceGroups, CanonicalObjectIdComparer.Instance);

                    yield return new MembershipMergeRecord(
                        objectId,
                        included,
                        excluded,
                        inDestination,
                        orderedSourceGroups,
                        destinationProperties);
                }
            }
            finally
            {
                foreach (var cursor in cursors)
                {
                    await cursor.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// Splits one merged batch into nonempty included, excluded, and destination membership blobs for the next pass.
        /// </summary>
        private async Task WriteIntermediateOutputsAsync(
            MembershipMergeRequest request,
            IReadOnlyList<MembershipMergeInput> inputs,
            MembershipMergeSummary summary,
            MembershipMergeDiagnostics diagnostics,
            int pass,
            int batch,
            ICollection<MembershipMergeInput> nextInputs,
            ICollection<string> intermediatePaths,
            CancellationToken cancellationToken)
        {
            foreach (var kind in Enum.GetValues<MembershipMergeInputKind>())
            {
                var count = kind switch
                {
                    MembershipMergeInputKind.Included => summary.IncludedCount,
                    MembershipMergeInputKind.Excluded => summary.ExcludedCount,
                    MembershipMergeInputKind.Destination => summary.DestinationCount,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                if (count == 0)
                {
                    continue;
                }

                var path =
                    $"{request.AttemptPrefix}.intermediate.pass-{pass:D2}.batch-{batch:D4}.{kind}.json";
                var envelope = new GroupMembership
                {
                    RunId = request.FirstSourceEnvelope.RunId,
                    SyncJobId = request.FirstSourceEnvelope.SyncJobId,
                    Exclusionary = kind == MembershipMergeInputKind.Excluded,
                    SourceMembers = new List<AzureADUser>()
                };

                intermediatePaths.Add(path);
                using var projectionSummary = new MembershipMergeSummary();
                await _repository.WriteMembershipAsync(
                    path,
                    envelope,
                    StreamMembershipKindAsync(
                        inputs,
                        kind,
                        applyExclusions: false,
                        summary: projectionSummary,
                        diagnostics: diagnostics,
                        cancellationToken: cancellationToken),
                    cancellationToken: cancellationToken);
                EnsureMatchingSummaries(
                    summary,
                    projectionSummary,
                    $"intermediate output '{path}'");
                nextInputs.Add(MembershipMergeInput.FromPath(path, kind));
            }
        }

        /// <summary>
        /// Splits one extraction batch into nonempty intermediate streams while retaining the final snapshot's envelope and path.
        /// </summary>
        private async Task WriteSnapshotIntermediateOutputsAsync(
            IReadOnlyList<MembershipMergeInput> inputs,
            MembershipMergeSummary summary,
            MembershipMergeDiagnostics diagnostics,
            GroupMembership sourceEnvelope,
            string sourcePath,
            int pass,
            int batch,
            ICollection<MembershipMergeInput> nextInputs,
            ICollection<string> intermediatePaths,
            CancellationToken cancellationToken)
        {
            foreach (var kind in Enum.GetValues<MembershipMergeInputKind>())
            {
                var count = kind switch
                {
                    MembershipMergeInputKind.Included => summary.SourceMemberCount,
                    MembershipMergeInputKind.Excluded => summary.ExcludedCount,
                    MembershipMergeInputKind.Destination => summary.DestinationCount,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                if (count == 0)
                {
                    continue;
                }

                var path =
                    $"{sourcePath}.intermediate.pass-{pass:D2}.batch-{batch:D4}.{kind}.json";
                var envelope = new GroupMembership
                {
                    RunId = sourceEnvelope.RunId,
                    SyncJobId = sourceEnvelope.SyncJobId,
                    Exclusionary = kind == MembershipMergeInputKind.Excluded,
                    SourceMembers = new List<AzureADUser>()
                };

                intermediatePaths.Add(path);
                using var projectionSummary = new MembershipMergeSummary();
                await _repository.WriteMembershipAsync(
                    path,
                    envelope,
                    StreamMembershipKindAsync(
                        inputs,
                        kind,
                        applyExclusions: true,
                        summary: projectionSummary,
                        diagnostics: diagnostics,
                        cancellationToken: cancellationToken),
                    cancellationToken: cancellationToken);
                EnsureMatchingSummaries(
                    summary,
                    projectionSummary,
                    $"snapshot intermediate output '{path}'");
                nextInputs.Add(MembershipMergeInput.FromPath(path, kind));
            }
        }

        /// <summary>
        /// Reads a manifest that must already exist after another attempt wins publication.
        /// </summary>
        private async Task<MembershipMergeManifest> ReadCommittedManifestAsync(
            string manifestPath,
            CancellationToken cancellationToken)
        {
            var manifest = await TryReadCommittedManifestAsync(
                manifestPath,
                cancellationToken);
            if (manifest == null)
            {
                throw new InvalidDataException(
                    $"Committed membership merge manifest '{manifestPath}' was not found.");
            }

            return manifest;
        }

        /// <summary>
        /// Returns no result when the manifest is absent and rejects present manifests that cannot identify both delta blobs.
        /// </summary>
        private async Task<MembershipMergeManifest> TryReadCommittedManifestAsync(
            string manifestPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blob = await _repository.DownloadFileAsync(manifestPath);
            if (blob == null || blob.BlobStatus == BlobStatus.NotFound)
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<MembershipMergeManifest>(blob.Content);
            if (manifest == null
                || string.IsNullOrWhiteSpace(manifest.AdditionsPath)
                || string.IsNullOrWhiteSpace(manifest.RemovalsPath))
            {
                throw new InvalidDataException(
                    $"Committed membership merge manifest '{manifestPath}' is invalid.");
            }

            return manifest;
        }

        private static MembershipMergeResult ToResult(
            string manifestPath,
            MembershipMergeManifest manifest) =>
            new MembershipMergeResult(
                manifestPath,
                manifest.AdditionsPath,
                manifest.RemovalsPath,
                manifest.SourceMemberCount,
                manifest.DestinationMemberCount,
                manifest.AddCount,
                manifest.RemoveCount,
                manifest.Status);

        /// <summary>
        /// Reads a bounded merge to completion and collects its source, destination, and delta counts.
        /// </summary>
        private async Task<MembershipMergeSummary> SummarizeMergeAsync(
            IReadOnlyList<MembershipMergeInput> inputs,
            MembershipMergeDiagnostics diagnostics,
            CancellationToken cancellationToken)
        {
            var summary = new MembershipMergeSummary();
            try
            {
                await foreach (var record in MergeRecordsAsync(inputs, diagnostics, cancellationToken))
                {
                    summary.Record(record);
                }

                return summary;
            }
            catch
            {
                summary.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Streams one membership kind from a fresh bounded merge and collects counts used to detect changed inputs.
        /// </summary>
        private async IAsyncEnumerable<AzureADUser> StreamMembershipKindAsync(
            IReadOnlyList<MembershipMergeInput> inputs,
            MembershipMergeInputKind kind,
            bool applyExclusions,
            MembershipMergeSummary summary,
            MembershipMergeDiagnostics diagnostics,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var record in MergeRecordsAsync(inputs, diagnostics, cancellationToken))
            {
                summary.Record(record);
                var include = kind switch
                {
                    MembershipMergeInputKind.Included =>
                        applyExclusions ? record.IsSourceMember : record.Included,
                    MembershipMergeInputKind.Excluded => record.Excluded,
                    MembershipMergeInputKind.Destination => record.InDestination,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                if (!include)
                {
                    continue;
                }

                yield return new AzureADUser
                {
                    ObjectId = record.ObjectId,
                    SourceGroups = kind == MembershipMergeInputKind.Included
                        ? new List<Guid>(record.SourceGroups)
                        : null,
                    Properties = kind == MembershipMergeInputKind.Destination
                        && record.DestinationProperties.HasValue
                            ? record.DestinationProperties.Value.Clone()
                            : null
                };
            }
        }

        /// <summary>
        /// Streams only members whose source-versus-destination state produces the requested delta action.
        /// </summary>
        private async IAsyncEnumerable<AzureADUser> StreamDeltaMembersAsync(
            IReadOnlyList<MembershipMergeInput> inputs,
            MembershipAction action,
            MembershipMergeSummary summary,
            MembershipMergeDiagnostics diagnostics,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var record in MergeRecordsAsync(inputs, diagnostics, cancellationToken))
            {
                summary.Record(record);
                if (record.TryCreateDeltaMember(out var member)
                    && member.MembershipAction == action)
                {
                    yield return member;
                }
            }
        }

        private static async IAsyncEnumerable<AzureADUser> EmptyMembersAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        private static void EnsureMatchingSummaries(
            MembershipMergeSummary expected,
            MembershipMergeSummary actual,
            string output)
        {
            if (!expected.HasSameContentAs(actual))
            {
                throw new InvalidDataException(
                    $"Membership merge inputs changed while writing {output}.");
            }
        }

        private async Task DeletePathsAsync(IEnumerable<string> paths)
        {
            var failure = await TryDeletePathsAsync(paths);
            if (failure != null)
            {
                throw failure;
            }
        }

        /// <summary>
        /// Attempts every cleanup path and returns the first failure after giving the remaining paths a chance to delete.
        /// </summary>
        private async Task<Exception> TryDeletePathsAsync(IEnumerable<string> paths)
        {
            Exception firstFailure = null;
            foreach (var path in paths.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    await _repository.DeleteFileAsync(path);
                }
                catch (Exception exception)
                {
                    firstFailure ??= exception;
                }
            }

            return firstFailure;
        }

        /// <summary>
        /// Finds the lowest ObjectId currently available across all input cursors for the next merge step.
        /// </summary>
        private static bool TryFindNextObjectId(
            IReadOnlyList<MembershipInputCursor> cursors,
            out Guid objectId)
        {
            objectId = default;
            var found = false;

            foreach (var cursor in cursors)
            {
                if (!cursor.TryPeek(out var record))
                {
                    continue;
                }

                if (!found || CanonicalObjectIdComparer.Instance.Compare(record.ObjectId, objectId) < 0)
                {
                    objectId = record.ObjectId;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Reads one sorted input with bounded lookahead and folds adjacent duplicate ObjectIds into one merge record.
        /// </summary>
        private sealed class MembershipInputCursor : IAsyncDisposable
        {
            private readonly MembershipMergeInput _input;
            private readonly int _readAheadSize;
            private readonly MembershipMergeDiagnostics _diagnostics;
            private readonly IAsyncEnumerator<AzureADUser> _members;
            private readonly Queue<MembershipMergeRecord> _records = new Queue<MembershipMergeRecord>();
            private AzureADUser _pendingMember;

            public MembershipInputCursor(
                MembershipMergeInput input,
                IBlobStorageRepository repository,
                int readAheadSize,
                MembershipMergeDiagnostics diagnostics,
                CancellationToken cancellationToken)
            {
                _input = input ?? throw new ArgumentNullException(nameof(input));
                _readAheadSize = readAheadSize;
                _diagnostics = diagnostics;
                _members = input.OpenMembers(repository, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }

            /// <summary>
            /// Refills this cursor only to its configured lookahead limit so buffering remains independent of membership size.
            /// </summary>
            public async Task FillAsync()
            {
                while (_records.Count < _readAheadSize)
                {
                    var record = await ReadNextRecordAsync();
                    if (record == null)
                    {
                        break;
                    }

                    _records.Enqueue(record);
                    _diagnostics.RecordBuffered();
                }
            }

            public bool TryPeek(out MembershipMergeRecord record)
            {
                if (_records.Count == 0)
                {
                    record = null;
                    return false;
                }

                record = _records.Peek();
                return true;
            }

            public bool TryTake(Guid objectId, out MembershipMergeRecord record)
            {
                if (_records.Count == 0 || _records.Peek().ObjectId != objectId)
                {
                    record = null;
                    return false;
                }

                record = _records.Dequeue();
                _diagnostics.RecordConsumed();
                return true;
            }

            public ValueTask DisposeAsync() => _members.DisposeAsync();

            /// <summary>
            /// Reads one ObjectId and folds its adjacent duplicates while validating canonical order and preserving provenance.
            /// </summary>
            private async Task<MembershipMergeRecord> ReadNextRecordAsync()
            {
                var first = _pendingMember;
                _pendingMember = null;

                if (first == null && !await _members.MoveNextAsync())
                {
                    return null;
                }

                first ??= _members.Current;
                if (first == null)
                {
                    throw new InvalidDataException($"Membership input '{_input.Name}' contains a null member.");
                }

                var objectId = first.ObjectId;
                var sourceGroups = new HashSet<Guid>();
                JsonElement? destinationProperties = CreateDestinationProperties(first);
                AddSourceGroups(first, sourceGroups);

                while (await _members.MoveNextAsync())
                {
                    var member = _members.Current;
                    if (member == null)
                    {
                        throw new InvalidDataException($"Membership input '{_input.Name}' contains a null member.");
                    }

                    var comparison = CanonicalObjectIdComparer.Instance.Compare(member.ObjectId, objectId);
                    if (comparison < 0)
                    {
                        throw new InvalidDataException(
                            $"Membership input '{_input.Name}' is not in canonical ObjectId order.");
                    }

                    if (comparison > 0)
                    {
                        _pendingMember = member;
                        break;
                    }

                    AddSourceGroups(member, sourceGroups);
                    destinationProperties ??= CreateDestinationProperties(member);
                }

                var orderedSourceGroups = new Guid[sourceGroups.Count];
                sourceGroups.CopyTo(orderedSourceGroups);
                Array.Sort(orderedSourceGroups, CanonicalObjectIdComparer.Instance);

                return new MembershipMergeRecord(
                    objectId,
                    _input.Kind == MembershipMergeInputKind.Included,
                    _input.Kind == MembershipMergeInputKind.Excluded,
                    _input.Kind == MembershipMergeInputKind.Destination,
                    orderedSourceGroups,
                    destinationProperties);
            }

            /// <summary>
            /// Copies opaque destination-only data so removals retain properties required by downstream updaters.
            /// </summary>
            private JsonElement? CreateDestinationProperties(AzureADUser member)
            {
                if (_input.Kind != MembershipMergeInputKind.Destination)
                {
                    return null;
                }

                var properties = member.Properties;
                return properties == null
                    ? null
                    : JsonSerializer.SerializeToElement(properties, properties.GetType());
            }

            /// <summary>
            /// Unions source provenance, including the legacy single SourceGroup field when no list replaces it.
            /// </summary>
            private void AddSourceGroups(AzureADUser member, ISet<Guid> sourceGroups)
            {
                if (_input.Kind != MembershipMergeInputKind.Included)
                {
                    return;
                }

                if (member.SourceGroups != null && member.SourceGroups.Count > 0)
                {
                    foreach (var sourceGroup in member.SourceGroups)
                    {
                        sourceGroups.Add(sourceGroup);
                    }
                }

                if (member.SourceGroup != Guid.Empty
                    || member.SourceGroups == null
                    || member.SourceGroups.Count == 0)
                {
                    sourceGroups.Add(member.SourceGroup);
                }
            }
        }
    }
}
