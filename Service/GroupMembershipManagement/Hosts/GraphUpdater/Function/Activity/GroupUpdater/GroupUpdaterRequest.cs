// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterRequest
    {
        public RequestType Type { get; set; }
        public ICollection<AzureADUser> Members { get; set; }
        public bool IsInitialSync { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}