// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
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
                result.Content = new BinaryData(Files[path]);
            }
            else
            {
                result.BlobStatus = BlobStatus.NotFound;
            }

            return Task.FromResult(result);
        }

        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            throw new NotImplementedException();
        }
    }
}
