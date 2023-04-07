// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Hosts.GraphUpdater
{
    public class FileDownloaderRequest
    {
        public string FilePath { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
