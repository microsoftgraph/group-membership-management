// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.AzureUserReader
{
    public class AzureUserReaderRequest
    {
        public string ContainerName { get; set; }
        public string BlobPath { get; set; }
    }
}