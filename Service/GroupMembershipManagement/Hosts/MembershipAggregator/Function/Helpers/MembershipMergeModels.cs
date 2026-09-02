// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Services.Entities;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Hosts.MembershipAggregator.Helpers
{
    /// <summary>
    /// Identifies how an input contributes to the final source and destination membership sets.
    /// </summary>
    internal enum MembershipMergeInputKind
    {
        Included,
        Excluded,
        Destination
    }

    /// <summary>
    /// Opens a named membership stream from blob storage or an in-process source for merging.
    /// </summary>
    internal sealed class MembershipMergeInput
    {
        private readonly Func<CancellationToken, IAsyncEnumerable<AzureADUser>> _openMembers;

        private MembershipMergeInput(
            string name,
            string path,
            MembershipMergeInputKind kind,
            Func<CancellationToken, IAsyncEnumerable<AzureADUser>> openMembers)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An input name is required.", nameof(name));
            if (path == null && openMembers == null)
                throw new ArgumentException("An input path or member source is required.");

            Name = name;
            Path = path;
            Kind = kind;
            _openMembers = openMembers;
        }

        public string Name { get; }

        public string Path { get; }

        public MembershipMergeInputKind Kind { get; }

        public static MembershipMergeInput FromPath(
            string path,
            MembershipMergeInputKind kind) =>
            new MembershipMergeInput(path, path, kind, null);

        internal static MembershipMergeInput FromMembers(
            string name,
            MembershipMergeInputKind kind,
            IReadOnlyList<AzureADUser> members)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            return new MembershipMergeInput(
                name,
                null,
                kind,
                cancellationToken => Enumerate(members, cancellationToken));
        }

        internal static MembershipMergeInput FromMemberSource(
            string name,
            MembershipMergeInputKind kind,
            Func<CancellationToken, IAsyncEnumerable<AzureADUser>> openMembers) =>
            new MembershipMergeInput(
                name,
                null,
                kind,
                openMembers ?? throw new ArgumentNullException(nameof(openMembers)));

        internal IAsyncEnumerable<AzureADUser> OpenMembers(
            IBlobStorageRepository repository,
            CancellationToken cancellationToken) =>
            _openMembers?.Invoke(cancellationToken)
            ?? repository.StreamMembershipAsync(Path, cancellationToken: cancellationToken);

        private static async IAsyncEnumerable<AzureADUser> Enumerate(
            IReadOnlyList<AzureADUser> members,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return member;
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>
    /// Describes one retry-safe merge attempt and the paths used to publish its artifacts.
    /// </summary>
    internal sealed class MembershipMergeRequest
    {
        public MembershipMergeRequest(
            IReadOnlyList<MembershipMergeInput> inputs,
            string outputPrefix,
            string idempotencyKey,
            Guid attemptId,
            GroupMembership firstSourceEnvelope)
        {
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            if (string.IsNullOrWhiteSpace(outputPrefix))
                throw new ArgumentException("An output prefix is required.", nameof(outputPrefix));
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
            if (attemptId == Guid.Empty)
                throw new ArgumentException("A non-empty attempt identifier is required.", nameof(attemptId));

            OutputPrefix = outputPrefix.TrimEnd('/');
            IdempotencyKey = idempotencyKey;
            AttemptId = attemptId;
            FirstSourceEnvelope = firstSourceEnvelope
                ?? throw new ArgumentNullException(nameof(firstSourceEnvelope));
        }

        public IReadOnlyList<MembershipMergeInput> Inputs { get; }

        public string OutputPrefix { get; }

        public string IdempotencyKey { get; }

        public Guid AttemptId { get; }

        public GroupMembership FirstSourceEnvelope { get; }

        public string AttemptPrefix =>
            $"{OutputPrefix}/{IdempotencyKey}-{AttemptId:N}";
    }

    /// <summary>
    /// Represents the combined state for one ObjectId across all source, exclusion, and destination inputs.
    /// </summary>
    internal sealed record MembershipMergeRecord(
        Guid ObjectId,
        bool Included,
        bool Excluded,
        bool InDestination,
        Guid[] SourceGroups,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JsonElement? DestinationProperties = null)
    {
        public bool IsSourceMember => Included && !Excluded;

        public bool TryCreateDeltaMember(out AzureADUser member)
        {
            if (IsSourceMember == InDestination)
            {
                member = null;
                return false;
            }

            member = new AzureADUser
            {
                ObjectId = ObjectId,
                MembershipAction = IsSourceMember ? MembershipAction.Add : MembershipAction.Remove,
                SourceGroups = IsSourceMember ? new List<Guid>(SourceGroups) : null
            };
            if (!IsSourceMember && DestinationProperties.HasValue)
            {
                member.Properties = DestinationProperties.Value.Clone();
            }

            return true;
        }
    }

    /// <summary>
    /// Records the authoritative delta artifact paths, counts, and status claimed by the winning attempt.
    /// </summary>
    internal sealed record MembershipMergeManifest(
        string AdditionsPath,
        string RemovalsPath,
        int SourceMemberCount,
        int DestinationMemberCount,
        int AddCount,
        int RemoveCount,
        MembershipDeltaStatus Status);

    /// <summary>
    /// Returns the committed manifest and delta artifacts to the calling activity.
    /// </summary>
    internal sealed record MembershipMergeResult(
        string ManifestPath,
        string AdditionsPath,
        string RemovalsPath,
        int SourceMemberCount,
        int DestinationMemberCount,
        int AddCount,
        int RemoveCount,
        MembershipDeltaStatus Status);

    /// <summary>
    /// Reports the member counts written to the staged source and destination snapshots.
    /// </summary>
    internal sealed record MembershipSnapshotResult(
        int SourceMemberCount,
        int DestinationMemberCount);

    /// <summary>
    /// Collects the counts needed to publish and cross-check streamed merge outputs.
    /// </summary>
    internal sealed class MembershipMergeSummary
    {
        public int IncludedCount { get; private set; }

        public int ExcludedCount { get; private set; }

        public int DestinationCount { get; private set; }

        public int SourceMemberCount { get; private set; }

        public int AddCount { get; private set; }

        public int RemoveCount { get; private set; }

        public void Record(MembershipMergeRecord record)
        {
            if (record.Included) IncludedCount++;
            if (record.Excluded) ExcludedCount++;
            if (record.InDestination) DestinationCount++;
            if (record.IsSourceMember) SourceMemberCount++;

            if (record.IsSourceMember && !record.InDestination)
            {
                AddCount++;
            }
            else if (!record.IsSourceMember && record.InDestination)
            {
                RemoveCount++;
            }
        }

        public bool HasSameCountsAs(MembershipMergeSummary other) =>
            other != null
            && IncludedCount == other.IncludedCount
            && ExcludedCount == other.ExcludedCount
            && DestinationCount == other.DestinationCount
            && SourceMemberCount == other.SourceMemberCount
            && AddCount == other.AddCount
            && RemoveCount == other.RemoveCount;
    }

    /// <summary>
    /// Sets the maximum open streams and per-stream lookahead used by each bounded merge pass.
    /// </summary>
    public sealed class MembershipMergeOptions
    {
        public const int DefaultMaxStreamsPerPass = 16;
        public const int DefaultReadAheadSize = 1;

        public int MaxStreamsPerPass { get; set; } = DefaultMaxStreamsPerPass;

        public int ReadAheadSize { get; set; } = DefaultReadAheadSize;

        public bool IsValid =>
            MaxStreamsPerPass >= 4
            && ReadAheadSize >= 1;
    }

    /// <summary>
    /// Captures merge-buffer and pass behavior for bounded-memory regression tests.
    /// </summary>
    internal sealed class MembershipMergeDiagnostics
    {
        private int _bufferedRecords;

        public int PeakBufferedRecords { get; private set; }

        public int PeakOpenStreams { get; private set; }

        public int PassCount { get; internal set; }

        public bool UsedHierarchicalMerge { get; internal set; }

        internal void OpenStreams(int count)
        {
            PeakOpenStreams = Math.Max(PeakOpenStreams, count);
        }

        internal void RecordBuffered()
        {
            _bufferedRecords++;
            PeakBufferedRecords = Math.Max(PeakBufferedRecords, _bufferedRecords);
        }

        internal void RecordConsumed()
        {
            _bufferedRecords--;
        }
    }
}
