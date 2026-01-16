// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
    public class MockBlobStorageRepository : IBlobStorageRepository
    {
        public List<(string Path, string Content)> Sent { get; set; } = new List<(string Path, string Content)>();

        public List<BlobItem> Blobs { get; set; } = new List<BlobItem>();

        public MockBlobContainerClient ContainerClient { get; set; }

        public MockBlobStorageRepository()
        {
            for (int i = 0; i < 10; i++)
            {
                var prefix = i % 2 == 0 ? "even" : "odd";
                var blobProperties = BlobsModelFactory.BlobItemProperties(false, lastModified: DateTimeOffset.UtcNow.AddDays(-i));
                Blobs.Add(BlobsModelFactory.BlobItem(name: $"{prefix}_{i}.json", properties: blobProperties));
            }

            ContainerClient = new MockBlobContainerClient(Blobs);
        }

        public Task DeleteFileAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<BlobResult> DownloadFileAsync(string path)
        {
            throw new NotImplementedException();
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
            Sent.Add((path, content));
            return Task.CompletedTask;
        }

        public Task UploadFileStreamAsync<T>(string path, T content, Dictionary<string, string> metadata = null, JsonSerializerOptions serializerOptions = null)
        {
            return Task.CompletedTask;
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
            return Task.CompletedTask;
        }

        public Task UploadCacheFromGuidsAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null)
        {
            return Task.CompletedTask;
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
            // For testing, just return 0 - tests can override this behavior if needed
            return Task.FromResult(0);
        }

        public Task<string> UploadFileBlockAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            throw new NotImplementedException();
        }

        public Task CommitFileAsync(string path, List<string> blockIds)
        {
            throw new NotImplementedException();
        }

        public async Task DeleteFilesByPrefixAsync(string prefix, bool excludeLatest = false)
        {
            var blobs = ContainerClient.GetBlobs(prefix: prefix).OrderByDescending(m => m.Properties.LastModified);
            foreach (var blob in blobs)
            {
                if (excludeLatest)
                {
                    excludeLatest = false;
                    continue;
                }

                var blobClient = ContainerClient.GetBlobClient(blob.Name);
                await blobClient.DeleteIfExistsAsync();
            }
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

    public class MockBlobContainerClient : BlobContainerClient
    {
        private List<BlobItem> _blobs;

        public MockBlobContainerClient(List<BlobItem> blobs)
        {
            _blobs = blobs;
        }

        public override Pageable<BlobItem> GetBlobs(BlobTraits traits = BlobTraits.None, BlobStates states = BlobStates.None, string prefix = null, CancellationToken cancellationToken = default)
        {
            var filteredBlobs = prefix != null
                                ? _blobs.Where(x => x.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)).ToList()
                                : _blobs;

            var page = Azure.Page<BlobItem>.FromValues(filteredBlobs, null, null);
            return Pageable<BlobItem>.FromPages([page]);
        }

        public override BlobClient GetBlobClient(string blobName)
        {
            return new MockBlobClient(_blobs, blobName);
        }
    }

    public class MockBlobClient : BlobClient
    {
        private List<BlobItem> _blobs;
        private string _blobName;

        public MockBlobClient(List<BlobItem> blobs, string blobName)
        {
            _blobs = blobs;
            _blobName = blobName;
        }

        public override Task<Response<bool>> DeleteIfExistsAsync(DeleteSnapshotsOption snapshotsOption = DeleteSnapshotsOption.None, BlobRequestConditions conditions = null, CancellationToken cancellationToken = default)
        {
            var blob = _blobs.Where(x => x.Name == _blobName).FirstOrDefault();
            if (blob != null)
            {
                _blobs.Remove(blob);
                return Task.FromResult(Response.FromValue(true, null));
            }

            return Task.FromResult(Response.FromValue(false, null));
        }
    }
}
