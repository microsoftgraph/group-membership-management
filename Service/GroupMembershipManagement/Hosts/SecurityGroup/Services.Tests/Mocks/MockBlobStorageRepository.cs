// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
    public class MockBlobStorageRepository : IBlobStorageRepository
    {
        public List<(string Path, string Content)> Sent { get; set; } = new List<(string Path, string Content)>();
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

        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            Sent.Add((path, content));
            return Task.CompletedTask;
        }
    }
}
