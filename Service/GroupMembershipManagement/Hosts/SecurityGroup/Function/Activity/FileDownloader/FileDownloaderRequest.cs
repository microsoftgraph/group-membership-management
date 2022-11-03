// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.SecurityGroup
{
    public class FileDownloaderRequest
    {
        public string FilePath { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
