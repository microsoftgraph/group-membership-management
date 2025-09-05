// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Services.Contracts;
using System;

namespace Services
{
    public class BlobClientFactory : IBlobClientFactory
    {

        /// <summary>
        /// Creates a BlobClient using a Uri and TokenCredential.
        /// </summary>
        /// <param name="blobUri">
        ///    A System.Uri referencing the blob that includes the name of the account, the
        ///     name of the container, and the name of the blob. This is likely to be similar
        ///     to "https://{account_name}.blob.core.windows.net/{container_name}/{blob_name}".
        /// </param>
        /// <returns></returns>
        public BlobClient GetBlobClient(Uri blobUri)
        {
            TokenCredential credential;
#if DEBUG
            credential = new DefaultAzureCredential();
#else
            credential = new ManagedIdentityCredential();
#endif
            return new BlobClient(blobUri, credential);
        }
    }
}