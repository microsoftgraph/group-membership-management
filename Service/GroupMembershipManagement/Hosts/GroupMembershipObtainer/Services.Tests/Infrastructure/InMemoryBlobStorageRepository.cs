// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Storage.Blobs.Models;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.BlobStorage;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Services.Infrastructure
{
    /// <summary>
    /// In-memory <see cref="IBlobStorageRepository"/> that stores the same bytes as the production
    /// <see cref="BlobStorageRepository"/> and parses them with the same production readers
    /// (failure-model invariant I13). Blobs are byte arrays; <see cref="GetContent"/> decodes them as
    /// UTF-8, as <c>BinaryData.ToString()</c> does in production downloads.
    /// <para>
    /// Beyond production it offers test hooks: read/write/delete failure paths, before-read/write hooks,
    /// an operation log, write history, ETags with conditional writes that fail like the Azure SDK
    /// (<see cref="RequestFailedException"/> 409 BlobAlreadyExists / 412 ConditionNotMet), injected
    /// precondition failures and per-path latency.
    /// </para>
    /// <para>
    /// Overwriting streaming writes that production issues through <c>OpenWriteAsync(overwrite: true)</c>
    /// (<see cref="UploadFileStreamAsync{T}"/> and both <c>UploadGroupMembershipFromGuidsAsync</c> overloads) are
    /// not atomic, as in Azure: the open commits an empty blob at the name first, the content is committed only when
    /// the stream completes, and a failure or cancellation in between (see <see cref="DuringOverwriteAsync"/>) leaves
    /// the empty blob. Every public write is recorded in <see cref="WriteCalls"/> with its API and whether it is
    /// write-once.
    /// </para>
    /// </summary>
    internal sealed class InMemoryBlobStorageRepository : IBlobStorageRepository
    {
        private const long ETagBase = 0x8DC000000000000;

        private readonly object _lock = new();
        private readonly Dictionary<string, BlobEntry> _blobs =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _stagedBlocks =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Queue<int>> _injectedPreconditionFailures =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TimeSpan> _latencies =
            new(StringComparer.OrdinalIgnoreCase);
        private long _writeSequence;

        public Func<string, Task> BeforeReadAsync { get; set; }
        public Func<string, Task> BeforeWriteAsync { get; set; }

        /// <summary>
        /// Runs before a conditional write evaluates its conditions (after <see cref="BeforeWriteAsync"/>),
        /// for example to let a racing writer change the blob and its ETag.
        /// </summary>
        public Func<string, Task> BeforeConditionalWriteAsync { get; set; }

        public HashSet<string> ReadFailurePaths { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> WriteFailurePaths { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DeleteFailurePaths { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> Operations { get; } = new();
        public List<WriteObservation> WriteHistory { get; } = new();

        /// <summary>Every call of a public write API, in call order (recorded before the write runs).</summary>
        public List<WriteCall> WriteCalls { get; } = new();

        /// <summary>
        /// Runs inside an overwriting streaming write after its open committed the empty blob and before the content
        /// is committed. A throw (or a cancellation of the write's token) interrupts the write and leaves the blob
        /// empty, as an interrupted <c>OpenWriteAsync(overwrite: true)</c> stream does in Azure.
        /// </summary>
        public Func<string, Task> DuringOverwriteAsync { get; set; }

        /// <summary>
        /// Runs after each blob that <see cref="DeleteListedBlobsAsync"/> deleted, with its path, for example to let a
        /// straggling duplicate writer re-upload under a prefix that is being cleaned up.
        /// </summary>
        public Func<string, Task> AfterListedDeleteAsync { get; set; }

        public IReadOnlyCollection<string> Paths
        {
            get
            {
                lock (_lock)
                {
                    return _blobs.Keys.OrderBy(path => path).ToList();
                }
            }
        }

        public string GetContent(string path)
        {
            var bytes = GetBytes(path);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        public byte[] GetBytes(string path)
        {
            lock (_lock)
            {
                return _blobs.TryGetValue(Normalize(path), out var entry)
                    ? entry.Content.ToArray()
                    : null;
            }
        }

        public ETag? GetETag(string path)
        {
            lock (_lock)
            {
                return _blobs.TryGetValue(Normalize(path), out var entry)
                    ? entry.ETag
                    : null;
            }
        }

        /// <summary>Stores raw bytes without running hooks, latency or failure injection.</summary>
        public void PutBytes(
            string path,
            byte[] content,
            Dictionary<string, string> metadata = null)
        {
            lock (_lock)
            {
                WriteLocked(Normalize(path), content, metadata);
            }
        }

        /// <summary>
        /// Makes the next <paramref name="count"/> conditional writes to <paramref name="path"/> fail as the
        /// service would: 409 BlobAlreadyExists or 412 ConditionNotMet. <see cref="UploadFileIfAbsentAsync"/>
        /// maps either to <c>false</c>, as production does.
        /// </summary>
        public void InjectPreconditionFailure(string path, int status = 412, int count = 1)
        {
            if (status != 409 && status != 412)
            {
                throw new ArgumentOutOfRangeException(nameof(status), "Use 409 or 412.");
            }

            lock (_lock)
            {
                var normalizedPath = Normalize(path);
                if (!_injectedPreconditionFailures.TryGetValue(normalizedPath, out var failures))
                {
                    failures = new Queue<int>();
                    _injectedPreconditionFailures[normalizedPath] = failures;
                }

                for (var i = 0; i < count; i++)
                {
                    failures.Enqueue(status);
                }
            }
        }

        /// <summary>
        /// Delays every read, write and listed delete (<see cref="DeleteListedBlobsAsync"/>) of <paramref name="path"/>;
        /// the delay honours cancellation.
        /// </summary>
        public void InjectLatency(string path, TimeSpan delay)
        {
            lock (_lock)
            {
                _latencies[Normalize(path)] = delay;
            }
        }

        /// <summary>
        /// Downloads a blob with its ETag, as <c>BlobClient.DownloadContentAsync</c> exposes
        /// <c>Details.ETag</c>, for read-then-conditional-write flows.
        /// </summary>
        public async Task<InMemoryBlobDownload> DownloadWithETagAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var normalizedPath = Normalize(path);
            await BeforeRead(normalizedPath, cancellationToken);
            lock (_lock)
            {
                ThrowIfReadFails(normalizedPath);
                return _blobs.TryGetValue(normalizedPath, out var entry)
                    ? new InMemoryBlobDownload(ToBlobResult(normalizedPath, entry), entry.ETag)
                    : new InMemoryBlobDownload(new BlobResult { BlobStatus = BlobStatus.NotFound }, null);
            }
        }

        /// <summary>
        /// Uploads with <see cref="BlobRequestConditions"/> as <c>BlobClient.UploadAsync</c> does and returns
        /// the new ETag. Only <c>IfMatch</c> and <c>IfNoneMatch</c> are modelled.
        /// </summary>
        public Task<ETag> UploadFileConditionalAsync(
            string path,
            string content,
            BlobRequestConditions conditions,
            Dictionary<string, string> metadata = null,
            CancellationToken cancellationToken = default)
        {
            return ConditionalWriteAsync(
                path,
                Encoding.UTF8.GetBytes(content ?? string.Empty),
                metadata,
                conditions ?? new BlobRequestConditions(),
                cancellationToken);
        }

        // Production's UploadFileAsync is one BlobClient.UploadAsync (Put Blob, or staged blocks and one Put Block
        // List), so the overwrite is atomic: no empty or partial blob is ever visible.
        public Task UploadFileAsync(
            string path,
            string content,
            Dictionary<string, string> metadata = null)
        {
            return WriteAsync(path, content, metadata);
        }

        public Task UploadFileAsync(
            string path,
            string content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            return WriteAsync(path, content, metadata, cancellationToken);
        }

        public async Task<bool> UploadFileIfAbsentAsync(
            string path,
            string content,
            CancellationToken cancellationToken = default)
        {
            // Mirrors BlobStorageRepository.UploadFileIfAbsentAsync: If-None-Match: * and 409/412 mean "exists".
            try
            {
                await ConditionalWriteAsync(
                    path,
                    Encoding.UTF8.GetBytes(content ?? string.Empty),
                    null,
                    new BlobRequestConditions { IfNoneMatch = ETag.All },
                    cancellationToken);
                return true;
            }
            catch (RequestFailedException exception)
                when (exception.Status == 409 || exception.Status == 412)
            {
                return false;
            }
        }

        public async Task<string> UploadFileBlockAsync(
            string path,
            string content,
            Dictionary<string, string> metadata = null)
        {
            var normalizedPath = Normalize(path);
            var blockId = Guid.NewGuid().ToString("N");
            lock (_lock)
            {
                if (!_stagedBlocks.TryGetValue(normalizedPath, out var blocks))
                {
                    blocks = new List<string>();
                    _stagedBlocks[normalizedPath] = blocks;
                }

                blocks.Add(content);
            }

            await Task.CompletedTask;
            return blockId;
        }

        public Task DeleteFileAsync(string path)
        {
            var normalizedPath = Normalize(path);
            lock (_lock)
            {
                Operations.Add($"delete:{normalizedPath}");
                if (DeleteFailurePaths.Contains(normalizedPath))
                {
                    throw new IOException($"Injected delete failure for '{normalizedPath}'.");
                }

                _blobs.Remove(normalizedPath);
            }

            return Task.CompletedTask;
        }

        public Task DeleteFilesByPrefixAsync(string prefix, bool excludeLatest = false)
        {
            var normalizedPrefix = Normalize(prefix);
            lock (_lock)
            {
                var matches = _blobs
                    .Where(entry => entry.Key.StartsWith(
                        normalizedPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.Value.Sequence)
                    .Select(entry => entry.Key)
                    .ToList();
                if (excludeLatest && matches.Count > 0)
                {
                    matches.RemoveAt(0);
                }

                foreach (var path in matches)
                {
                    _blobs.Remove(path);
                }
            }

            return Task.CompletedTask;
        }

        public async Task<BlobResult> DownloadFileAsync(string path) =>
            (await DownloadWithETagAsync(path)).Result;

        public async Task<BlobResult> DownloadFileAsync(string path, CancellationToken cancellationToken) =>
            (await DownloadWithETagAsync(path, cancellationToken)).Result;

        public async Task<BlobResult> DownloadCacheFileAsync(string path)
        {
            var latest = await FindLatestFileAsync(path);
            return latest.BlobStatus == BlobStatus.Found
                ? await DownloadFileAsync(latest.Path)
                : latest;
        }

        public Task DeleteFilesAsync(string path) =>
            DeleteFilesByPrefixAsync(path);

        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path)
        {
            var normalizedPath = Normalize(path);
            lock (_lock)
            {
                return Task.FromResult(
                    _blobs.TryGetValue(normalizedPath, out var entry)
                        ? new BlobMetadataResult
                        {
                            BlobStatus = BlobStatus.Found,
                            Metadata = new Dictionary<string, string>(entry.Metadata)
                        }
                        : new BlobMetadataResult
                        {
                            BlobStatus = BlobStatus.NotFound
                        });
            }
        }

        public async Task CommitFileAsync(string path, List<string> blockIds)
        {
            var normalizedPath = Normalize(path);
            string content;
            lock (_lock)
            {
                content = _stagedBlocks.TryGetValue(normalizedPath, out var blocks)
                    ? string.Concat(blocks)
                    : string.Empty;
                _stagedBlocks.Remove(normalizedPath);
            }

            await WriteAsync(normalizedPath, content, null);
        }

        public async Task<List<AzureADUser>> ReadBlobsAsync(string path)
        {
            // Mirrors production: blobs in name order, first occurrence of each ObjectId wins.
            var seenIds = new HashSet<Guid>();
            var uniqueUsers = new List<AzureADUser>();
            foreach (var entry in SnapshotByPrefix(path))
            {
                var users = await DeserializeUsersAsync(entry.Content)
                    ?? throw new Exception($"Failed to deserialize blob: {entry.Path}");
                foreach (var user in users)
                {
                    if (seenIds.Add(user.ObjectId))
                    {
                        uniqueUsers.Add(user);
                    }
                }
            }

            return uniqueUsers;
        }

        public async IAsyncEnumerable<AzureADUser> StreamUsersFromBlobsAsync(
            string path)
        {
            // Mirrors production: blobs in name order, null lists and users skipped, ObjectIds deduplicated.
            var seenIds = new HashSet<Guid>();
            foreach (var entry in SnapshotByPrefix(path))
            {
                var users = await DeserializeUsersAsync(entry.Content);
                if (users == null)
                {
                    continue;
                }

                foreach (var user in users)
                {
                    if (user != null && seenIds.Add(user.ObjectId))
                    {
                        yield return user;
                    }
                }
            }
        }

        public Task DeleteBlobsAsync(string path) =>
            DeleteFilesByPrefixAsync(path);

        public Task<BlobResult> FindLatestFileAsync(string prefix)
        {
            var normalizedPrefix = Normalize(prefix);
            lock (_lock)
            {
                var latest = _blobs
                    .Where(entry => entry.Key.StartsWith(
                        normalizedPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.Value.Sequence)
                    .FirstOrDefault();
                return Task.FromResult(
                    latest.Value == null
                        ? new BlobResult { BlobStatus = BlobStatus.NotFound }
                        : new BlobResult
                        {
                            BlobStatus = BlobStatus.Found,
                            Path = latest.Key,
                            LastModified = latest.Value.LastModified
                        });
            }
        }

        public Task<BlobResult> FindAggregatedFileByRunIdAsync(
            string groupId,
            string runId)
        {
            lock (_lock)
            {
                var suffix = $"_{runId}_Aggregated.json";
                var path = _blobs.Keys.FirstOrDefault(candidate =>
                    candidate.StartsWith($"{groupId}/", StringComparison.OrdinalIgnoreCase) &&
                    candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(
                    path == null
                        ? new BlobResult { BlobStatus = BlobStatus.NotFound }
                        : new BlobResult
                        {
                            BlobStatus = BlobStatus.Found,
                            Path = path
                        });
            }
        }

        public Task<Dictionary<string, BlobResult>> FindPartFilesByRunIdAsync(
            string groupId,
            string runId)
        {
            var result = new Dictionary<string, BlobResult>(
                StringComparer.OrdinalIgnoreCase);
            var marker = $"_{runId}_";
            lock (_lock)
            {
                foreach (var path in _blobs.Keys.Where(candidate =>
                    candidate.StartsWith($"{groupId}/", StringComparison.OrdinalIgnoreCase) &&
                    candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    !candidate.EndsWith("_Aggregated.json", StringComparison.OrdinalIgnoreCase)))
                {
                    var markerIndex = path.IndexOf(
                        marker,
                        StringComparison.OrdinalIgnoreCase);
                    if (markerIndex < 0)
                    {
                        continue;
                    }

                    var tagStart = markerIndex + marker.Length;
                    var tag = path.Substring(
                        tagStart,
                        path.Length - tagStart - ".json".Length);
                    result[tag] = new BlobResult
                    {
                        BlobStatus = BlobStatus.Found,
                        Path = path
                    };
                }
            }

            return Task.FromResult(result);
        }

        public async Task<HashSet<T>> ReadValuesFromBlobAsync<T>(
            string path,
            Func<string, T> parseFunction) =>
            await ReadValuesFromBlobAsync(
                path,
                parseFunction,
                CancellationToken.None);

        public async Task<HashSet<T>> ReadValuesFromBlobAsync<T>(
            string path,
            Func<string, T> parseFunction,
            CancellationToken cancellationToken)
        {
            var result = new HashSet<T>();
            var content = await ReadBytesAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (content == null)
            {
                return result;
            }

            // Production reads with a StreamReader, which strips a UTF-8 byte order mark.
            using var reader = new StreamReader(new MemoryStream(content, writable: false));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    result.Add(parseFunction(line));
                }
            }

            return result;
        }

        public async Task<HashSet<Guid>> ExtractGroupMembershipSourceMembersAsync(
            string path)
        {
            var content = await ReadExistingBytesAsync(path);
            return GroupMembershipSourceMembersStreamingExtractor.Extract(
                new MemoryStream(content, writable: false));
        }

        public async Task<int> StreamMembershipToCacheAsync(
            string sourceMembershipFilePath,
            string destinationCacheFilePath,
            Dictionary<string, string> metadata = null)
        {
            // Mirrors production: ids in source order, not deduplicated, newline-separated without a
            // trailing newline, and NumberOfUsers added to the metadata. Production stages blocks and commits
            // once, so the overwrite is atomic.
            var content = await ReadExistingBytesAsync(sourceMembershipFilePath);
            var ids = GroupMembershipSourceMembersStreamingExtractor
                .EnumerateGuids(new MemoryStream(content, writable: false))
                .ToList();
            var finalMetadata = metadata == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(metadata);
            finalMetadata["NumberOfUsers"] = ids.Count.ToString();
            await WriteAsync(
                destinationCacheFilePath,
                string.Join(Environment.NewLine, ids),
                finalMetadata);
            return ids.Count;
        }

        public Task UploadFileStreamAsync<T>(
            string path,
            T content,
            Dictionary<string, string> metadata = null,
            JsonSerializerOptions serializerOptions = null)
        {
            // Production: OpenWriteAsync(overwrite: true), then JsonSerializer.SerializeAsync into the stream.
            return OpenWriteOverwriteAsync(
                path,
                (stream, _) => JsonSerializer.SerializeAsync(stream, content, serializerOptions),
                metadata,
                CancellationToken.None);
        }

        public Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query) =>
            UploadGroupMembershipFromGuidsAsync(
                membershipFilePath,
                sourceMemberIds,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query,
                CancellationToken.None);

        public async Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            CancellationToken cancellationToken)
        {
            // The production serializer, so the stored bytes are identical. Production writes through
            // OpenWriteAsync(overwrite: true): the open commits an empty blob first.
            await OpenWriteOverwriteAsync(
                membershipFilePath,
                (stream, token) => BlobStorageRepository.WriteGroupMembershipFromGuidsAsync(
                    stream,
                    sourceMemberIds,
                    destination,
                    runId,
                    syncJobId,
                    exclusionary,
                    membershipObtainerDryRunEnabled,
                    query,
                    token),
                null,
                cancellationToken);
        }

        public async Task UploadGroupMembershipFromGuidsAsync(
            string filePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            Guid sourceGroupId,
            CancellationToken cancellationToken = default)
        {
            // The production serializer and validation, so the stored bytes are identical. Production writes through
            // OpenWriteAsync(overwrite: true): the open commits an empty blob first.
            BlobStorageRepository.ValidateSourceGroup(sourceGroupId);
            await OpenWriteOverwriteAsync(
                filePath,
                (stream, token) => BlobStorageRepository.WriteGroupMembershipFromGuidsAsync(
                    stream,
                    sourceMemberIds,
                    destination,
                    runId,
                    syncJobId,
                    exclusionary,
                    membershipObtainerDryRunEnabled,
                    query,
                    token,
                    sourceGroupId),
                null,
                cancellationToken);
        }

        public Task UploadCacheFromGuidsAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null)
        {
            // Mirrors production: ids as given, newline-separated without a trailing newline. Production stages
            // blocks and commits once, so the overwrite is atomic.
            var content = string.Join(Environment.NewLine, sourceMemberIds);
            return WriteAsync(destinationCacheFilePath, content, metadata);
        }

        public async Task<int> MergeAndStreamUserBlobsAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            // The production streaming merge and writer over the stored page uploads, with the production fallback
            // to the materializing merge when an upload is not canonically sorted.
            byte[] streamed;
            int count;
            try
            {
                (streamed, count) = await StreamMergeAsync(
                    sourceBlobPrefix, destination, runId, syncJobId, exclusionary, membershipObtainerDryRunEnabled, query);
            }
            catch (UnsortedUserUploadException exception)
            {
                UserUploadMerge.LogUnsortedFallback(MergeLogger, sourceBlobPrefix, exception);
                var membership = await ReadMergedMembershipAsync(
                    sourceBlobPrefix,
                    destination,
                    runId,
                    syncJobId,
                    exclusionary,
                    membershipObtainerDryRunEnabled,
                    query);
                await WriteAsync(destinationPath, JsonSerializer.Serialize(membership), null);
                return membership.SourceMembers.Count;
            }

            await WriteBytesAsync(destinationPath, streamed, null);
            return count;
        }

        /// <summary>Receives the merge's unsorted-input fallback warning, as the production repository's logger does.</summary>
        public Microsoft.Extensions.Logging.ILogger MergeLogger { get; set; }

        private async Task<(byte[] Content, int Count)> StreamMergeAsync(
            string sourceBlobPrefix,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            var inputs = SnapshotByPrefix(sourceBlobPrefix)
                .Select(entry => new UserUploadInput(
                    entry.Path,
                    _ => Task.FromResult<Stream>(new MemoryStream(entry.Content, writable: false))))
                .ToList();
            var envelope = BlobStorageRepository.MergeEnvelope(
                destination, runId, syncJobId, exclusionary, membershipObtainerDryRunEnabled, query);
            using var content = new MemoryStream();
            var count = await UserUploadMerge.WriteMergedAsync(content, envelope, inputs, CancellationToken.None);
            return (content.ToArray(), count);
        }

        public async Task<WriteOnceResult> UploadFileWriteOnceAsync(
            string path,
            string content,
            Dictionary<string, string> metadata = null,
            CancellationToken cancellationToken = default)
        {
            // Production encodes the text as UTF-8 without a BOM, as UploadFileAsync does.
            var created = await WriteIfAbsentAsync(
                path,
                Encoding.UTF8.GetBytes(content ?? string.Empty),
                metadata,
                cancellationToken);
            return new WriteOnceResult(!created);
        }

        public async Task<WriteOnceResult> UploadGroupMembershipFromGuidsWriteOnceAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            CancellationToken cancellationToken = default)
        {
            // The production serializer, so the stored bytes are identical.
            using var content = new MemoryStream();
            await BlobStorageRepository.WriteGroupMembershipFromGuidsAsync(
                content,
                sourceMemberIds,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query,
                cancellationToken);
            var created = await WriteIfAbsentAsync(
                membershipFilePath,
                content.ToArray(),
                null,
                cancellationToken);
            return new WriteOnceResult(!created, sourceMemberIds.Count);
        }

        public async Task<WriteOnceResult> UploadCacheFromGuidsWriteOnceAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null)
        {
            // Mirrors production: ids as given, newline-separated without a trailing newline; the count is
            // the number of ids written (or that would have been written, when the blob already exists).
            var ids = sourceMemberIds.ToList();
            var created = await WriteIfAbsentAsync(
                destinationCacheFilePath,
                Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, ids)),
                metadata);
            return new WriteOnceResult(!created, ids.Count);
        }

        public async Task<WriteOnceResult> StreamMembershipToCacheWriteOnceAsync(
            string sourceMembershipFilePath,
            string destinationCacheFilePath,
            Dictionary<string, string> metadata = null)
        {
            // Mirrors production: the same content and metadata as StreamMembershipToCacheAsync.
            var content = await ReadExistingBytesAsync(sourceMembershipFilePath);
            var ids = GroupMembershipSourceMembersStreamingExtractor
                .EnumerateGuids(new MemoryStream(content, writable: false))
                .ToList();
            var finalMetadata = metadata == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(metadata);
            finalMetadata["NumberOfUsers"] = ids.Count.ToString();
            var created = await WriteIfAbsentAsync(
                destinationCacheFilePath,
                Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, ids)),
                finalMetadata);
            return new WriteOnceResult(!created, ids.Count);
        }

        public async Task<WriteOnceResult> MergeAndStreamUserBlobsWriteOnceAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            byte[] content;
            int count;
            try
            {
                (content, count) = await StreamMergeAsync(
                    sourceBlobPrefix, destination, runId, syncJobId, exclusionary, membershipObtainerDryRunEnabled, query);
            }
            catch (UnsortedUserUploadException exception)
            {
                UserUploadMerge.LogUnsortedFallback(MergeLogger, sourceBlobPrefix, exception);
                var membership = await ReadMergedMembershipAsync(
                    sourceBlobPrefix,
                    destination,
                    runId,
                    syncJobId,
                    exclusionary,
                    membershipObtainerDryRunEnabled,
                    query);
                content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(membership));
                count = membership.SourceMembers.Count;
            }

            var created = await WriteIfAbsentAsync(
                destinationPath,
                content,
                null);
            if (created)
            {
                return new WriteOnceResult(false, count);
            }

            // As in production, report the existing blob's count, not the current listing's.
            var existing = await ExtractGroupMembershipSourceMembersAsync(destinationPath);
            return new WriteOnceResult(true, existing.Count);
        }

        private async Task<GroupMembership> ReadMergedMembershipAsync(
            string sourceBlobPrefix,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            var uniqueMembers = new Dictionary<Guid, AzureADUser>();
            foreach (var entry in SnapshotByPrefix(sourceBlobPrefix))
            {
                var members = await DeserializeUsersAsync(entry.Content)
                    ?? throw new Exception($"Failed to deserialize blob: {entry.Path}");
                foreach (var member in members.Where(member => member != null))
                {
                    uniqueMembers.TryAdd(member.ObjectId, member);
                }
            }

            var orderedMembers = uniqueMembers.Values.ToList();
            orderedMembers.Sort(CanonicalMemberComparer<AzureADUser>.Instance);
            return new GroupMembership
            {
                SourceMembers = orderedMembers,
                Destination = destination,
                RunId = runId,
                SyncJobId = syncJobId,
                Exclusionary = exclusionary,
                MembershipObtainerDryRunEnabled =
                    membershipObtainerDryRunEnabled,
                Query = query
            };
        }

        public async IAsyncEnumerable<AzureADUser> StreamMembershipAsync(
            string path,
            Action<GroupMembership> onMembershipDetailsKnown = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // The production reader, so plain, BOM-prefixed and compressed blobs behave as in production.
            var content = await ReadExistingBytesAsync(path, cancellationToken);
            await using var stream = new MemoryStream(content, writable: false);
            await foreach (var member in MembershipStream.ReadAsync(
                stream,
                onMembershipDetailsKnown,
                cancellationToken))
            {
                yield return member;
            }
        }

        public async Task WriteMembershipAsync(
            string path,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            Dictionary<string, string> metadata = null,
            CancellationToken cancellationToken = default)
        {
            // The production writer (Brotli + Base64). As in production, nothing is published unless the
            // whole membership was written.
            using var content = new MemoryStream();
            await MembershipStream.WriteAsync(content, envelope, members, cancellationToken);
            await WriteBytesAsync(path, content.ToArray(), metadata, cancellationToken);
        }

        // Selection pipeline members (story 16751398, slice F). Each uses the production serializer, so the stored
        // bytes are identical, and commits through the same write-once or conditional path as production.

        public async Task<WriteOnceResult> UploadGroupMembershipFromGuidsWriteOnceAsync(
            string filePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            Guid sourceGroupId,
            CancellationToken cancellationToken = default)
        {
            // The production validation and serializer, so the stored bytes are identical.
            BlobStorageRepository.ValidateSourceGroup(sourceGroupId);
            if (BeforePartWriterAsync != null)
            {
                await BeforePartWriterAsync(Normalize(filePath));
            }

            using var content = new MemoryStream();
            await BlobStorageRepository.WriteGroupMembershipFromGuidsAsync(
                content,
                sourceMemberIds,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query,
                cancellationToken,
                sourceGroupId);
            var created = await WriteIfAbsentAsync(
                filePath,
                content.ToArray(),
                null,
                cancellationToken);
            return new WriteOnceResult(!created, sourceMemberIds.Count);
        }

        /// <summary>
        /// Runs when a selection-pipeline part writer is called, before the double serializes (and buffers) the part,
        /// so a test can sample the caller's live memory without the double's own buffer.
        /// </summary>
        public Func<string, Task> BeforePartWriterAsync { get; set; }

        /// <summary>Counts the bytes staged by streaming writers (I12 blob-byte counter).</summary>
        public long StreamedWriteBytes => Interlocked.Read(ref _streamedWriteBytes);

        private long _streamedWriteBytes;

        public async Task<WriteOnceResult> UploadGroupMembershipStreamWriteOnceAsync(
            string path,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            Action beforeCommit,
            CancellationToken stagingCancellationToken,
            CancellationToken commitCancellationToken)
        {
            // Production stages blocks while streaming and commits once; here the staged bytes are buffered, the
            // path's injected latency models the block uploads (cancelled by the staging token), then one commit.
            if (BeforePartWriterAsync != null)
            {
                await BeforePartWriterAsync(Normalize(path));
            }

            using var content = new CountingMemoryStream(count => Interlocked.Add(ref _streamedWriteBytes, count));
            var count = await BlobStorageRepository.WriteGroupMembershipStreamAsync(
                content,
                envelope,
                members,
                stagingCancellationToken);
            await DelayAsync(Normalize(path), stagingCancellationToken);
            beforeCommit?.Invoke();
            var created = await WriteIfAbsentAsync(path, content.ToArray(), null, commitCancellationToken, applyLatency: false);
            if (created)
            {
                return new WriteOnceResult(false, count);
            }

            var existing = await ExtractGroupMembershipSourceMembersAsync(path);
            return new WriteOnceResult(true, existing.Count);
        }

        public async Task<BlobStreamWithETag> OpenReadWithETagAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var normalizedPath = Normalize(path);
            await BeforeRead(normalizedPath, cancellationToken);
            lock (_lock)
            {
                ThrowIfReadFails(normalizedPath);
                Operations.Add($"read:{normalizedPath}");
                return _blobs.TryGetValue(normalizedPath, out var entry)
                    ? new BlobStreamWithETag(
                        new MemoryStream(entry.Content, writable: false),
                        entry.ETag.ToString(),
                        entry.LastModified)
                    : null;
            }
        }

        public async Task<bool> UploadFileConditionalStreamAsync(
            string path,
            Func<Stream, CancellationToken, Task> writeContent,
            string ifMatchETag,
            CancellationToken cancellationToken = default)
        {
            using var content = new MemoryStream();
            await writeContent(content, cancellationToken);
            var conditions = string.IsNullOrEmpty(ifMatchETag)
                ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                : new BlobRequestConditions { IfMatch = new ETag(ifMatchETag) };
            try
            {
                await ConditionalWriteAsync(path, content.ToArray(), null, conditions, cancellationToken);
                return true;
            }
            catch (RequestFailedException exception)
                when (exception.Status == 409 || exception.Status == 412)
            {
                lock (_lock)
                {
                    Operations.Add($"write-rejected:{Normalize(path)}");
                }

                return false;
            }
        }

        public async Task<int> DeleteListedBlobsAsync(
            IReadOnlyCollection<string> exactPaths,
            IReadOnlyCollection<string> prefixes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deleted = 0;
            var targets = new List<string>();
            lock (_lock)
            {
                foreach (var exactPath in exactPaths ?? Array.Empty<string>())
                {
                    var normalizedPath = Normalize(exactPath);
                    if (!string.IsNullOrEmpty(normalizedPath) && _blobs.ContainsKey(normalizedPath))
                    {
                        targets.Add(normalizedPath);
                    }
                }

                foreach (var prefix in prefixes ?? Array.Empty<string>())
                {
                    var normalizedPrefix = Normalize(prefix);
                    if (string.IsNullOrEmpty(normalizedPrefix))
                    {
                        continue;
                    }

                    // Azure prefixes are case-sensitive.
                    targets.AddRange(_blobs.Keys.Where(key => key.StartsWith(normalizedPrefix, StringComparison.Ordinal)));
                }
            }

            // One delete request per listed blob, as production issues them; a path's injected latency delays its
            // delete and honours cancellation.
            foreach (var target in targets.Distinct(StringComparer.Ordinal).ToList())
            {
                await DelayAsync(target, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_lock)
                {
                    Operations.Add($"delete:{target}");
                    if (DeleteFailurePaths.Contains(target))
                    {
                        throw new IOException($"Injected delete failure for '{target}'.");
                    }

                    if (_blobs.Remove(target))
                    {
                        deleted++;
                    }
                }

                if (AfterListedDeleteAsync != null)
                {
                    await AfterListedDeleteAsync(target);
                }
            }

            return deleted;
        }

        private sealed class CountingMemoryStream : MemoryStream
        {
            private readonly Action<int> _onWrite;

            public CountingMemoryStream(Action<int> onWrite)
            {
                _onWrite = onWrite;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _onWrite(count);
                base.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                _onWrite(buffer.Length);
                base.Write(buffer);
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                _onWrite(buffer.Length);
                return base.WriteAsync(buffer, cancellationToken);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                _onWrite(count);
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }
        }

        private static async Task<List<AzureADUser>> DeserializeUsersAsync(byte[] content)
        {
            using var stream = new MemoryStream(content, writable: false);
            return await JsonSerializer.DeserializeAsync<List<AzureADUser>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private async Task<byte[]> ReadBytesAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var normalizedPath = Normalize(path);
            await BeforeRead(normalizedPath, cancellationToken);
            lock (_lock)
            {
                ThrowIfReadFails(normalizedPath);
                return _blobs.TryGetValue(normalizedPath, out var entry)
                    ? entry.Content
                    : null;
            }
        }

        private async Task<byte[]> ReadExistingBytesAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            await ReadBytesAsync(path, cancellationToken)
            ?? throw new FileNotFoundException(path);

        private Task WriteAsync(
            string path,
            string content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken = default,
            [CallerMemberName] string api = null) =>
            WriteBytesAsync(
                path,
                Encoding.UTF8.GetBytes(content ?? string.Empty),
                metadata,
                cancellationToken,
                api);

        /// <summary>
        /// An atomic overwrite, as production's single <c>UploadAsync</c> (Put Blob, or staged blocks and one
        /// Put Block List) is: the previous content stays visible until the new content is committed.
        /// </summary>
        private async Task WriteBytesAsync(
            string path,
            byte[] content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken = default,
            [CallerMemberName] string api = null)
        {
            RecordWriteCall(api, path, writeOnce: false);
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = Normalize(path);
            await BeforeWrite(normalizedPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                ThrowIfWriteFails(normalizedPath);
                WriteLocked(normalizedPath, content, metadata);
            }
        }

        /// <summary>
        /// Models production's <c>BlobClient.OpenWriteAsync(overwrite: true)</c> followed by writing the stream: the
        /// open itself commits an empty blob (replacing any existing content), the content is committed only when
        /// the stream completes, and a failure or cancellation in between leaves the empty blob at the name.
        /// </summary>
        private async Task OpenWriteOverwriteAsync(
            string path,
            Func<Stream, CancellationToken, Task> writeContent,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken,
            [CallerMemberName] string api = null)
        {
            RecordWriteCall(api, path, writeOnce: false);
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = Normalize(path);
            await BeforeWrite(normalizedPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 1. The open: an unconditional Put Blob of empty content.
            lock (_lock)
            {
                ThrowIfWriteFails(normalizedPath);
                Operations.Add($"open-write:{normalizedPath}");
                CommitLocked(normalizedPath, Array.Empty<byte>(), metadata);
            }

            // 2. The stream body. An interruption here leaves the empty blob committed above.
            if (DuringOverwriteAsync != null)
            {
                await DuringOverwriteAsync(normalizedPath).WaitAsync(cancellationToken);
            }

            using var content = new MemoryStream();
            await writeContent(content, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 3. The stream's final Put Block List.
            lock (_lock)
            {
                WriteLocked(normalizedPath, content.ToArray(), metadata);
            }
        }

        private void RecordWriteCall(string api, string path, bool writeOnce)
        {
            lock (_lock)
            {
                WriteCalls.Add(new WriteCall(api, Normalize(path), writeOnce));
            }
        }

        /// <summary>
        /// Write-once semantics, as the production staged-block commit conditioned on If-None-Match: *:
        /// creates the blob only when the path is absent (checked atomically under the lock) and returns
        /// whether this call created it. A rejection is recorded as a <c>write-rejected:</c> operation.
        /// An injected 409/412 is treated as production does: "already written" when the blob exists,
        /// otherwise rethrown.
        /// </summary>
        private async Task<bool> WriteIfAbsentAsync(
            string path,
            byte[] content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken = default,
            bool applyLatency = true,
            [CallerMemberName] string api = null)
        {
            RecordWriteCall(api, path, writeOnce: true);
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = Normalize(path);
            if (applyLatency)
            {
                await BeforeWrite(normalizedPath, cancellationToken);
            }
            else if (BeforeWriteAsync != null)
            {
                await BeforeWriteAsync(normalizedPath).WaitAsync(cancellationToken);
            }

            if (BeforeConditionalWriteAsync != null)
            {
                await BeforeConditionalWriteAsync(normalizedPath).WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                ThrowIfWriteFails(normalizedPath);
                var exists = _blobs.ContainsKey(normalizedPath);
                if (_injectedPreconditionFailures.TryGetValue(normalizedPath, out var failures) &&
                    failures.Count > 0)
                {
                    var status = failures.Dequeue();
                    if (!exists)
                    {
                        throw status == 409 ? BlobAlreadyExists(normalizedPath) : ConditionNotMet(normalizedPath);
                    }
                }

                if (exists)
                {
                    Operations.Add($"write-rejected:{normalizedPath}");
                    return false;
                }

                WriteLocked(normalizedPath, content, metadata);
                return true;
            }
        }

        private async Task<ETag> ConditionalWriteAsync(
            string path,
            byte[] content,
            Dictionary<string, string> metadata,
            BlobRequestConditions conditions,
            CancellationToken cancellationToken,
            [CallerMemberName] string api = null)
        {
            RecordWriteCall(api, path, writeOnce: conditions?.IfNoneMatch == ETag.All);
            if (conditions.IfModifiedSince != null ||
                conditions.IfUnmodifiedSince != null ||
                conditions.TagConditions != null ||
                conditions.LeaseId != null)
            {
                throw new NotSupportedException(
                    "The in-memory blob double models only IfMatch and IfNoneMatch conditions.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = Normalize(path);
            await BeforeWrite(normalizedPath, cancellationToken);
            if (BeforeConditionalWriteAsync != null)
            {
                await BeforeConditionalWriteAsync(normalizedPath).WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                ThrowIfWriteFails(normalizedPath);
                if (_injectedPreconditionFailures.TryGetValue(normalizedPath, out var failures) &&
                    failures.Count > 0)
                {
                    var status = failures.Dequeue();
                    throw status == 409 ? BlobAlreadyExists(normalizedPath) : ConditionNotMet(normalizedPath);
                }

                var exists = _blobs.TryGetValue(normalizedPath, out var current);
                if (conditions.IfNoneMatch is { } ifNoneMatch && exists)
                {
                    if (ifNoneMatch == ETag.All)
                    {
                        // Put Blob with If-None-Match: * on an existing blob.
                        throw BlobAlreadyExists(normalizedPath);
                    }

                    if (ifNoneMatch == current.ETag)
                    {
                        throw ConditionNotMet(normalizedPath);
                    }
                }

                if (conditions.IfMatch is { } ifMatch &&
                    (!exists || (ifMatch != ETag.All && ifMatch != current.ETag)))
                {
                    throw ConditionNotMet(normalizedPath);
                }

                return WriteLocked(normalizedPath, content, metadata);
            }
        }

        private async Task BeforeRead(
            string path,
            CancellationToken cancellationToken = default)
        {
            await DelayAsync(path, cancellationToken);
            if (BeforeReadAsync != null)
            {
                await BeforeReadAsync(path).WaitAsync(cancellationToken);
            }
        }

        private async Task BeforeWrite(
            string path,
            CancellationToken cancellationToken)
        {
            await DelayAsync(path, cancellationToken);
            if (BeforeWriteAsync != null)
            {
                await BeforeWriteAsync(path).WaitAsync(cancellationToken);
            }
        }

        private Task DelayAsync(string path, CancellationToken cancellationToken)
        {
            TimeSpan delay;
            lock (_lock)
            {
                if (!_latencies.TryGetValue(path, out delay))
                {
                    return Task.CompletedTask;
                }
            }

            return Task.Delay(delay, cancellationToken);
        }

        private ETag WriteLocked(
            string path,
            byte[] content,
            Dictionary<string, string> metadata)
        {
            Operations.Add($"write:{path}");
            WriteHistory.Add(new WriteObservation(path, content?.Length ?? 0));
            return CommitLocked(path, content, metadata);
        }

        /// <summary>Stores a committed version (new ETag and sequence) without recording a content write.</summary>
        private ETag CommitLocked(
            string path,
            byte[] content,
            Dictionary<string, string> metadata)
        {
            _writeSequence++;
            var stored = content?.ToArray() ?? Array.Empty<byte>();
            var etag = new ETag($"\"0x{ETagBase + _writeSequence:X}\"");
            _blobs[path] = new BlobEntry
            {
                Content = stored,
                Metadata = metadata == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(metadata),
                LastModified = DateTimeOffset.UtcNow.AddTicks(_writeSequence),
                Sequence = _writeSequence,
                ETag = etag
            };
            return etag;
        }

        private List<(string Path, byte[] Content)> SnapshotByPrefix(string prefix)
        {
            // Azure lists blobs in name order.
            var normalizedPrefix = Normalize(prefix);
            lock (_lock)
            {
                return _blobs
                    .Where(entry => entry.Key.StartsWith(
                        normalizedPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => (entry.Key, entry.Value.Content))
                    .ToList();
            }
        }

        private void ThrowIfReadFails(string path)
        {
            if (ReadFailurePaths.Contains(path))
            {
                throw new IOException($"Injected read failure for '{path}'.");
            }
        }

        private void ThrowIfWriteFails(string path)
        {
            if (WriteFailurePaths.Contains(path))
            {
                throw new IOException($"Injected write failure for '{path}'.");
            }
        }

        private static RequestFailedException BlobAlreadyExists(string path) =>
            new(409, $"The specified blob already exists. ('{path}')", "BlobAlreadyExists", null);

        private static RequestFailedException ConditionNotMet(string path) =>
            new(412, $"The condition specified using HTTP conditional header(s) is not met. ('{path}')", "ConditionNotMet", null);

        private static BlobResult ToBlobResult(string path, BlobEntry entry) =>
            new()
            {
                BlobStatus = BlobStatus.Found,
                Path = path,
                Content = Encoding.UTF8.GetString(entry.Content),
                LastModified = entry.LastModified,
                Metadata = new Dictionary<string, string>(entry.Metadata)
            };

        private static string Normalize(string path) =>
            path?.TrimStart('/') ?? string.Empty;

        private sealed class BlobEntry
        {
            public byte[] Content { get; init; }
            public Dictionary<string, string> Metadata { get; init; }
            public DateTimeOffset LastModified { get; init; }
            public long Sequence { get; init; }
            public ETag ETag { get; init; }
        }

        internal sealed record WriteObservation(string Path, int ByteCount);

        /// <summary>One call of a public write API: its name, the normalized path and whether it is write-once.</summary>
        internal sealed record WriteCall(string Api, string Path, bool WriteOnce);
    }

    internal sealed record InMemoryBlobDownload(BlobResult Result, ETag? ETag);
}
