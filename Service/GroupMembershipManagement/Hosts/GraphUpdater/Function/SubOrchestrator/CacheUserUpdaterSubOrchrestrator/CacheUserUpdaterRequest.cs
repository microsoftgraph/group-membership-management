// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class CacheUserUpdaterRequest
    {
        public Guid GroupId { get; set; }
        public List<AzureADUser> UserIds { get; set; }
        public Guid? RunId { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}