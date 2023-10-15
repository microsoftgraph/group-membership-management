// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class LoadTestingSyncJobRetrieverResponse
    {
        public List<SyncJob> SyncJobs{ get; set; }
    }
}