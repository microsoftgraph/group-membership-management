// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IBlobStorageRepository
    {
        public Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null);
        public Task DeleteFileAsync(string path);
        public Task<BlobResult> DownloadFileAsync(string path);
        public Task<BlobResult> DownloadCacheFileAsync(string path);
        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path);
    }
}
