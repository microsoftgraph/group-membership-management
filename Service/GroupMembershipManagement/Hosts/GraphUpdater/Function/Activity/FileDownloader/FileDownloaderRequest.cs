// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;

namespace Hosts.GraphUpdater
{
    public class FileDownloaderRequest
    {
        public string FilePath { get; set; }
        public Guid RunId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}
