// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class LoadTestingSyncJobCreatorRequest
    {
        public Dictionary<int,List<Guid>> GroupSizesAndIds { get; set; }
        public List<SyncJob> SyncJobs { get; set; }
        public Guid RunId { get; set; }
    }
}