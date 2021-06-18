// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Storage.Blobs;

namespace Services.Contracts
{
    public interface IBlobClientFactory
    {
        public BlobClient GetBlobClient(string connectionString, string blobContainerName, string blobName);
    }
}