// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Services.Entities;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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
    /// Collects the counts and content identity needed to cross-check streamed merge outputs.
    /// </summary>
    internal sealed class MembershipMergeSummary : IDisposable
    {
        private IncrementalHash _contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private byte[] _contentFingerprint;

        public int IncludedCount { get; private set; }

        public int ExcludedCount { get; private set; }

        public int DestinationCount { get; private set; }

        public int SourceMemberCount { get; private set; }

        public int AddCount { get; private set; }

        public int RemoveCount { get; private set; }

        /// <summary>
        /// Records one merged member's counts and complete content identity.
        /// </summary>
        public void Record(MembershipMergeRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            AppendRecord(record);

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

        /// <summary>
        /// Confirms that two passes observed the same counts and complete record content.
        /// </summary>
        public bool HasSameContentAs(MembershipMergeSummary other) =>
            other != null
            && IncludedCount == other.IncludedCount
            && ExcludedCount == other.ExcludedCount
            && DestinationCount == other.DestinationCount
            && SourceMemberCount == other.SourceMemberCount
            && AddCount == other.AddCount
            && RemoveCount == other.RemoveCount
            && CryptographicOperations.FixedTimeEquals(CompleteFingerprint(), other.CompleteFingerprint());

        /// <summary>
        /// Releases the incremental hash after validation completes or fails.
        /// </summary>
        public void Dispose()
        {
            _contentHash?.Dispose();
            _contentHash = null;
        }

        /// <summary>
        /// Adds every delta-relevant record field to the bounded content fingerprint.
        /// </summary>
        private void AppendRecord(MembershipMergeRecord record)
        {
            if (_contentHash == null)
            {
                throw new InvalidOperationException("A completed membership merge summary cannot record more members.");
            }

            // Lengths and presence flags keep the hash input unambiguous without retaining records.
            Span<byte> recordHeader = stackalloc byte[21];
            record.ObjectId.TryWriteBytes(recordHeader);
            recordHeader[16] = (byte)(
                (record.Included ? 1 : 0)
                | (record.Excluded ? 2 : 0)
                | (record.InDestination ? 4 : 0));
            BinaryPrimitives.WriteInt32LittleEndian(recordHeader.Slice(17), record.SourceGroups.Length);
            _contentHash.AppendData(recordHeader);

            Span<byte> sourceGroupBytes = stackalloc byte[16];
            foreach (var sourceGroup in record.SourceGroups)
            {
                sourceGroup.TryWriteBytes(sourceGroupBytes);
                _contentHash.AppendData(sourceGroupBytes);
            }

            Span<byte> propertiesHeader = stackalloc byte[5];
            if (!record.DestinationProperties.HasValue)
            {
                propertiesHeader[0] = 0;
                _contentHash.AppendData(propertiesHeader.Slice(0, 1));
                return;
            }

            propertiesHeader[0] = 1;
            var propertiesJson = record.DestinationProperties.Value.GetRawText();
            BinaryPrimitives.WriteInt32LittleEndian(propertiesHeader.Slice(1), Encoding.UTF8.GetByteCount(propertiesJson));
            _contentHash.AppendData(propertiesHeader);
            AppendUtf8(propertiesJson);
        }

        /// <summary>
        /// Hashes text in bounded chunks without allocating its complete UTF-8 representation.
        /// </summary>
        private void AppendUtf8(string value)
        {
            var encoder = Encoding.UTF8.GetEncoder();
            var remaining = value.AsSpan();
            Span<byte> buffer = stackalloc byte[1024];

            while (!remaining.IsEmpty)
            {
                encoder.Convert(
                    remaining,
                    buffer,
                    flush: true,
                    out var charactersUsed,
                    out var bytesUsed,
                    out _);
                _contentHash.AppendData(buffer.Slice(0, bytesUsed));
                remaining = remaining.Slice(charactersUsed);
            }
        }

        /// <summary>
        /// Finalizes the fingerprint once so repeated consistency checks use the same value.
        /// </summary>
        private byte[] CompleteFingerprint()
        {
            // Finalizing consumes the hash state, so retain the result for repeated comparisons.
            if (_contentFingerprint != null)
            {
                return _contentFingerprint;
            }

            if (_contentHash == null)
            {
                throw new ObjectDisposedException(nameof(MembershipMergeSummary));
            }

            _contentFingerprint = _contentHash.GetHashAndReset();
            _contentHash.Dispose();
            _contentHash = null;
            return _contentFingerprint;
        }
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
