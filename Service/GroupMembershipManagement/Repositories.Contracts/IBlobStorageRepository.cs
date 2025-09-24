// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;
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
        public Task DeleteBlobsAsync(string path);
        public Task<BlobResult> FindLatestFileAsync(string prefix);
        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(string path, System.Func<string, T> parseFunction);

        /// <summary>
        /// Extract distinct SourceMembers[].ObjectId GUIDs from a GroupMembership JSON blob at the given path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public Task<HashSet<Guid>> ExtractGroupMembershipSourceMembersAsync(string path);
    }
}
