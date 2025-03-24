// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Storage.Blobs;
using System;

namespace Services.Contracts
{
    public interface IBlobClientFactory
    {
        /// <summary>
        /// Creates a BlobClient using a Uri and DefaultAzureCredential.
        /// </summary>
        /// <param name="blobUri">
        ///    A System.Uri referencing the blob that includes the name of the account, the
        ///     name of the container, and the name of the blob. This is likely to be similar
        ///     to "https://{account_name}.blob.core.windows.net/{container_name}/{blob_name}".
        /// </param>
        /// <returns></returns>
        public BlobClient GetBlobClient(Uri blobUri);
    }
}