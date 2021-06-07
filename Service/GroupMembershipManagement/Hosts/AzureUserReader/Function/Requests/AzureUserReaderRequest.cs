// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace AzureUserReader.Requests
{
    public class AzureUserReaderRequest
    {
        public string ContainerName { get; set; }
        public string BlobPath { get; set; }
    }
}