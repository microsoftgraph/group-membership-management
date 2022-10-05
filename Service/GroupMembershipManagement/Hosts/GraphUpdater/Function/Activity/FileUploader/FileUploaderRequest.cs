// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class FileUploaderRequest
    {
        public Guid RunId { get; set; }
        public SyncJob SyncJob { get; set; }
        public Guid ObjectId { get; set; }
        public List<AzureADUser> Users { get; set; }
    }
}