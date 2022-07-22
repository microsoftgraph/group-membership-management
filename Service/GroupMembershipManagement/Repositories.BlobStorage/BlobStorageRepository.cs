// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    public class BlobStorageRepository : IBlobStorageRepository
    {
        private BlobContainerClient _containerClient;

        public BlobStorageRepository(string containerUrl)
        {
            _containerClient = new BlobContainerClient(new Uri(containerUrl), new DefaultAzureCredential());
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
                    Content = content.Value.Content,
                    Metadata = content.Value.Details.Metadata,
                    BlobStatus = BlobStatus.Found
                };
            }

            return new BlobResult { BlobStatus = BlobStatus.NotFound };
        }

        public async Task<BlobResult> DownloadCacheFileAsync(string path)
        {
            var latest = _containerClient.GetBlobs(prefix: path).OrderByDescending(m => m.Properties.LastModified).FirstOrDefault();
            if (latest == null ) return new BlobResult { BlobStatus = BlobStatus.NotFound };
            var name = latest.Name;
            var blobClient = _containerClient.GetBlobClient(name);
            var blobExists = await blobClient.ExistsAsync();
            if (blobExists)
            {
                var content = await blobClient.DownloadContentAsync();
                return new BlobResult
                {
                    Content = content.Value.Content,
                    Metadata = content.Value.Details.Metadata,
                    BlobStatus = BlobStatus.Found
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
            await blobClient.UploadAsync(BinaryData.FromString(content), overwrite: true);

            if (metadata != null && metadata.Count > 0)
                blobClient.SetMetadata(metadata);
        }
    }
}
