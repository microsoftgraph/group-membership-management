// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Storage.Blobs;
using Services.Contracts;

namespace Services
{
    public class BlobClientFactory : IBlobClientFactory
    {
        public BlobClient GetBlobClient(string connectionString, string blobContainerName, string blobName)
        {
            return new BlobClient(connectionString, blobContainerName, blobName);
        }
    }
}