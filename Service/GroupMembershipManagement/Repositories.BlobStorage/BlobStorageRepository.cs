// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    public class BlobStorageRepository : IBlobStorageRepository
    {
        private BlobContainerClient _containerClient;

        public BlobStorageRepository(string containerUrl)
        {
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

            _containerClient = new BlobContainerClient(new Uri(containerUrl), credential);
            _containerClient.CreateIfNotExists();
        }

        public async Task DeleteFileAsync(string path)
        {
            await _containerClient.DeleteBlobIfExistsAsync(path, DeleteSnapshotsOption.IncludeSnapshots);
        }

        public async Task<BlobResult> DownloadFileAsync(string path)
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var blobExists = await blobClient.ExistsAsync();
            if (blobExists)
            {
                var content = await blobClient.DownloadContentAsync();
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

        public async Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            var blobClient = _containerClient.GetBlobClient(path);

            if (metadata == null || metadata.Count == 0)
            {
                await blobClient.UploadAsync(BinaryData.FromString(content), overwrite: true);
            }
            else
            {
                var options = new BlobUploadOptions { Metadata = metadata };
                await blobClient.UploadAsync(BinaryData.FromString(content), options);
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

        public async Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IEnumerable<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            var destClient = _containerClient.GetBlobClient(membershipFilePath);
            await using var destStream = await destClient.OpenWriteAsync(overwrite: true);
            await using var writer = new Utf8JsonWriter(destStream);

            writer.WriteStartObject();

            writer.WritePropertyName("SourceMembers");
            writer.WriteStartArray();

            foreach (var id in sourceMemberIds)
            {
                writer.WriteStartObject();
                writer.WriteString("ObjectId", id);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

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

            writer.WriteEndObject();
            await writer.FlushAsync();
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

        public async Task<HashSet<T>> ReadValuesFromBlobAsync<T>(string path, Func<string, T> parseFunc)
        {
            var result = new HashSet<T>();
            var blobClient = _containerClient.GetBlobClient(path);
            var blobExists = await blobClient.ExistsAsync();
            if (!blobExists)
                return result;

            var stream = await blobClient.OpenReadAsync();
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
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
            var seenIds = new HashSet<Guid>();
            var uniqueUsers = new List<AzureADUser>();
            int count = 0;

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
                        count++;
                    }
                }
            }

            var groupMembership = new GroupMembership
            {
                SourceMembers = uniqueUsers,
                Destination = destination,
                RunId = runId,
                SyncJobId = syncJobId,
                Exclusionary = exclusionary,
                MembershipObtainerDryRunEnabled = membershipObtainerDryRunEnabled,
                Query = query
            };

            await UploadFileAsync(destinationPath, JsonSerializer.Serialize(groupMembership));

            return count;
        }
    }
}
