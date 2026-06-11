// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IBlobStorageRepository
    {
        public Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null);
        Task<string> UploadFileBlockAsync(string path, string content, Dictionary<string, string> metadata = null);
        public Task DeleteFileAsync(string path);
        public Task DeleteFilesByPrefixAsync(string prefix, bool excludeLatest = false);
        public Task<BlobResult> DownloadFileAsync(string path);
        public Task<BlobResult> DownloadCacheFileAsync(string path);
        public Task DeleteFilesAsync(string path);
        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path);
        public Task CommitFileAsync(string path, List<string> blockIds);
        public Task<List<AzureADUser>> ReadBlobsAsync(string path);
        public IAsyncEnumerable<AzureADUser> StreamUsersFromBlobsAsync(string path);
        public Task DeleteBlobsAsync(string path);
        public Task<BlobResult> FindLatestFileAsync(string prefix);
        public Task<BlobResult> FindAggregatedFileByRunIdAsync(string groupId, string runId);
        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(string path, System.Func<string, T> parseFunction);

        /// <summary>
        /// Extract distinct SourceMembers[].ObjectId GUIDs from a GroupMembership JSON blob at the given path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public Task<HashSet<Guid>> ExtractGroupMembershipSourceMembersAsync(string path);

        /// <summary>
        /// Stream GUIDs from a GroupMembership JSON blob and write them as a cache file (one GUID per line).
        /// This avoids loading all GUIDs into memory at once.
        /// </summary>
        /// <param name="sourceMembershipFilePath">Path to the source GroupMembership JSON blob.</param>
        /// <param name="destinationCacheFilePath">Path where the cache file should be written.</param>
        /// <param name="metadata">Optional metadata to attach to the cache blob.</param>
        /// <returns>The count of GUIDs written to the cache.</returns>
        public Task<int> StreamMembershipToCacheAsync(string sourceMembershipFilePath, string destinationCacheFilePath, Dictionary<string, string> metadata = null);

        /// <summary>
        /// Serialize an object directly to blob storage as JSON without creating intermediate string.
        /// This reduces memory usage for large objects by streaming the serialization.
        /// </summary>
        /// <typeparam name="T">Type of the object to serialize.</typeparam>
        /// <param name="path">Blob path.</param>
        /// <param name="content">Object to serialize.</param>
        /// <param name="metadata">Optional metadata to attach to the blob.</param>
        public Task UploadFileStreamAsync<T>(string path, T content, Dictionary<string, string> metadata = null, JsonSerializerOptions serializerOptions = null);

        public Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IEnumerable<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query);

        public Task UploadCacheFromGuidsAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null);

        /// <summary>
        /// Merge multiple user array blobs into a single GroupMembership JSON blob with streaming.
        /// This method:
        /// 1. Reads source blobs one at a time using streaming JSON parser
        /// 2. Deduplicates users by ObjectId using HashSet&lt;Guid&gt; (16 bytes per user)
        /// 3. Writes each unique user directly to output stream
        /// Peak memory: O(unique_user_count * 16 bytes) instead of O(user_count * user_object_size)
        /// </summary>
        /// <param name="sourceBlobPrefix">Prefix to find source user array blobs.</param>
        /// <param name="destinationPath">Path for output GroupMembership JSON blob.</param>
        /// <param name="destination">The destination group.</param>
        /// <param name="runId">The run ID.</param>
        /// <param name="syncJobId">The sync job ID.</param>
        /// <param name="exclusionary">Whether this is an exclusionary sync.</param>
        /// <param name="membershipObtainerDryRunEnabled">Dry run flag.</param>
        /// <param name="query">The sync job query.</param>
        /// <returns>The count of unique users written.</returns>
        public Task<int> MergeAndStreamUserBlobsAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query);
    }
}
