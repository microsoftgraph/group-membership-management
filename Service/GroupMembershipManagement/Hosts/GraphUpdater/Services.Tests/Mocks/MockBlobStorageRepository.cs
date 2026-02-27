// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services.Tests.Mocks
{
    public class MockBlobStorageRepository : IBlobStorageRepository
    {
        public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();

        public Task DeleteFileAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<BlobResult> DownloadFileAsync(string path)
        {
            var result = new BlobResult();

            if (Files.ContainsKey(path))
            {
                result.BlobStatus = BlobStatus.Found;
                result.Content = Files[path];
            }
            else
            {
                result.BlobStatus = BlobStatus.NotFound;
            }

            return Task.FromResult(result);
        }

        public Task<BlobResult> DownloadCacheFileAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task DeleteFilesAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            throw new NotImplementedException();
        }

        public Task UploadFileStreamAsync<T>(string path, T content, Dictionary<string, string> metadata = null, JsonSerializerOptions serializerOptions = null)
        {
            throw new NotImplementedException();
        }

        public Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IEnumerable<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            throw new NotImplementedException();
        }

        public Task UploadCacheFromGuidsAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null)
        {
            throw new NotImplementedException();
        }

        public Task<int> MergeAndStreamUserBlobsAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            throw new NotImplementedException();
        }

        public Task<string> UploadFileBlockAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            throw new NotImplementedException();
        }

        public Task CommitFileAsync(string path, List<string> blockIds)
        {
            throw new NotImplementedException();
        }

        public Task DeleteFilesByPrefixAsync(string prefix, bool excludeLatest = false)
        {
            throw new NotImplementedException();
        }

        public Task<List<AzureADUser>> ReadBlobsAsync(string path) => Task.FromResult(new List<AzureADUser>());

        public async IAsyncEnumerable<AzureADUser> StreamUsersFromBlobsAsync(string path)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task DeleteBlobsAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<BlobResult> FindLatestFileAsync(string prefix)
        {
            throw new NotImplementedException();
        }

        public Task<BlobResult> FindAggregatedFileByRunIdAsync(string groupId, string runId)
        {
            throw new NotImplementedException();
        }

        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(string path, Func<string, T> parseFunction)
        {
            throw new NotImplementedException();
        }

        public Task<HashSet<Guid>> ExtractGroupMembershipSourceMembersAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<int> StreamMembershipToCacheAsync(string sourceMembershipFilePath, string destinationCacheFilePath, Dictionary<string, string> metadata = null)
        {
            // For testing, just return 0 - tests can override this behavior if needed
            return Task.FromResult(0);
        }
    }
}
