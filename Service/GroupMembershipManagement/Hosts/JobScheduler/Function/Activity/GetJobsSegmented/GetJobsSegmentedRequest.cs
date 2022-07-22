// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Entities;
using Microsoft.Azure.Cosmos.Table;
using Services.Entities;
using System.Collections.Generic;
using System.Linq;

namespace Hosts.JobScheduler
{
    public class GetJobsSegmentedRequest
    {
        public AsyncPageable<SyncJob> PageableQueryResult { get; set; }
        public string ContinuationToken;
    }
}
