// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    public class BlobStorageRepository : IBlobStorageRepository
    {
        private BlobContainerClient _containerClient;
        private readonly Func<string, BlockBlobClient> _blockBlobClientFactory;
        private readonly ILogger _logger;

        public BlobStorageRepository(string containerUrl)
            : this(containerUrl, logger: null)
        {
        }

        /// <param name="containerUrl">The container URL.</param>
        /// <param name="logger">Receives warnings, for example when a merge falls back to its materializing path.</param>
        public BlobStorageRepository(string containerUrl, ILogger logger)
        {
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

            _containerClient = new BlobContainerClient(new Uri(containerUrl), credential);
            _blockBlobClientFactory = path => _containerClient.GetBlockBlobClient(path);
            _logger = logger ?? NullLogger.Instance;
        }

        public BlobStorageRepository(BlobContainerClient containerClient)
            : this(containerClient, logger: null)
        {
        }

        public BlobStorageRepository(BlobContainerClient containerClient, ILogger logger)
        {
            _containerClient = containerClient ?? throw new ArgumentNullException(nameof(containerClient));
            _blockBlobClientFactory = path => _containerClient.GetBlockBlobClient(path);
            _logger = logger ?? NullLogger.Instance;
        }

        internal BlobStorageRepository(
            BlobContainerClient containerClient,
            Func<string, BlockBlobClient> blockBlobClientFactory,
            ILogger logger = null)
        {
            _containerClient = containerClient ?? throw new ArgumentNullException(nameof(containerClient));
            _blockBlobClientFactory = blockBlobClientFactory
                ?? throw new ArgumentNullException(nameof(blockBlobClientFactory));
            _logger = logger ?? NullLogger.Instance;
        }

        public async Task DeleteFileAsync(string path)
        {
            await _containerClient.DeleteBlobIfExistsAsync(path, DeleteSnapshotsOption.IncludeSnapshots);
        }

        public Task<BlobResult> DownloadFileAsync(string path) =>
            DownloadFileAsync(path, CancellationToken.None);

        public async Task<BlobResult> DownloadFileAsync(string path, CancellationToken cancellationToken)
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var blobExists = await blobClient.ExistsAsync(cancellationToken);
            if (blobExists)
            {
                var content = await blobClient.DownloadContentAsync(cancellationToken);
                return new BlobResult
                {
                    Content = content.Value.Content == null ? string.Empty : content.Value.Content.ToString(),
                    Metadata = content.Value.Details.Metadata,
                    BlobStatus = BlobStatus.Found
                };
            }

            return new BlobResult { BlobStatus = BlobStatus.NotFound };
        }

        public async Task<List<AzureADUser>> ReadBlobsAsync(string path)
        {
            var seenIds = new HashSet<Guid>();
            var uniqueUsers = new List<AzureADUser>();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var blobs = _containerClient.GetBlobsAsync(prefix: path);
            await foreach (BlobItem blobItem in blobs)
            {
                await using var stream = await _containerClient.GetBlobClient(blobItem.Name).OpenReadAsync();
                var users = await JsonSerializer.DeserializeAsync<List<AzureADUser>>(stream, options);
                if (users == null)
                {
                    throw new Exception($"Failed to deserialize blob: {blobItem.Name}");
                }

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

        public async IAsyncEnumerable<AzureADUser> StreamUsersFromBlobsAsync(string path)
        {
            var seenIds = new HashSet<Guid>();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var blobs = _containerClient.GetBlobsAsync(prefix: path);
            await foreach (BlobItem blobItem in blobs)
            {
                await using var stream = await _containerClient.GetBlobClient(blobItem.Name).OpenReadAsync();
                var users = await JsonSerializer.DeserializeAsync<List<AzureADUser>>(stream, options);
                if (users == null)
                {
                    continue; // Handle the case where users is null
                }

                foreach (var user in users)
                {
                    if (user == null)
                    {
                        continue;
                    }

                    if (seenIds.Add(user.ObjectId))
                    {
                        yield return user;
                    }
                }
            }
        }

        public async Task DeleteBlobsAsync(string path)
        {
            var blobItems = _containerClient.GetBlobsAsync(prefix: path);
            var deleteTasks = new List<Task>();
            await foreach (BlobItem blobItem in blobItems)
            {
                var blobClient = _containerClient.GetBlobClient(blobItem.Name);
                deleteTasks.Add(blobClient.DeleteIfExistsAsync());
            }
            await Task.WhenAll(deleteTasks);
        }

        public async Task DeleteFilesAsync(string path)
        {
            var blobItems = _containerClient.GetBlobsAsync(prefix: path);
            await foreach (BlobItem blobItem in blobItems)
            {
                var blobClient = _containerClient.GetBlobClient(blobItem.Name);
                await blobClient.DeleteIfExistsAsync();
            }
        }

        public async Task DeleteFilesByPrefixAsync(string prefix, bool excludeLatest = false)
        {
            var blobs = _containerClient.GetBlobs(prefix: prefix).OrderByDescending(m => m.Properties.LastModified);
            foreach (var blob in blobs)
            {
                if (excludeLatest)
                {
                    excludeLatest = false;
                    continue;
                }

                var blobClient = _containerClient.GetBlobClient(blob.Name);
                await blobClient.DeleteIfExistsAsync();
            }
        }

        public async Task<BlobResult> DownloadCacheFileAsync(string path)
        {
            var latest = _containerClient.GetBlobs(prefix: path).OrderByDescending(m => m.Properties.LastModified).FirstOrDefault();
            if (latest == null) return new BlobResult { BlobStatus = BlobStatus.NotFound };
            var name = latest.Name;
            var blobClient = _containerClient.GetBlobClient(name);
            var blobExists = await blobClient.ExistsAsync();
            var properties = await blobClient.GetPropertiesAsync();

            if (blobExists)
            {
                var content = await blobClient.DownloadContentAsync();
                return new BlobResult
                {
                    Content = content.Value.Content == null ? string.Empty : content.Value.Content.ToString(),
                    Metadata = content.Value.Details.Metadata,
                    BlobStatus = BlobStatus.Found,
                    LastModified = properties.Value.LastModified
                };
            }

            return new BlobResult { BlobStatus = BlobStatus.NotFound };
        }

        public async Task<BlobMetadataResult> GetBlobMetadataAsync(string path)
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var blobExists = await blobClient.ExistsAsync();
            if (blobExists)
            {
                var properties = await blobClient.GetPropertiesAsync();
                return new BlobMetadataResult
                {
                    Metadata = properties.Value.Metadata,
                    BlobStatus = BlobStatus.Found
                };
            }

            return new BlobMetadataResult { BlobStatus = BlobStatus.NotFound };
        }

        public Task UploadFileAsync(
            string path,
            string content,
            Dictionary<string, string> metadata = null) =>
            UploadFileAsync(path, content, metadata, CancellationToken.None);

        public async Task UploadFileAsync(
            string path,
            string content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            var blobClient = _containerClient.GetBlobClient(path);

            if (metadata == null || metadata.Count == 0)
            {
                await blobClient.UploadAsync(
                    BinaryData.FromString(content),
                    overwrite: true,
                    cancellationToken);
            }
            else
            {
                var options = new BlobUploadOptions { Metadata = metadata };
                await blobClient.UploadAsync(
                    BinaryData.FromString(content),
                    options,
                    cancellationToken);
            }
        }

        public async Task<bool> UploadFileIfAbsentAsync(
            string path,
            string content,
            CancellationToken cancellationToken = default)
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var options = new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            };

            try
            {
                await blobClient.UploadAsync(
                    BinaryData.FromString(content),
                    options,
                    cancellationToken);
                return true;
            }
            catch (RequestFailedException exception)
                when (exception.Status == 409 || exception.Status == 412)
            {
                return false;
            }
        }

        public async Task UploadFileStreamAsync<T>(string path, T content, Dictionary<string, string> metadata = null, JsonSerializerOptions serializerOptions = null)
        {
            var blobClient = _containerClient.GetBlobClient(path);

            // OpenWriteAsync streams directly to blob storage, avoiding large in-memory strings
            var options = new BlobOpenWriteOptions();
            if (metadata != null && metadata.Count > 0)
            {
                options.Metadata = metadata;
            }

            await using var stream = await blobClient.OpenWriteAsync(overwrite: true, options);
            await JsonSerializer.SerializeAsync(stream, content, serializerOptions);
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
            var destClient = _containerClient.GetBlobClient(membershipFilePath);
            await using var destStream = await destClient.OpenWriteAsync(
                overwrite: true,
                cancellationToken: cancellationToken);
            await WriteGroupMembershipFromGuidsAsync(
                destStream,
                sourceMemberIds,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query,
                cancellationToken);
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
            var created = await CommitWriteOnceAsync(
                membershipFilePath,
                (stream, token) => WriteGroupMembershipFromGuidsAsync(
                    stream,
                    sourceMemberIds,
                    destination,
                    runId,
                    syncJobId,
                    exclusionary,
                    membershipObtainerDryRunEnabled,
                    query,
                    token),
                metadata: null,
                cancellationToken);
            return new WriteOnceResult(!created, sourceMemberIds.Count);
        }

        /// <summary>
        /// Writes the identifier-only GroupMembership JSON used by
        /// <see cref="UploadGroupMembershipFromGuidsAsync(string, IReadOnlyCollection{Guid}, AzureADGroup, Guid, Guid, bool, bool, string, CancellationToken)"/>.
        /// Shared with test doubles so they store the same bytes. The destination stream is left open.
        /// When <paramref name="sourceGroup"/> is set, each member also carries <c>"SourceGroup"</c> after its
        /// <c>ObjectId</c> (the source-group overload, I14); otherwise the output is the identifier-only shape (I16).
        /// </summary>
        internal static async Task WriteGroupMembershipFromGuidsAsync(
            Stream destStream,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            CancellationToken cancellationToken,
            Guid? sourceGroup = null)
        {
            await using var writer = new Utf8JsonWriter(destStream);

            writer.WriteStartObject();

            // Write the envelope before the members.
            writer.WritePropertyName("Destination");
            JsonSerializer.Serialize(writer, destination);

            writer.WriteString("RunId", runId);
            writer.WriteString("SyncJobId", syncJobId);
            writer.WriteBoolean("Exclusionary", exclusionary);
            writer.WriteBoolean("MembershipObtainerDryRunEnabled", membershipObtainerDryRunEnabled);
            if (query != null)
            {
                writer.WriteString("Query", query);
            }

            writer.WritePropertyName("SourceMembers");
            writer.WriteStartArray();

            var orderedMemberIds = sourceMemberIds.ToArray();
            Array.Sort(orderedMemberIds, CanonicalObjectIdComparer.Instance);

            foreach (var id in orderedMemberIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("ObjectId", id);
                if (sourceGroup.HasValue)
                {
                    writer.WriteString("SourceGroup", sourceGroup.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
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
            ValidateSourceGroup(sourceGroupId);
            var destClient = _containerClient.GetBlobClient(filePath);
            await using var destStream = await destClient.OpenWriteAsync(
                overwrite: true,
                cancellationToken: cancellationToken);
            await WriteGroupMembershipFromGuidsAsync(
                destStream,
                sourceMemberIds,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query,
                cancellationToken,
                sourceGroupId);
        }

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
            ValidateSourceGroup(sourceGroupId);
            var created = await CommitWriteOnceAsync(
                filePath,
                (stream, token) => WriteGroupMembershipFromGuidsAsync(
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
                metadata: null,
                cancellationToken);
            return new WriteOnceResult(!created, sourceMemberIds.Count);
        }

        /// <summary>Rejects <see cref="Guid.Empty"/> for the source-group identifier writer overloads.</summary>
        internal static void ValidateSourceGroup(Guid sourceGroupId)
        {
            if (sourceGroupId == Guid.Empty)
            {
                throw new ArgumentException(
                    "The source-group identifier writer requires a source group.",
                    nameof(sourceGroupId));
            }
        }

        public async Task UploadCacheFromGuidsAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null)
        {
            var destClient = _containerClient.GetBlockBlobClient(destinationCacheFilePath);
            var blockIds = new List<string>();
            int count = 0;
            const int batchSize = 10000; // Write in batches of 10k GUIDs (~380KB per block)

            var sb = new StringBuilder();
            bool needsNewline = false; // Track if we need to prepend newline (for block boundaries)

            foreach (var id in sourceMemberIds)
            {
                if (needsNewline || sb.Length > 0)
                    sb.Append(Environment.NewLine);
                sb.Append(id.ToString());
                count++;
                needsNewline = false;

                if (count % batchSize == 0)
                {
                    var blockId = await StageBlockAsync(destClient, sb.ToString());
                    blockIds.Add(blockId);
                    sb.Clear();
                    needsNewline = true; // Next block needs to start with newline to separate from previous block
                }
            }

            if (sb.Length > 0)
            {
                var blockId = await StageBlockAsync(destClient, sb.ToString());
                blockIds.Add(blockId);
            }

            if (blockIds.Count > 0)
            {
                var commitOptions = new CommitBlockListOptions();
                commitOptions.Metadata = commitOptions.Metadata ?? new Dictionary<string, string>();

                if (metadata != null)
                {
                    foreach (var kvp in metadata)
                    {
                        commitOptions.Metadata[kvp.Key] = kvp.Value;
                    }
                }
                await destClient.CommitBlockListAsync(blockIds, commitOptions);
            }
            else
            {
                var blobClient = _containerClient.GetBlobClient(destinationCacheFilePath);
                var options = new BlobUploadOptions();
                if (metadata != null && metadata.Count > 0)
                {
                    options.Metadata = metadata;
                }
                await blobClient.UploadAsync(BinaryData.FromString(string.Empty), options);
            }
        }

        public async Task<string> UploadFileBlockAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            var blockBlobClient = _containerClient.GetBlockBlobClient(path);
            var blockIdBytes = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString());
            var blockId = Convert.ToBase64String(blockIdBytes);
            var byteArray = Encoding.UTF8.GetBytes(content);

            using (MemoryStream stream = new MemoryStream(byteArray))
            {
                await blockBlobClient.StageBlockAsync(blockId, stream);
            }

            if (metadata != null && metadata.Count > 0)
                await blockBlobClient.SetMetadataAsync(metadata);

            return blockId;
        }

        public async Task CommitFileAsync(string path, List<string> blockIds)
        {
            var blockBlobClient = _containerClient.GetBlockBlobClient(path);
            var response = await blockBlobClient.CommitBlockListAsync(blockIds);
        }

        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(
            string path,
            Func<string, T> parseFunc) =>
            ReadValuesFromBlobAsync(path, parseFunc, CancellationToken.None);

        public async Task<HashSet<T>> ReadValuesFromBlobAsync<T>(
            string path,
            Func<string, T> parseFunc,
            CancellationToken cancellationToken)
        {
            var result = new HashSet<T>();
            var blobClient = _containerClient.GetBlobClient(path);
            var blobExists = await blobClient.ExistsAsync(cancellationToken);
            if (!blobExists)
                return result;

            var stream = await blobClient.OpenReadAsync(
                cancellationToken: cancellationToken);
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var value = parseFunc(line);
                    result.Add(value);
                }
            }
            return result;
        }

        public async Task<BlobResult> FindLatestFileAsync(string prefix)
        {
            var latest = _containerClient.GetBlobs(prefix: prefix).OrderByDescending(m => m.Properties.LastModified).FirstOrDefault();
            if (latest == null) return new BlobResult { BlobStatus = BlobStatus.NotFound };
            var name = latest.Name;
            var result = new BlobResult
            {
                Path = name,
                BlobStatus = BlobStatus.Found,
            };

            return await Task.FromResult(result);
        }

        public async Task<BlobResult> FindAggregatedFileByRunIdAsync(string groupId, string runId)
        {
            var prefix = $"{groupId}/";
            var suffix = $"_{runId}_Aggregated.json";

            await foreach (var blob in _containerClient.GetBlobsAsync(prefix: prefix))
            {
                if (blob.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return new BlobResult
                    {
                        Path = blob.Name,
                        BlobStatus = BlobStatus.Found,
                    };
                }
            }

            return new BlobResult { BlobStatus = BlobStatus.NotFound };
        }

        public async Task<Dictionary<string, BlobResult>> FindPartFilesByRunIdAsync(string groupId, string runId)
        {
            var result = new Dictionary<string, BlobResult>(StringComparer.OrdinalIgnoreCase);
            var prefix = $"{groupId}/";
            var runIdMarker = $"_{runId}_";
            const string aggregatedSuffix = "_Aggregated.json";
            const string jsonSuffix = ".json";

            await foreach (var blob in _containerClient.GetBlobsAsync(prefix: prefix))
            {
                var name = blob.Name;
                if (!name.EndsWith(jsonSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(aggregatedSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                var runIdIdx = name.IndexOf(runIdMarker, StringComparison.OrdinalIgnoreCase);
                if (runIdIdx < 0) continue;

                // Extract "{PartType}_{Index}" from between "_{runId}_" and ".json".
                // Example: "2f51.../07012026-0430_edab070d-..._GroupMembership_1.json" -> "GroupMembership_1"
                var tagStart = runIdIdx + runIdMarker.Length;
                var tagLength = name.Length - tagStart - jsonSuffix.Length;
                if (tagLength <= 0) continue;
                var tag = name.Substring(tagStart, tagLength);

                result[tag] = new BlobResult
                {
                    Path = name,
                    BlobStatus = BlobStatus.Found,
                };
            }

            return result;
        }

        public async Task<HashSet<Guid>> ExtractGroupMembershipSourceMembersAsync(string path)
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var exists = await blobClient.ExistsAsync();
            if (!exists)
                throw new FileNotFoundException(path);

            using var stream = await blobClient.OpenReadAsync(new BlobOpenReadOptions(false));
            var ids = GroupMembershipSourceMembersStreamingExtractor.Extract(stream);
            return ids;
        }

        public async IAsyncEnumerable<AzureADUser> StreamMembershipAsync(
            string path,
            Action<GroupMembership> onMembershipDetailsKnown = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var exists = await blobClient.ExistsAsync(cancellationToken);
            if (!exists)
                throw new FileNotFoundException(path);

            await using var stream = await blobClient.OpenReadAsync(new BlobOpenReadOptions(false), cancellationToken);
            await foreach (var member in MembershipStream.ReadAsync(stream, onMembershipDetailsKnown, cancellationToken))
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
            var blobClient = _blockBlobClientFactory(path);
            await using var stream = new BlockBlobStagingWriteStream(blobClient);
            await MembershipStream.WriteAsync(stream, envelope, members, cancellationToken);
            await stream.CommitAsync(metadata, cancellationToken);
        }

        public async Task<int> StreamMembershipToCacheAsync(string sourceMembershipFilePath, string destinationCacheFilePath, Dictionary<string, string> metadata = null)
        {
            var sourceClient = _containerClient.GetBlobClient(sourceMembershipFilePath);
            var exists = await sourceClient.ExistsAsync();
            if (!exists)
                throw new FileNotFoundException(sourceMembershipFilePath);

            var destClient = _containerClient.GetBlockBlobClient(destinationCacheFilePath);
            var blockIds = new List<string>();
            int count = 0;
            const int batchSize = 10000; // Write in batches of 10k GUIDs (~380KB per block)

            using var sourceStream = await sourceClient.OpenReadAsync(new BlobOpenReadOptions(false));
            var sb = new StringBuilder();
            bool needsNewline = false; // Track if we need to prepend newline (for block boundaries)

            foreach (var guid in GroupMembershipSourceMembersStreamingExtractor.EnumerateGuids(sourceStream))
            {
                if (needsNewline || sb.Length > 0)
                    sb.Append(Environment.NewLine);
                sb.Append(guid.ToString());
                count++;
                needsNewline = false;

                // Flush batch to blob storage
                if (count % batchSize == 0)
                {
                    var blockId = await StageBlockAsync(destClient, sb.ToString());
                    blockIds.Add(blockId);
                    sb.Clear();
                    needsNewline = true; // Next block needs to start with newline to separate from previous block
                }
            }

            // Write remaining content
            if (sb.Length > 0)
            {
                var blockId = await StageBlockAsync(destClient, sb.ToString());
                blockIds.Add(blockId);
            }

            // Build final metadata including NumberOfUsers
            var finalMetadata = new Dictionary<string, string>();
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    finalMetadata[kvp.Key] = kvp.Value;
                }
            }
            finalMetadata["NumberOfUsers"] = count.ToString();

            // Commit all blocks
            if (blockIds.Count > 0)
            {
                var commitOptions = new CommitBlockListOptions
                {
                    Metadata = finalMetadata
                };
                await destClient.CommitBlockListAsync(blockIds, commitOptions);
            }
            else
            {
                // Empty file case - use regular blob client for simple upload
                var blobClient = _containerClient.GetBlobClient(destinationCacheFilePath);
                var options = new BlobUploadOptions
                {
                    Metadata = finalMetadata
                };
                await blobClient.UploadAsync(BinaryData.FromString(string.Empty), options);
            }

            return count;
        }

        private static async Task<string> StageBlockAsync(BlockBlobClient blockBlobClient, string content)
        {
            var blockIdBytes = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString());
            var blockId = Convert.ToBase64String(blockIdBytes);
            var byteArray = Encoding.UTF8.GetBytes(content);

            using (var stream = new MemoryStream(byteArray))
            {
                await blockBlobClient.StageBlockAsync(blockId, stream);
            }

            return blockId;
        }

        /// <summary>
        /// Streams a k-way merge of the canonically sorted page uploads under <paramref name="sourceBlobPrefix"/>
        /// into staged blocks and publishes them with one commit (overwriting). The bytes equal
        /// <c>JsonSerializer.Serialize(groupMembership)</c> of the materializing merge. If an upload is not sorted
        /// (an older release wrote it), nothing is published from the streaming attempt and the materializing merge
        /// runs instead, with a warning.
        /// </summary>
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
            var envelope = MergeEnvelope(destination, runId, syncJobId, exclusionary, membershipObtainerDryRunEnabled, query);
            var inputs = await ListUserUploadInputsAsync(sourceBlobPrefix, CancellationToken.None);
            try
            {
                var blobClient = _blockBlobClientFactory(destinationPath);
                await using var stream = new BlockBlobStagingWriteStream(blobClient);
                var count = await UserUploadMerge.WriteMergedAsync(stream, envelope, inputs, CancellationToken.None);
                try
                {
                    await stream.CommitAsync(metadata: null, CancellationToken.None);
                }
                catch (RequestFailedException exception) when (string.Equals(
                    exception.ErrorCode,
                    BlobErrorCode.InvalidBlockList.ToString(),
                    StringComparison.Ordinal))
                {
                    // A concurrent duplicate of this merge committed first, which discards this merge's uncommitted
                    // blocks. When the blob exists the merge is done: report the existing blob's count, as the
                    // write-once merge does. Otherwise the failure is real.
                    var exists = await blobClient.ExistsAsync(CancellationToken.None);
                    if (exists?.Value != true)
                    {
                        throw;
                    }

                    var existingMembers = await ExtractGroupMembershipSourceMembersAsync(destinationPath);
                    return existingMembers.Count;
                }

                return count;
            }
            catch (UnsortedUserUploadException exception)
            {
                UserUploadMerge.LogUnsortedFallback(_logger, sourceBlobPrefix, exception);
                return await MergeMaterializedAsync(
                    sourceBlobPrefix, destinationPath, destination, runId, syncJobId, exclusionary, membershipObtainerDryRunEnabled, query);
            }
        }

        /// <summary>
        /// Write-once form of <see cref="MergeAndStreamUserBlobsAsync"/>: the same streaming merge, published with
        /// one If-None-Match: * commit, with the same unsorted-input fallback.
        /// </summary>
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
            var envelope = MergeEnvelope(destination, runId, syncJobId, exclusionary, membershipObtainerDryRunEnabled, query);
            var inputs = await ListUserUploadInputsAsync(sourceBlobPrefix, CancellationToken.None);
            var count = 0;
            bool created;
            try
            {
                created = await CommitWriteOnceAsync(
                    destinationPath,
                    async (stream, token) =>
                    {
                        count = await UserUploadMerge.WriteMergedAsync(stream, envelope, inputs, token);
                        return (IDictionary<string, string>)null;
                    },
                    CancellationToken.None);
            }
            catch (UnsortedUserUploadException exception)
            {
                UserUploadMerge.LogUnsortedFallback(_logger, sourceBlobPrefix, exception);
                return await MergeMaterializedWriteOnceAsync(
                    sourceBlobPrefix, destinationPath, destination, runId, syncJobId, exclusionary, membershipObtainerDryRunEnabled, query);
            }

            if (created)
            {
                return new WriteOnceResult(false, count);
            }

            // An earlier execution already published this blob. Report the existing blob's count, never the
            // count of the current listing, whose page uploads may already have been cleaned up.
            var existingMembers = await ExtractGroupMembershipSourceMembersAsync(destinationPath);
            return new WriteOnceResult(true, existingMembers.Count);
        }

        /// <summary>The envelope of a merged membership; the writer supplies SourceMembers.</summary>
        internal static GroupMembership MergeEnvelope(
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query) =>
            new GroupMembership
            {
                SourceMembers = new List<AzureADUser>(),
                Destination = destination,
                RunId = runId,
                SyncJobId = syncJobId,
                Exclusionary = exclusionary,
                MembershipObtainerDryRunEnabled = membershipObtainerDryRunEnabled,
                Query = query
            };

        /// <summary>Lists the page uploads in listing order; each opens with a buffer sized to the input count.</summary>
        private async Task<List<UserUploadInput>> ListUserUploadInputsAsync(string sourceBlobPrefix, CancellationToken cancellationToken)
        {
            var names = new List<string>();
            await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: sourceBlobPrefix, cancellationToken: cancellationToken))
            {
                names.Add(blobItem.Name);
            }

            var bufferSize = UserUploadMerge.ReadBufferSize(names.Count);
            var inputs = new List<UserUploadInput>(names.Count);
            foreach (var name in names)
            {
                inputs.Add(new UserUploadInput(
                    name,
                    token => _containerClient.GetBlobClient(name).OpenReadAsync(0, bufferSize, null, token)));
            }

            return inputs;
        }

        /// <summary>The materializing merge, used when a page upload is not canonically sorted.</summary>
        private async Task<int> MergeMaterializedAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            var groupMembership = await ReadMergedMembershipAsync(
                sourceBlobPrefix,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query);

            await UploadFileAsync(destinationPath, JsonSerializer.Serialize(groupMembership));

            return groupMembership.SourceMembers.Count;
        }

        /// <summary>The materializing write-once merge, used when a page upload is not canonically sorted.</summary>
        private async Task<WriteOnceResult> MergeMaterializedWriteOnceAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            var groupMembership = await ReadMergedMembershipAsync(
                sourceBlobPrefix,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query);

            var created = await UploadTextWriteOnceAsync(
                destinationPath,
                JsonSerializer.Serialize(groupMembership),
                metadata: null,
                CancellationToken.None);
            if (created)
            {
                return new WriteOnceResult(false, groupMembership.SourceMembers.Count);
            }

            // An earlier execution already published this blob. Report the existing blob's count, never the
            // count of the current listing, whose page uploads may already have been cleaned up.
            var existingMembers = await ExtractGroupMembershipSourceMembersAsync(destinationPath);
            return new WriteOnceResult(true, existingMembers.Count);
        }

        public async Task<WriteOnceResult> UploadFileWriteOnceAsync(
            string path,
            string content,
            Dictionary<string, string> metadata = null,
            CancellationToken cancellationToken = default)
        {
            var created = await UploadTextWriteOnceAsync(path, content, metadata, cancellationToken);
            return new WriteOnceResult(!created);
        }

        public async Task<WriteOnceResult> UploadCacheFromGuidsWriteOnceAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null)
        {
            var count = 0;
            var created = await CommitWriteOnceAsync(
                destinationCacheFilePath,
                async (stream, token) =>
                {
                    count = await WriteGuidLinesAsync(stream, sourceMemberIds, token);
                    return metadata;
                },
                CancellationToken.None);
            return new WriteOnceResult(!created, count);
        }

        public async Task<WriteOnceResult> StreamMembershipToCacheWriteOnceAsync(
            string sourceMembershipFilePath,
            string destinationCacheFilePath,
            Dictionary<string, string> metadata = null)
        {
            var sourceClient = _containerClient.GetBlobClient(sourceMembershipFilePath);
            var exists = await sourceClient.ExistsAsync();
            if (!exists)
                throw new FileNotFoundException(sourceMembershipFilePath);

            var count = 0;
            var created = await CommitWriteOnceAsync(
                destinationCacheFilePath,
                async (stream, token) =>
                {
                    using var sourceStream = await sourceClient.OpenReadAsync(
                        new BlobOpenReadOptions(false),
                        token);
                    count = await WriteGuidLinesAsync(
                        stream,
                        GroupMembershipSourceMembersStreamingExtractor.EnumerateGuids(sourceStream),
                        token);

                    // Same metadata as StreamMembershipToCacheAsync, including NumberOfUsers.
                    var finalMetadata = new Dictionary<string, string>();
                    if (metadata != null)
                    {
                        foreach (var kvp in metadata)
                        {
                            finalMetadata[kvp.Key] = kvp.Value;
                        }
                    }
                    finalMetadata["NumberOfUsers"] = count.ToString();
                    return (IDictionary<string, string>)finalMetadata;
                },
                CancellationToken.None);
            return new WriteOnceResult(!created, count);
        }

        public async Task<WriteOnceResult> UploadGroupMembershipStreamWriteOnceAsync(
            string path,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            Action beforeCommit,
            CancellationToken stagingCancellationToken,
            CancellationToken commitCancellationToken)
        {
            var blobClient = _blockBlobClientFactory(path);
            int count;
            try
            {
                await using var stream = new BlockBlobStagingWriteStream(blobClient);
                count = await WriteGroupMembershipStreamAsync(stream, envelope, members, stagingCancellationToken);
                beforeCommit?.Invoke();
                await stream.CommitAsync(
                    metadata: null,
                    new BlobRequestConditions { IfNoneMatch = ETag.All },
                    commitCancellationToken);
            }
            catch (RequestFailedException exception) when (IsWriteOnceRejection(exception))
            {
                var exists = await blobClient.ExistsAsync(commitCancellationToken);
                if (exists?.Value == true)
                {
                    var existing = await ExtractGroupMembershipSourceMembersAsync(path);
                    return new WriteOnceResult(true, existing.Count);
                }

                throw;
            }

            return new WriteOnceResult(false, count);
        }

        public async Task<BlobStreamWithETag> OpenReadWithETagAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var blobClient = _containerClient.GetBlobClient(path);
            try
            {
                var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
                var details = response.Value.Details;
                return new BlobStreamWithETag(
                    response.Value.Content,
                    details.ETag.ToString(),
                    details.LastModified);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
        }

        public async Task<bool> UploadFileConditionalStreamAsync(
            string path,
            Func<Stream, CancellationToken, Task> writeContent,
            string ifMatchETag,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(writeContent);
            var conditions = string.IsNullOrEmpty(ifMatchETag)
                ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                : new BlobRequestConditions { IfMatch = new ETag(ifMatchETag) };
            var blobClient = _blockBlobClientFactory(path);
            try
            {
                await using var stream = new BlockBlobStagingWriteStream(blobClient);
                await writeContent(stream, cancellationToken);
                await stream.CommitAsync(metadata: null, conditions, cancellationToken);
                return true;
            }
            catch (RequestFailedException exception) when (IsWriteOnceRejection(exception))
            {
                return false;
            }
        }

        public async Task<int> DeleteListedBlobsAsync(
            IReadOnlyCollection<string> exactPaths,
            IReadOnlyCollection<string> prefixes,
            CancellationToken cancellationToken = default)
        {
            var deleted = 0;
            foreach (var exactPath in exactPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(exactPath))
                {
                    continue;
                }

                await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: exactPath, cancellationToken: cancellationToken))
                {
                    if (string.Equals(blobItem.Name, exactPath, StringComparison.Ordinal))
                    {
                        await _containerClient.GetBlobClient(blobItem.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
                        deleted++;
                    }
                }
            }

            foreach (var prefix in prefixes ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(prefix))
                {
                    continue;
                }

                await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
                {
                    await _containerClient.GetBlobClient(blobItem.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
                    deleted++;
                }
            }

            return deleted;
        }

        /// <summary>
        /// Streams <paramref name="members"/> into a plain-JSON GroupMembership document whose bytes equal
        /// <c>JsonSerializer.Serialize(envelope)</c> with those members as <c>SourceMembers</c>. The envelope's
        /// own <c>SourceMembers</c> are ignored. Shared with test doubles. Returns the number of members written.
        /// </summary>
        internal static async Task<int> WriteGroupMembershipStreamAsync(
            Stream destStream,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(members);

            var originalMembers = envelope.SourceMembers;
            byte[] envelopeBytes;
            try
            {
                envelope.SourceMembers = new List<AzureADUser>();
                envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            }
            finally
            {
                envelope.SourceMembers = originalMembers;
            }

            // SourceMembers is ordered last, so the empty document ends with "SourceMembers":[]}.
            var emptyTail = Encoding.UTF8.GetBytes("\"SourceMembers\":[]}");
            if (!envelopeBytes.AsSpan().EndsWith(emptyTail))
            {
                throw new InvalidOperationException("The membership envelope does not end with its SourceMembers array.");
            }

            await destStream.WriteAsync(envelopeBytes.AsMemory(0, envelopeBytes.Length - 2), cancellationToken);

            // One reusable writer over one reusable buffer: each member is serialized exactly as
            // JsonSerializer.SerializeToUtf8Bytes(member) would, and the buffer is drained to the stream in batches.
            const int drainThreshold = 64 * 1024;
            var buffer = new ArrayBufferWriter<byte>(drainThreshold + 4096);
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });
            var count = 0;
            await foreach (var member in members.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (count > 0)
                {
                    buffer.GetSpan(1)[0] = (byte)',';
                    buffer.Advance(1);
                }

                writer.Reset(buffer);
                JsonSerializer.Serialize(writer, member);
                writer.Flush();
                count++;

                if (buffer.WrittenCount >= drainThreshold)
                {
                    await destStream.WriteAsync(buffer.WrittenMemory, cancellationToken);
                    buffer.ResetWrittenCount();
                }
            }

            var tail = buffer.GetSpan(2);
            tail[0] = (byte)']';
            tail[1] = (byte)'}';
            buffer.Advance(2);
            await destStream.WriteAsync(buffer.WrittenMemory, cancellationToken);
            return count;
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
            var seenIds = new HashSet<Guid>();
            var uniqueUsers = new List<AzureADUser>();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var blobs = _containerClient.GetBlobsAsync(prefix: sourceBlobPrefix);
            await foreach (var blobItem in blobs)
            {
                await using var sourceStream = await _containerClient.GetBlobClient(blobItem.Name).OpenReadAsync();
                var users = await JsonSerializer.DeserializeAsync<List<AzureADUser>>(sourceStream, options);
                if (users == null)
                {
                    throw new Exception($"Failed to deserialize blob: {blobItem.Name}");
                }

                foreach (var user in users)
                {
                    if (user == null)
                    {
                        continue;
                    }

                    if (seenIds.Add(user.ObjectId))
                    {
                        uniqueUsers.Add(user);
                    }
                }
            }

            uniqueUsers.Sort(CanonicalMemberComparer<AzureADUser>.Instance);

            return new GroupMembership
            {
                SourceMembers = uniqueUsers,
                Destination = destination,
                RunId = runId,
                SyncJobId = syncJobId,
                Exclusionary = exclusionary,
                MembershipObtainerDryRunEnabled = membershipObtainerDryRunEnabled,
                Query = query
            };
        }

        private Task<bool> UploadTextWriteOnceAsync(
            string path,
            string content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken) =>
            CommitWriteOnceAsync(
                path,
                async (stream, token) =>
                {
                    // BinaryData.FromString, used by UploadFileAsync, encodes as UTF-8 without a BOM.
                    var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
                    await stream.WriteAsync(bytes, token);
                    return (IDictionary<string, string>)metadata;
                },
                cancellationToken);

        private Task<bool> CommitWriteOnceAsync(
            string path,
            Func<Stream, CancellationToken, Task> writeContent,
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken) =>
            CommitWriteOnceAsync(
                path,
                async (stream, token) =>
                {
                    await writeContent(stream, token);
                    return metadata;
                },
                cancellationToken);

        /// <summary>
        /// Stages the content as uncommitted blocks and publishes it with one Put Block List conditioned on
        /// If-None-Match: *, so a crash mid-write never leaves a partial blob at <paramref name="path"/>.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when this call created the blob; <see langword="false"/> when the commit was
        /// rejected (409, 412, or InvalidBlockList from a concurrent commit) and the blob is confirmed to exist.
        /// </returns>
        private async Task<bool> CommitWriteOnceAsync(
            string path,
            Func<Stream, CancellationToken, Task<IDictionary<string, string>>> writeContent,
            CancellationToken cancellationToken)
        {
            var blobClient = _blockBlobClientFactory(path);
            try
            {
                await using var stream = new BlockBlobStagingWriteStream(blobClient);
                var metadata = await writeContent(stream, cancellationToken);
                await stream.CommitAsync(
                    metadata,
                    new BlobRequestConditions { IfNoneMatch = ETag.All },
                    cancellationToken);
                return true;
            }
            catch (RequestFailedException exception) when (IsWriteOnceRejection(exception))
            {
                var exists = await blobClient.ExistsAsync(cancellationToken);
                if (exists?.Value == true)
                {
                    return false;
                }

                throw;
            }
        }

        private static bool IsWriteOnceRejection(RequestFailedException exception) =>
            exception.Status == 409 ||
            exception.Status == 412 ||
            string.Equals(
                exception.ErrorCode,
                BlobErrorCode.InvalidBlockList.ToString(),
                StringComparison.Ordinal);

        /// <summary>
        /// Writes one identifier per line separated by <see cref="Environment.NewLine"/>, with no trailing
        /// separator: the same bytes as <see cref="UploadCacheFromGuidsAsync"/>.
        /// </summary>
        private static async Task<int> WriteGuidLinesAsync(
            Stream stream,
            IEnumerable<Guid> ids,
            CancellationToken cancellationToken)
        {
            const int batchSize = 10000;
            var count = 0;
            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                if (count > 0)
                    sb.Append(Environment.NewLine);
                sb.Append(id.ToString());
                count++;

                if (count % batchSize == 0)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()), cancellationToken);
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()), cancellationToken);
            }

            return count;
        }
    }
}
